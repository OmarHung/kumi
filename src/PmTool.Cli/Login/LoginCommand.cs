using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Cli.Api;
using PmTool.Cli.Commands;
using PmTool.Cli.Output;
using PmTool.Cli.Settings;

namespace PmTool.Cli.Login;

/// <summary><c>kumi auth login</c> 與 <c>kumi agent login</c> 共用的流程：登入的是命令列的權杖還是本機代理的。</summary>
internal enum LoginTarget
{
    Cli,
    Agent,
}

internal static class LoginCommand
{
    /// <summary>命令列的權杖預設要的範圍；<c>agent:run</c> 是本機代理的權杖專用，不能用 --scope 要。</summary>
    internal static readonly IReadOnlyList<string> DefaultScopes = ["items:read", "items:write", "blueprint:read"];

    private static readonly string[] AllowedScopes = ["items:read", "items:write", "blueprint:read", "blueprint:write"];

    public static async Task<int> RunAsync(CommandCall call, LoginTarget target)
    {
        var mode = ChooseMode(call);
        var server = await ResolveServerAsync(call);
        Credentials.EnsureTransportAllowed(call.Context, call.Arguments, server);
        return mode == LoginMode.Browser
            ? await BrowserAsync(call, server, target)
            : await PasteAsync(call, server, target, mode == LoginMode.Stdin);
    }

    /// <summary>伺服器：--server、環境變數、設定檔，依序第一個有的；都沒有就問（沒有終端機時是用法錯誤）。</summary>
    internal static async Task<string> ResolveServerAsync(CommandCall call)
    {
        var context = call.Context;
        var stored = new ConfigStore(context.ConfigPath).Load();
        return Credentials.Normalize(
            call.Arguments.Value("server")
            ?? Credentials.ServerFromEnvironment(context)?.Value
            ?? stored.Server
            ?? await PromptAsync(call, "伺服器網址（例如 https://pm.example.com）："));
    }

    private static LoginMode ChooseMode(CommandCall call)
    {
        var (withToken, paste) = (call.Arguments.Has("with-token"), call.Arguments.Has("paste"));
        if (withToken && paste)
        {
            throw CliException.Usage("--paste 與 --with-token 只能選一個：--paste 是在終端機貼上權杖，--with-token 是從標準輸入讀。");
        }

        var mode = withToken ? LoginMode.Stdin : paste ? LoginMode.Paste : call.Context.IsInteractive ? LoginMode.Browser : LoginMode.None;
        if (mode == LoginMode.None)
        {
            throw CliException.Usage("沒有終端機可以開瀏覽器登入，也沒辦法貼上權杖。請加 --with-token，從標準輸入讀取（例如 echo $TOKEN | kumi auth login --with-token）。");
        }

        if (mode != LoginMode.Browser && (call.Arguments.Has("scope") || call.Arguments.Has("no-browser")))
        {
            throw CliException.Usage("--scope 與 --no-browser 只有瀏覽器登入用，不能和 --paste、--with-token 一起用。");
        }

        return mode;
    }

    private static async Task<int> BrowserAsync(CommandCall call, string server, LoginTarget target)
    {
        var store = new ConfigStore(call.Context.ConfigPath);
        var stored = await StoredTokens.AdoptOldAgentTokenAsync(call, store.Load(), server);
        var kind = target == LoginTarget.Agent ? LoginToken.Agent : LoginToken.Cli;
        var scopes = target == LoginTarget.Cli ? ParseScopes(call.Arguments.Values("scope")) : [];
        var request = new BrowserLoginRequest(
            server, [kind], scopes, StoredTokens.ReplaceCandidates(stored, server, [kind]), call.Arguments.Has("no-browser"));

        var result = await new BrowserLogin(call.Context).RunAsync(request, call.Error, call.CancellationToken);
        if (target == LoginTarget.Agent && result.AgentSecret is null)
        {
            throw new CliException(ExitCodes.Error, "組織沒有開放成員使用自己的 AI 帳號，沒有建立本機代理的權杖。請洽組織管理員。");
        }

        store.Save(StoredTokens.Merge(stored, server, result));
        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, ResultJson(server, result));
            return ExitCodes.Success;
        }

        await call.Out.WriteLineAsync($"已登入 {server}，身分：{result.Describe}。");
        if (target == LoginTarget.Agent)
        {
            await call.Out.WriteLineAsync("本機代理的權杖已存好。執行 kumi agent start 開始，或 kumi agent enable 設成登入電腦時自動啟動。");
        }

        await WarnAboutEnvironmentTokenAsync(call);
        return ExitCodes.Success;
    }

    private static async Task<int> PasteAsync(CommandCall call, string server, LoginTarget target, bool fromStandardInput)
    {
        var context = call.Context;
        var token = await ReadTokenAsync(call, fromStandardInput);
        if (!token.StartsWith(Credentials.TokenPrefix, StringComparison.Ordinal) || token.Any(char.IsWhiteSpace))
        {
            throw CliException.Usage($"這看起來不是存取權杖：權杖是 {Credentials.TokenPrefix} 開頭的一段文字。請到網頁「我的帳號 → 存取權杖」建立。");
        }

        var session = await new ApiClient(context.Http, server, token).GetAsync("session", null, call.CancellationToken);
        if (session.Get("user") is null)
        {
            throw new CliException(ExitCodes.Error, $"{server} 的回應不像 Kumi 的伺服器（沒有說登入的是誰），沒有存權杖。請確認伺服器網址。");
        }

        if (target == LoginTarget.Agent && session.Get("scopes") is { } granted && !HasScope(granted, AccessTokenAgentScope))
        {
            throw new CliException(ExitCodes.Error, $"這把權杖沒有 {AccessTokenAgentScope} 範圍，不能給本機代理用。請到網頁「AI 帳號」建立本機代理用的權杖。");
        }

        var store = new ConfigStore(context.ConfigPath);
        var stored = store.Load();
        var sameServer = stored.Server is not null && Credentials.SameServer(stored.Server, server);
        store.Save(target == LoginTarget.Cli
            ? new StoredConfig { Server = server, Token = token, AgentToken = sameServer ? stored.AgentToken : null }
            : new StoredConfig { Server = server, Token = sameServer ? stored.Token : null, AgentToken = token });

        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, session);
            return ExitCodes.Success;
        }

        await call.Out.WriteLineAsync($"已登入 {server}，身分：{Describe(session)}。");
        await WarnAboutEnvironmentTokenAsync(call);
        return ExitCodes.Success;
    }

    internal const string AccessTokenAgentScope = "agent:run";

    internal static bool HasScope(JsonElement scopes, string scope) =>
        scopes.ValueKind == JsonValueKind.Array && scopes.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && item.GetString() == scope);

    internal static string Describe(JsonElement session)
    {
        var user = session.Get("user") ?? default;
        return $"{user.Get("displayName").Display()}（{user.Get("email").Display()}）";
    }

    private static JsonObject ResultJson(string server, BrowserLoginResult result) => new()
    {
        ["server"] = server,
        ["user"] = new JsonObject
        {
            ["displayName"] = result.DisplayName,
            ["email"] = result.Email,
            ["organizationName"] = result.OrganizationName,
        },
        ["cliToken"] = result.CliSecret is not null,
        ["agentToken"] = result.AgentSecret is not null,
        ["agentSkipped"] = result.AgentSkipped,
    };

    private static IReadOnlyList<string> ParseScopes(IReadOnlyList<string> requested)
    {
        if (requested.Count == 0)
        {
            return DefaultScopes;
        }

        // 一個 --scope 可以用逗號放好幾個，重複的合併。
        var scopes = requested.SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Distinct().ToArray();
        if (scopes.Contains(AccessTokenAgentScope))
        {
            throw CliException.Usage($"{AccessTokenAgentScope} 是本機代理的權杖專用的範圍，請用 kumi agent login 登入本機代理。");
        }

        var unknown = scopes.FirstOrDefault(scope => !AllowedScopes.Contains(scope));
        return unknown is not null
            ? throw CliException.Usage($"不認得的範圍 {unknown}。可以用：{string.Join("、", AllowedScopes)}。")
            : scopes.Length == 0 ? DefaultScopes : scopes;
    }

    private static async Task WarnAboutEnvironmentTokenAsync(CommandCall call)
    {
        if (Credentials.TokenFromEnvironment(call.Context) is { } fromEnvironment)
        {
            await call.Error.WriteLineAsync($"注意：環境變數 {fromEnvironment.Variable} 有設定，指令會優先用它，不是剛存的這一把。");
        }
    }

    private static async Task<string> ReadTokenAsync(CommandCall call, bool fromStandardInput)
    {
        var context = call.Context;
        if (fromStandardInput)
        {
            return (await context.In.ReadToEndAsync(call.CancellationToken)).Trim();
        }

        if (!context.IsInteractive || context.ReadSecret is null)
        {
            throw CliException.Usage("沒有終端機可以輸入權杖。請加 --with-token，從標準輸入讀取（例如 echo $TOKEN | kumi auth login --with-token）。");
        }

        await context.Error.WriteAsync("貼上存取權杖（輸入時不會顯示）：");
        return context.ReadSecret()?.Trim() ?? throw new CliException(ExitCodes.Error, "已取消。");
    }

    private static async Task<string> PromptAsync(CommandCall call, string question)
    {
        if (!call.Context.IsInteractive)
        {
            throw CliException.Usage($"請用 --server 或環境變數 {Credentials.ServerVariable} 指定伺服器網址。");
        }

        await call.Context.Error.WriteAsync(question);
        return (await call.Context.In.ReadLineAsync(call.CancellationToken))?.Trim() ?? string.Empty;
    }

    private enum LoginMode
    {
        None,
        Browser,
        Paste,
        Stdin,
    }
}
