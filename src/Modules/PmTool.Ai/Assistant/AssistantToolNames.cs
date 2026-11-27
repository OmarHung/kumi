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

    /// <summary>一個項目的全部留言（含回覆與刪除的佔位），分頁讀。</summary>
    public const string ListComments = "list_comments";

    /// <summary>專案裡已刪除的項目（復原建議用）。</summary>
    public const string ListDeletedItems = "list_deleted_items";

    /// <summary>專案說明（資訊頁）。</summary>
    public const string GetProjectInfo = "get_project_info";

    /// <summary>專案的週期與里程碑（可以含已經結束的）。</summary>
    public const string GetPlanning = "get_planning";

    public const string ProposePlanningChanges = "propose_planning_changes";

    public const string ProposeCommentChanges = "propose_comment_changes";

    public const string ProposeItemRestores = "propose_item_restores";

    public const string ProposeProjectChanges = "propose_project_changes";

    /// <summary>知識庫裡看得到的空間。</summary>
    public const string ListSpaces = "list_spaces";

    /// <summary>一個知識庫空間的頁面樹（看得到的）。</summary>
    public const string GetPageTree = "get_page_tree";

    /// <summary>一頁知識庫的內容（Markdown）與 revision。</summary>
    public const string GetPage = "get_page";

    /// <summary>知識庫空間的垃圾桶（復原頁面用）。</summary>
    public const string ListPageTrash = "list_page_trash";

    /// <summary>存的檢視（專案的，或「我的工作」的）。</summary>
    public const string ListSavedViews = "list_saved_views";

    /// <summary>專案說明的說明格式。</summary>
    public const string ListDescriptionTemplates = "list_description_templates";

    /// <summary>專案總覽的數字（過期、快到期、停滯…）。</summary>
    public const string GetOverview = "get_overview";

    /// <summary>使用者自己的收件匣。</summary>
    public const string ListNotifications = "list_notifications";

    /// <summary>專案的變更集清單。</summary>
    public const string ListChangeSets = "list_changesets";

    public const string ProposeKnowledgeChanges = "propose_knowledge_changes";

    public const string ProposePeopleChanges = "propose_people_changes";

    public const string ProposeAttachmentChanges = "propose_attachment_changes";

    public const string ProposeSavedViewChanges = "propose_saved_view_changes";

    public const string ProposeDescriptionTemplateChanges = "propose_description_template_changes";

    public const string ListProjects = "list_projects";

    public const string ProposeProject = "propose_project";
}
