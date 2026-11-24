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
        new("auth", "logout", "刪掉這台電腦上存的權杖", "kumi auth logout [--json]", [], 0, 0, LogoutAsync),
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
        var cleared = new ConfigStore(call.Context.ConfigPath).ClearTokens();
        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, new JsonObject { ["removed"] = cleared.Any, ["cliToken"] = cleared.Cli, ["agentToken"] = cleared.Agent });
            return ExitCodes.Success;
        }

        await call.Out.WriteLineAsync(cleared switch
        {
            { Cli: true, Agent: true } => "已刪掉這台電腦上存的權杖（命令列的與本機代理的）。它們還沒有被撤銷：要讓它們失效，請到網頁「我的帳號 → 存取權杖」撤銷。",
            { Agent: true } => "已刪掉這台電腦上存的本機代理的權杖。它還沒有被撤銷：要讓它失效，請到網頁「我的帳號 → 存取權杖」撤銷。",
            { Cli: true } => "已刪掉這台電腦上存的權杖。它還沒有被撤銷：要讓它失效，請到網頁「我的帳號 → 存取權杖」撤銷。",
            _ => "這台電腦上沒有存權杖。",
        });
        if (AutostartBackends.For(call.Context.Autostart)?.IsInstalled() == true)
        {
            await call.Out.WriteLineAsync("背景的本機代理還在執行：要停掉請執行 kumi agent disable。");
        }

        if (Credentials.TokenFromEnvironment(call.Context) is { } fromEnvironment)
        {
            await call.Error.WriteLineAsync($"注意：環境變數 {fromEnvironment.Variable} 還設定著，指令仍會用它。");
        }

        return ExitCodes.Success;
    }

    private static string Describe(System.Text.Json.JsonElement session) => LoginCommand.Describe(session);

    private static string SourceName(CommandCall call) => call.Credentials.Source == TokenSource.Environment
        ? $"環境變數 {Credentials.TokenVariable}"
        : $"設定檔 {call.Context.ConfigPath}";
}
