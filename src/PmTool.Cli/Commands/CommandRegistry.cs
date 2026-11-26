namespace PmTool.Cli.Commands;

internal static class CommandRegistry
{
    public static readonly IReadOnlyList<CommandSpec> All =
    [
        .. AuthCommands.Specs,
        .. ProjectCommands.Specs,
        .. ProjectDescriptionCommands.Specs,
        .. ItemCommands.Specs,
        .. CommentCommands.Specs,
        .. ItemRecoveryCommands.Specs,
        .. AttachmentCommands.Specs,
        .. SearchCommands.Specs,
        .. BlueprintCommands.Specs,
        .. ChangeSetCommands.Specs,
        .. ImportCommands.Specs,
        .. AgentCommands.Specs,
        .. AgentLoginCommands.Specs,
        .. HostCommands.Specs,
        .. UpdateCommand.Specs,
        .. UninstallCommand.Specs,
        .. LicensesCommand.Specs,
        .. SetupCommands.Specs,
        .. SkillCommands.Specs,
    ];
}
