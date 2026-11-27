namespace PmTool.Ai.Assistant;

/// <summary>助理的工具名稱。網頁照名稱顯示說法，不能改名。</summary>
internal static class AssistantToolNames
{
    public const string GetBlueprint = "get_blueprint";

    public const string ListTemplates = "list_templates";

    public const string GetTemplate = "get_template";

    public const string DescribeUsage = "describe_usage";

    public const string PreviewChangeSet = "preview_changeset";

    public const string ProposeChangeSet = "propose_changeset";

    public const string GetItem = "get_item";

    public const string SearchItems = "search_items";

    public const string ProposeItemChanges = "propose_item_changes";

    public const string ProposeNewItems = "propose_new_items";

    public const string GetProject = "get_project";

    public const string ListPeople = "list_people";

    public const string ProposeMemberChanges = "propose_member_changes";

    public const string ProposeItemDeletions = "propose_item_deletions";

    public const string ProposeBulkItemChanges = "propose_bulk_item_changes";

    public const string WithdrawProposal = "withdraw_proposal";

    /// <summary>使用者自己負責或建立的項目，跨他看得到的所有專案（只回他本來就看得到的，所以專案對話與組織對話都能用）。</summary>
    public const string MyWork = "my_work";

    /// <summary>跨專案查項目，照使用者的權限（<see cref="IAssistantItemFinder"/>）。</summary>
    public const string FindItems = "find_items";

    public const string ListProjects = "list_projects";

    public const string ProposeProject = "propose_project";
}
