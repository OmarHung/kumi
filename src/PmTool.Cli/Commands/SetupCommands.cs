using PmTool.Cli.Setup;

namespace PmTool.Cli.Commands;

/// <summary>
/// <c>kumi setup</c>：設定精靈（登入、Claude Code／Codex 的 MCP、本機代理與開機自動啟動）。
/// 規格見 internal/HANDOFF-setup-wizard.md 第 4.2 節，流程在 <see cref="SetupWizard"/>。
/// </summary>
internal static class SetupCommands
{
    public static readonly IReadOnlyList<CommandSpec> Specs =
    [
        new("setup", string.Empty, "設定精靈：登入、讓 Claude Code／Codex 使用 Kumi、接上本機代理",
            "kumi setup [--server 網址] [--no-browser]",
            [new OptionSpec("no-browser", "", "不自己開瀏覽器，只印網址")],
            0, 0, call => new SetupWizard(call).RunAsync()),
    ];
}
