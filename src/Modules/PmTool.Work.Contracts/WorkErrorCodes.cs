namespace PmTool.Work;

/// <summary>工作引擎回報的失敗代碼。這些字串是對外合約，改名等於破壞相容性。</summary>
public static class WorkErrorCodes
{
    public const string Forbidden = "work.forbidden";

    public const string TemplateNotFound = "template.not_found";

    public const string ProjectNotFound = "project.not_found";
    public const string ProjectKeyInvalid = "project.key_invalid";
    public const string ProjectKeyTaken = "project.key_taken";
    public const string ProjectNameRequired = "project.name_required";
    public const string ProjectNameTooLong = "project.name_too_long";
    /// <summary>專案已封存，只能看不能改（寫入項目、藍圖、週期與里程碑）。要改先取消封存（project.manage）。</summary>
    public const string ProjectArchived = "project.archived";

    /// <summary>專案說明超過 <see cref="ProjectDescriptionRules.MaxTextLength"/> 字。</summary>
    public const string ProjectDescriptionTooLong = "project.description_too_long";

    /// <summary>專案說明裡有 NUL 或換行、Tab 以外的控制字元。</summary>
    public const string ProjectDescriptionInvalid = "project.description_invalid";

    /// <summary>專案說明裡有私鑰。成員都看得到、搜尋得到、備份裡也有，私鑰不能存在這裡。</summary>
    public const string ProjectDescriptionSecret = "project.description_secret";

    /// <summary>用 SetIfUnchangedAsync 改專案說明時，現在的更新時刻和預期的不同：別人在這之間改過（409）。什麼都沒改。</summary>
    public const string ProjectDescriptionConflict = "project.description_conflict";

    /// <summary>找不到這份說明格式：不存在、在別的組織，或是內建的（內建的不能改、不能刪）。</summary>
    public const string DescriptionTemplateNotFound = "description_template.not_found";

    public const string DescriptionTemplateNameRequired = "description_template.name_required";
    public const string DescriptionTemplateNameTooLong = "description_template.name_too_long";

    /// <summary>組織裡另一份或內建的說明格式已經用了這個名稱（不分大小寫）。</summary>
    public const string DescriptionTemplateNameTaken = "description_template.name_taken";

    /// <summary>說明格式的內容是空白。</summary>
    public const string DescriptionTemplateBodyRequired = "description_template.body_required";

    /// <summary>組織的說明格式已經到上限（<see cref="ProjectDescriptionRules.MaxTemplatesPerOrganization"/>）。</summary>
    public const string DescriptionTemplateLimitReached = "description_template.limit_reached";

    public const string MemberUnknownRole = "project.member_unknown_role";

    /// <summary>要移除的人不是這個專案的成員。</summary>
    public const string MemberNotFound = "project.member_not_found";

    /// <summary>移除或降級之後專案就沒有 admin 了。</summary>
    public const string LastAdmin = "project.last_admin";

    public const string ItemNotFound = "item.not_found";
    public const string ItemUnknownType = "item.unknown_type";
    public const string ItemTitleRequired = "item.title_required";
    public const string ItemTitleTooLong = "item.title_too_long";
    public const string ItemUnknownField = "item.unknown_field";
    public const string ItemInvalidValue = "item.invalid_value";
    public const string ItemUnknownAssignee = "item.unknown_assignee";
    public const string ItemUnknownParent = "item.unknown_parent";
    public const string ItemParentNotAllowed = "item.parent_not_allowed";
    public const string ItemParentCycle = "item.parent_cycle";
    public const string ItemDescriptionTooLong = "item.description_too_long";

    /// <summary>改說明時帶的版本和現在的不同：別人在你編輯時改過說明（409）。什麼都沒改。</summary>
    public const string ItemDescriptionConflict = "item.description_conflict";

    /// <summary>刪除項目時它還有沒刪除的下層項目；Details 每個下層一筆，路徑是 children/下層的編號。</summary>
    public const string ItemHasChildren = "item.has_children";

    /// <summary>改類型之後，有下層項目的類型不允許掛在新類型底下；Details 每個這樣的下層一筆，路徑是 children/下層的編號。</summary>
    public const string ItemChildrenNotAllowed = "item.children_not_allowed";

    /// <summary>復原項目時它的上層也是刪除的；Details 一筆，路徑是 parent，訊息帶上層的編號。先復原上層。</summary>
    public const string ItemParentDeleted = "item.parent_deleted";

    /// <summary>復原項目時藍圖已經沒有它的類型（刪除之後被變更集拿掉了），無法復原。</summary>
    public const string ItemTypeRemoved = "item.type_removed";

    /// <summary>批次操作的請求本身不合格（編號是空的、超過上限、狀態和其他欄位一起給、什麼都沒給）；Details 的路徑是 keys、state 或屬性名稱。</summary>
    public const string BulkInvalid = "item.bulk_invalid";

    /// <summary>
    /// 匯入的內容不合格：請求本身（沒有列、超過 <see cref="ItemImport.MaxRows"/> 列，路徑是 rows），
    /// 或某幾列（Details 每個問題一筆，路徑是 rows/列的索引/欄位，例如 rows/3/due_date、rows/0/title、rows/7/state；欄位的寫法和新增項目的驗證錯誤一樣）。
    /// </summary>
    public const string ImportInvalid = "item.import_invalid";

    /// <summary>歷史匯入的請求本身不合格（來源的名字、查詢的外部編號太多…）；Details 的路徑指出是哪一個參數。逐筆的問題用 <see cref="ImportInvalid"/>。</summary>
    public const string HistoryInvalid = "history.invalid";

    public const string AttachmentNotFound = "attachment.not_found";

    /// <summary>附件超過 <see cref="AttachmentRules.MaxBytes"/>。</summary>
    public const string AttachmentTooLarge = "attachment.too_large";

    /// <summary>組織的附件（項目與知識庫合計）已經到儲存空間的上限。新的上傳被擋，讀取、下載、刪除照常。對應 409。</summary>
    public const string AttachmentQuotaExceeded = "attachment.quota_exceeded";

    /// <summary>這個人過去 24 小時上傳的量已經到每日上限。對應 409。</summary>
    public const string AttachmentDailyQuotaExceeded = "attachment.daily_quota_exceeded";

    /// <summary>項目的附件已經 <see cref="AttachmentRules.MaxPerItem"/> 個。</summary>
    public const string AttachmentLimitReached = "attachment.limit_reached";

    /// <summary>檔名不合格（見 <see cref="AttachmentRules"/>）；Details 的路徑是 fileName。</summary>
    public const string AttachmentNameInvalid = "attachment.name_invalid";

    public const string TransitionNotAllowed = "item.transition_not_allowed";
    public const string TransitionRoleRequired = "item.transition_role_required";
    public const string TransitionFieldsRequired = "item.transition_fields_required";

    public const string CommentBodyRequired = "comment.body_required";
    public const string CommentBodyTooLong = "comment.body_too_long";
    public const string CommentBodyInvalid = "comment.body_invalid";

    /// <summary>變更集沒通過驗證或計畫；Details 是 IssueCodes 的問題，路徑指向變更集文件。</summary>
    public const string ChangeSetInvalid = "changeset.invalid";
    public const string ChangeSetNotFound = "changeset.not_found";

    /// <summary>變更集已經套用、拒絕或過期，不能再套用或拒絕。</summary>
    public const string ChangeSetNotPending = "changeset.not_pending";

    /// <summary>變更集的基準版本不是目前的版本：別的變更集先套用了。這份會被標成過期。</summary>
    public const string ChangeSetOutdated = "changeset.outdated";

    /// <summary>現在的資料讓這份變更集套用不了；Details 列出每一個原因，代碼是下面幾個。</summary>
    public const string ChangeSetApplyBlocked = "changeset.apply_blocked";
    public const string ChangeSetItemsOfRemovedType = "changeset.items_of_removed_type";
    public const string ChangeSetItemsUnderRemovedParent = "changeset.items_under_removed_parent";
    public const string ChangeSetMembersWithRemovedRole = "changeset.members_with_removed_role";
    public const string ChangeSetConversionFailed = "changeset.conversion_failed";
    public const string ChangeSetTooManyItems = "changeset.too_many_items";

    /// <summary>專案的項目一直有人在改，套用等不到鎖。稍後再試。</summary>
    public const string ChangeSetBusy = "changeset.busy";

    public const string QueryInvalid = "query.invalid";

    /// <summary>沒有這個檢視，或它是別人的（一律當成不存在）。</summary>
    public const string SavedViewNotFound = "saved_view.not_found";

    /// <summary>自己在這個專案已經有同名的檢視（不分大小寫）。</summary>
    public const string SavedViewNameTaken = "saved_view.name_taken";

    /// <summary>自己在這個專案的檢視已經到上限（<see cref="SavedViewRules.MaxPerProject"/>）。</summary>
    public const string SavedViewLimitReached = "saved_view.limit_reached";

    /// <summary>檢視的欄位不合格式；Details 的路徑是 name、layout、query.due……。</summary>
    public const string SavedViewInvalid = "saved_view.invalid";

    public const string CycleNotFound = "cycle.not_found";
    public const string CycleNameTaken = "cycle.name_taken";

    /// <summary>日期和同一個專案的另一個週期重疊（含頭尾）；訊息寫明是哪一個。</summary>
    public const string CycleOverlaps = "cycle.overlaps";
    public const string CycleLimitReached = "cycle.limit_reached";

    public const string MilestoneNotFound = "milestone.not_found";
    public const string MilestoneNameTaken = "milestone.name_taken";
    public const string MilestoneLimitReached = "milestone.limit_reached";

    /// <summary>週期或里程碑的欄位不合格式；Details 的路徑是 name、startDate、endDate、dueDate、description。</summary>
    public const string PlanningInvalid = "planning.invalid";

    /// <summary>
    /// 要回覆、編輯、刪除、復原或看版本的留言不存在、不是這個項目的、或不是留言。對應 404。
    /// 都是同一個答案，不透露別的項目有什麼。
    /// </summary>
    public const string CommentNotFound = "comment.not_found";

    /// <summary>
    /// 這則留言不是你的：只有作者能編輯；刪除別人的留言、復原別人刪的留言要有 project.manage。對應 403。
    /// </summary>
    public const string CommentNotAuthor = "comment.not_author";

    /// <summary>留言已經刪除：先復原才能編輯、回覆或看版本。對應 409。</summary>
    public const string CommentDeleted = "comment.deleted";

    /// <summary>這則留言已經有 <see cref="WorkRules.MaxCommentRevisions"/> 個版本，不能再編輯。對應 409。</summary>
    public const string CommentTooManyEdits = "comment.too_many_edits";

    /// <summary>專案裡等待中的變更集已經有 <see cref="WorkRules.MaxPendingChangeSetsPerProject"/> 份，要先套用或放棄幾份才能再提出。對應 409。</summary>
    public const string ChangeSetTooManyPending = "changeset.too_many_pending";

    /// <summary>從其他系統匯入的留言不能編輯。對應 409。</summary>
    public const string CommentImported = "comment.imported";

    /// <summary>找不到這個人，或他不在這個組織裡。對應 404，值和 API 的 person.not_found 相同。</summary>
    public const string PersonNotFound = "person.not_found";
}
