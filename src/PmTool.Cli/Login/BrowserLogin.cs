using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using PmTool.Cli.Api;

namespace PmTool.Cli.Login;

internal enum LoginToken
{
    Cli,
    Agent,
}

/// <param name="Scopes">命令列的權杖要的範圍（同意頁預先勾好的）；沒有要命令列的權杖時是空的。</param>
/// <param name="ReplaceTokens">這台電腦之前存的、這次要換掉的權杖（伺服器兌換時撤銷屬於這個帳號的）。</param>
internal sealed record BrowserLoginRequest(
    string Server,
    IReadOnlyList<LoginToken> Tokens,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<string> ReplaceTokens,
    bool NoBrowser);

internal sealed record BrowserLoginResult(
    string DisplayName, string Email, string OrganizationName, string? CliSecret, string? AgentSecret, bool AgentSkipped)
{
    public string Describe => $"{DisplayName}（{Email}）";

    // 兩個祕密：record 預設的 ToString 會把它們印進記錄。
    public override string ToString() => $"BrowserLoginResult {{ {Describe} }}";
}

/// <summary>
/// 瀏覽器登入（internal/HANDOFF-setup-wizard.md 第 3.2 節）：開 127.0.0.1 的迴路、開瀏覽器到同意頁、等導回來的碼，
/// 再用 PKCE 的 verifier 換成權杖。這裡不存設定檔，由呼叫端決定怎麼存。
/// </summary>
internal sealed class BrowserLogin(CliContext context)
{
    private const int MaxMachineNameLength = 60;
    private const string FallbackMachineName = "computer";

    public async Task<BrowserLoginResult> RunAsync(BrowserLoginRequest request, TextWriter messages, CancellationToken cancellation)
    {
        var state = Pkce.RandomToken();
        var verifier = Pkce.RandomToken();
        using var receiver = new LoopbackReceiver(state);
        StartListening(receiver);
        var url = AuthorizeUrl(request, receiver.Port, state, Pkce.Challenge(verifier), MachineName());

        if (request.NoBrowser)
        {
            await messages.WriteLineAsync($"請用瀏覽器開啟下面的網址，並在 Kumi 的頁面按「允許」：{Environment.NewLine}{url}");
        }
        else
        {
            await messages.WriteLineAsync(
                $"正在開瀏覽器，請在 Kumi 的頁面按「允許」。瀏覽器沒有打開的話，把這個網址貼到瀏覽器：{Environment.NewLine}{url}");
            // 開不起來也沒關係：網址已經印在上面了。
            _ = context.Login.OpenBrowser(url);
        }

        var outcome = await receiver.WaitAsync(context.Login.Timeout, cancellation);
        return outcome.Kind switch
        {
            LoopbackKind.Code => await RedeemAsync(request, outcome.Code!, verifier, cancellation),
            LoopbackKind.Denied => throw new CliException(ExitCodes.Error, "你在瀏覽器上拒絕了，沒有登入。"),
            _ => throw new CliException(
                ExitCodes.Error, $"等了 {Math.Max(1, (int)context.Login.Timeout.TotalMinutes)} 分鐘，瀏覽器都沒有回應，已經放棄。請重新執行。"),
        };
    }

    internal static string AuthorizeUrl(BrowserLoginRequest request, int port, string state, string challenge, string machine)
    {
        var tokens = string.Join(",", request.Tokens.Select(token => token == LoginToken.Cli ? "cli" : "agent"));
        var parts = new List<string>
        {
            $"port={port}",
            $"state={Uri.EscapeDataString(state)}",
            $"challenge={Uri.EscapeDataString(challenge)}",
            $"machine={Uri.EscapeDataString(machine)}",
            $"tokens={Uri.EscapeDataString(tokens)}",
        };
        if (request.Scopes.Count > 0)
        {
            parts.Add($"scope={string.Join('+', request.Scopes.Select(Uri.EscapeDataString))}");
        }

        return $"{request.Server}/cli/authorize?{string.Join('&', parts)}";
    }

    private static void StartListening(LoopbackReceiver receiver)
    {
        try
        {
            receiver.Start();
        }
        catch (SocketException exception)
        {
            throw new CliException(ExitCodes.Error, $"沒辦法在這台電腦開登入用的連接埠（{exception.SocketErrorCode}）。可以改用 --paste，貼上在網頁建立的存取權杖。");
        }
    }

    /// <summary>
    /// 和伺服器同一組規則（控制字元、Format、行與段落分隔字元都不要），以 rune 截斷：
    /// 不然伺服器會整個拒絕，或在代理對中間切斷。濾完是空的用固定的名稱。
    /// </summary>
    private string MachineName()
    {
        var cleaned = string.Concat(context.Login.MachineName().EnumerateRunes().Where(IsAllowedRune).Select(rune => rune.ToString())).Trim();
        var truncated = string.Concat(cleaned.EnumerateRunes().Take(MaxMachineNameLength).Select(rune => rune.ToString())).TrimEnd();
        return truncated.Length == 0 ? FallbackMachineName : truncated;
    }

    private static bool IsAllowedRune(Rune rune) =>
        !Rune.IsControl(rune) && Rune.GetUnicodeCategory(rune)
            is not (UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator);

    private async Task<BrowserLoginResult> RedeemAsync(BrowserLoginRequest request, string code, string verifier, CancellationToken cancellation)
    {
        var body = new JsonObject
        {
            ["code"] = code,
            ["codeVerifier"] = verifier,
            ["replaceTokens"] = new JsonArray([.. request.ReplaceTokens.Select(token => (JsonNode?)JsonValue.Create(token))]),
        };
        // 兌換不用登入：空的權杖讓 ApiClient 不帶 Authorization。
        var data = await new ApiClient(context.Http, request.Server, string.Empty).PostAsync("cli-login/token", body, cancellation);
        var account = data.Get("account") ?? default;
        var cli = Secret(data, "cli");
        var agent = Secret(data, "agent");
        var skipped = data.Flag("agentSkipped");
        if ((request.Tokens.Contains(LoginToken.Cli) && cli is null) || (request.Tokens.Contains(LoginToken.Agent) && agent is null && !skipped))
        {
            throw new CliException(ExitCodes.Error, $"{request.Server} 的回應不像 Kumi 的伺服器（沒有給權杖），沒有登入。請確認伺服器網址。");
        }

        return new BrowserLoginResult(
            account.Display("displayName"), account.Display("email"), account.Display("organizationName"), cli, agent, skipped);
    }

    private static string? Secret(System.Text.Json.JsonElement data, string name)
    {
        var secret = data.Get(name)?.Text("secret");
        return secret is not null && secret.StartsWith(Settings.Credentials.TokenPrefix, StringComparison.Ordinal) && !secret.Any(char.IsWhiteSpace)
            ? secret
            : null;
    }
}
