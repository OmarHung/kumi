using PmTool.Cli.Login;

namespace PmTool.Cli.Commands;

/// <summary>
/// <c>kumi agent login</c>：登入，只要本機代理的權杖（存在 <see cref="Settings.StoredConfig.AgentToken"/>，不動命令列的那一把）。
/// 規格見 internal/HANDOFF-setup-wizard.md 第 4.1 節。
/// </summary>
internal static class AgentLoginCommands
{
    public static readonly IReadOnlyList<CommandSpec> Specs =
    [
        new("agent", "login", "登入並存下本機代理用的權杖（和命令列的權杖分開）",
            "kumi agent login [--server 網址] [--no-browser] [--paste] [--with-token]",
            [
                new OptionSpec("no-browser", "", "不自己開瀏覽器，只印網址（透過 SSH 時請用 --paste）"),
                new OptionSpec("paste", "", "改成在終端機貼上在網頁「AI 帳號」建立的本機代理權杖"),
                new OptionSpec("with-token", "", "從標準輸入讀權杖（不互動，給腳本用）"),
            ], 0, 0, call => LoginCommand.RunAsync(call, LoginTarget.Agent)),
    ];
}
