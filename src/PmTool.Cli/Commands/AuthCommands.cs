using System.Text.Json.Nodes;
using PmTool.Cli.Agent.Autostart;
using PmTool.Cli.Api;
using PmTool.Cli.Login;
using PmTool.Cli.Output;
using PmTool.Cli.Settings;

namespace PmTool.Cli.Commands;

/// <summary>登入（瀏覽器、貼上權杖）、看目前是誰、登出。登入的流程在 <see cref="LoginCommand"/>。</summary>
internal static class AuthCommands
{
    public static readonly IReadOnlyList<CommandSpec> Specs =
    [
        new("auth", "login", "登入：預設開瀏覽器按「允許」，不必複製權杖",
            "kumi auth login [--server 網址] [--scope 範圍]… [--no-browser] [--paste] [--with-token]",
            [
                new OptionSpec("scope", "範圍", "瀏覽器登入時權杖要的範圍（items:read、items:write、blueprint:read、blueprint:write；預設前三個）；可以重複", Repeatable: true),
                new OptionSpec("no-browser", "", "不自己開瀏覽器，只印網址（透過 SSH 時請用 --paste）"),
                new OptionSpec("paste", "", "改成在終端機貼上在網頁建立的存取權杖"),
                new OptionSpec("with-token", "", "從標準輸入讀權杖（不互動，給腳本用）"),
            ], 0, 0, call => LoginCommand.RunAsync(call, LoginTarget.Cli)),
        new("auth", "status", "顯示伺服器與目前登入的是誰", "kumi auth status [--json]", [], 0, 0, StatusAsync),
        new("auth", "logout", "刪掉這台電腦上存的權杖；可以一併撤銷伺服器上的權杖並停掉背景的本機代理",
            "kumi auth logout [--revoke] [--json]",
            [new OptionSpec("revoke", "", "一併撤銷伺服器上的權杖（命令列與本機代理的兩把）並停掉背景的本機代理；有終端機時不加會問，--json 或沒有終端機時不加就只刪本機")],
            0, 0, LogoutAsync),
    ];

    private static async Task<int> StatusAsync(CommandCall call)
    {
        var api = call.Api;
        var session = await api.GetAsync("session", null, call.CancellationToken);
        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, session);
            return ExitCodes.Success;
        }

        var user = session.Get("user") ?? default;
        await call.Out.WriteLineAsync($"伺服器：{api.Server}");
        await call.Out.WriteLineAsync($"登入身分：{Describe(session)}");
        await call.Out.WriteLineAsync($"組織：{user.Get("organizationName").Display()}（{Labels.OrganizationRole(user.Text("role"))}）");
        await call.Out.WriteLineAsync($"權杖來源：{SourceName(call)}");
        if (session.Get("scopes") is { } scopes)
        {
            await call.Out.WriteLineAsync($"權杖範圍：{scopes.Display()}");
        }

        var hasAgentToken = new ConfigStore(call.Context.ConfigPath).Load().AgentToken is not null;
        await call.Out.WriteLineAsync($"本機代理的權杖：{(hasAgentToken ? "已存" : "沒有（要用本機代理請執行 kumi agent login）")}");

        return ExitCodes.Success;
    }

    private static async Task<int> LogoutAsync(CommandCall call)
    {
        var store = new ConfigStore(call.Context.ConfigPath);
        var stored = store.Load();
        var agentInstalled = AutostartBackends.For(call.Context.Autostart)?.IsInstalled() == true;
        var revoke = await ShouldRevokeAsync(call, stored, agentInstalled);

        // 先停背景的代理、再撤銷：它還在跑的話，下一次輪詢會拿著剛撤銷的權杖得到 401，記錄裡留下一堆錯誤。
        var agentStopped = revoke && agentInstalled && await StopBackgroundAgentAsync(call);
        var revocation = revoke ? await TokenRevocation.RevokeAsync(call, stored) : TokenRevocation.None;
        var cleared = store.ClearTokens();
        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, new JsonObject
            {
                ["removed"] = cleared.Any,
                ["cliToken"] = cleared.Cli,
                ["agentToken"] = cleared.Agent,
                ["revoked"] = new JsonObject { ["cliToken"] = revocation.Cli, ["agentToken"] = revocation.Agent },
                ["revokeFailures"] = new JsonArray([.. revocation.Failures.Select(failure => (JsonNode?)JsonValue.Create(failure))]),
                ["agentStopped"] = agentStopped,
            });
            return ExitCodes.Success;
        }

        await call.Out.WriteLineAsync(Summary(cleared, revocation, revoke));
        foreach (var failure in revocation.Failures)
        {
            await call.Error.WriteLineAsync($"沒有撤銷成功：{failure}。本機的已經刪掉了；請到網頁「設定 → 命令列與整合」的「存取權杖」撤銷。");
        }

        if (agentInstalled && !agentStopped)
        {
            await call.Out.WriteLineAsync("背景的本機代理還在執行：要停掉請執行 kumi agent disable。");
        }

        if (Credentials.TokenFromEnvironment(call.Context) is { } fromEnvironment)
        {
            await call.Error.WriteLineAsync($"注意：環境變數 {fromEnvironment.Variable} 還設定著，指令仍會用它。");
        }

        return ExitCodes.Success;
    }

    /// <summary>--revoke 就撤銷；沒加時只有互動（有終端機、不是 --json）才問，而且有東西可以撤銷才問。</summary>
    private static async Task<bool> ShouldRevokeAsync(CommandCall call, StoredConfig stored, bool agentInstalled)
    {
        if (call.Arguments.Has("revoke"))
        {
            return true;
        }

        var hasSomething = stored.Token is not null || stored.AgentToken is not null || agentInstalled;
        if (call.Json || !call.Context.IsInteractive || !hasSomething)
        {
            return false;
        }

        return await new Setup.SetupPrompt(call.Context, call.CancellationToken).AskAsync(
            "要一併撤銷伺服器上的權杖（命令列與本機代理的），並停掉背景的本機代理嗎？");
    }

    /// <summary>呼叫現有的 <c>kumi agent disable</c>；--json 時它的輸出不能混進登出的 JSON，所以丟掉。</summary>
    private static async Task<bool> StopBackgroundAgentAsync(CommandCall call)
    {
        var context = call.Json ? call.Context.WithWriters(TextWriter.Null, call.Context.Error) : call.Context;
        var exit = await call.Context.Login.RunCommand(["agent", "disable"], context, call.CancellationToken);
        if (exit != ExitCodes.Success)
        {
            await call.Error.WriteLineAsync("背景的本機代理沒有停下來：請執行 kumi agent disable 看原因。");
        }

        return exit == ExitCodes.Success;
    }

    private static string Summary(ClearedTokens cleared, RevocationResult revocation, bool revoked)
    {
        var what = cleared switch
        {
            { Cli: true, Agent: true } => "已刪掉這台電腦上存的權杖（命令列的與本機代理的）。",
            { Agent: true } => "已刪掉這台電腦上存的本機代理的權杖。",
            { Cli: true } => "已刪掉這台電腦上存的權杖。",
            _ => "這台電腦上沒有存權杖。",
        };
        if (!cleared.Any)
        {
            return what;
        }

        var allRevoked = revoked && revocation.Failures.Count == 0;
        return allRevoked
            ? $"{what}伺服器上的權杖也撤銷了。"
            : revoked ? what : $"{what}它們還沒有被撤銷：要讓它們失效，請到網頁「設定 → 命令列與整合」的「存取權杖」撤銷（下次登出可以加 --revoke 一併撤銷）。";
    }

    private static string Describe(System.Text.Json.JsonElement session) => LoginCommand.Describe(session);

    private static string SourceName(CommandCall call) => call.Credentials.Source == TokenSource.Environment
        ? $"環境變數 {Credentials.TokenVariable}"
        : $"設定檔 {call.Context.ConfigPath}";
}
