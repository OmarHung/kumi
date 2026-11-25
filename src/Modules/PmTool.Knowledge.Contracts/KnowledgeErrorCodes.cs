namespace PmTool.Knowledge;

/// <summary>知識庫回的失敗代碼。代碼是對外合約，不能改名。</summary>
public static class KnowledgeErrorCodes
{
    /// <summary>看得到但不能做這件事（403）：不能改的空間、不是組織管理員卻要建組織空間、沒有 project.manage 卻要管專案空間。</summary>
    public const string Forbidden = "knowledge.forbidden";

    /// <summary>和其他清單查詢同一個代碼（422），網頁只要認一種。</summary>
    public const string QueryInvalid = "query.invalid";

    /// <summary>空間不存在、在別的組織，或呼叫者看不到（404）。看不到和不存在一樣。</summary>
    public const string SpaceNotFound = "knowledge.space_not_found";

    /// <summary>細項（422，路徑 key）：代號不合 <see cref="KnowledgeRules.SpaceKeyPattern"/>。</summary>
    public const string SpaceKeyInvalid = "knowledge.space_key_invalid";

    /// <summary>組織裡已經有這個代號的空間（409）。代號不分大小寫都存成大寫，封存的空間也算。</summary>
    public const string SpaceKeyTaken = "knowledge.space_key_taken";

    /// <summary>細項（422，路徑 name）：名稱空白，或有換行、控制字元、看不見的格式字元。</summary>
    public const string SpaceNameRequired = "knowledge.space_name_required";

    /// <summary>細項（422，路徑 name）。</summary>
    public const string SpaceNameTooLong = "knowledge.space_name_too_long";

    /// <summary>細項（422，路徑 description）：太長，或有 NUL、換行與 Tab 以外的控制字元。</summary>
    public const string SpaceDescriptionInvalid = "knowledge.space_description_invalid";

    /// <summary>這個專案已經有空間了（409）。一個專案最多一個空間。</summary>
    public const string SpaceProjectTaken = "knowledge.space_project_taken";

    /// <summary>組織的空間已經有 <see cref="KnowledgeRules.MaxSpacesPerOrganization"/> 個（409）。</summary>
    public const string SpaceLimitReached = "knowledge.space_limit_reached";

    /// <summary>
    /// 空間是唯讀的（409）：空間封存了，或它綁的專案封存了。和沒有權限分開，讓使用者知道是空間唯讀、不是自己的角色不夠。
    /// </summary>
    public const string SpaceArchived = "knowledge.space_archived";

    /// <summary>建立專案空間時，專案不存在或看不到（422，路徑 projectKey）。</summary>
    public const string ProjectNotFound = "knowledge.project_not_found";

    /// <summary>頁面不存在、在垃圾桶裡（讀取與修改時），或呼叫者看不到（404）。看不到和不存在一樣。</summary>
    public const string PageNotFound = "knowledge.page_not_found";

    /// <summary>細項（422，路徑 title）：標題空白，或有換行、控制字元、看不見的格式字元。</summary>
    public const string PageTitleRequired = "knowledge.page_title_required";

    /// <summary>細項（422，路徑 title）。</summary>
    public const string PageTitleTooLong = "knowledge.page_title_too_long";

    /// <summary>細項（422，路徑 body）。</summary>
    public const string PageBodyTooLong = "knowledge.page_body_too_long";

    /// <summary>細項（422，路徑 body）：有 NUL、換行與 Tab 以外的控制字元，或無效的字元。</summary>
    public const string PageBodyInvalid = "knowledge.page_body_invalid";

    /// <summary>細項（422，路徑 body）：內容裡有私鑰（和專案說明同一套判斷）。</summary>
    public const string PageBodySecret = "knowledge.page_body_secret";

    /// <summary>修改頁面沒帶 expectedRevision（422，路徑 expectedRevision）。只有 API 這一層會遇到：服務的參數一定有值。</summary>
    public const string PageRevisionRequired = "knowledge.page_revision_required";

    /// <summary>送來的 expectedRevision 和現在的不同：別人（或自己的另一個分頁）先存了（409），什麼都不改。</summary>
    public const string PageConflict = "knowledge.page_conflict";

    /// <summary>空間裡的頁面（不含垃圾桶裡的）已經有 <see cref="KnowledgeRules.MaxPagesPerSpace"/> 頁（409）。</summary>
    public const string PageLimitReached = "knowledge.page_limit_reached";

    /// <summary>放下去之後會超過 <see cref="KnowledgeRules.MaxDepth"/> 層（422，路徑 parentNumber）。</summary>
    public const string PageTooDeep = "knowledge.page_too_deep";

    /// <summary>
    /// 搬移或新增的位置不對（422）：上層是自己或自己底下的頁面、上層或「放在它前面」的頁面不存在或看不到（路徑 parentNumber 或 beforeNumber）、
    /// beforeNumber 不是那個上層底下的頁面。
    /// </summary>
    public const string PagePositionInvalid = "knowledge.page_position_invalid";

    /// <summary>
    /// 底下有呼叫者看不到的頁面（409）：不能刪除（免得刪掉自己看不到的內容）、搬移或改限制（會改變那些頁面誰看得到）。
    /// 刪除只算還沒刪的頁面；搬移與改限制連垃圾桶裡的後代也算，因為垃圾桶裡的頁面沿現在的上層算誰看得到、復原後繼承什麼。
    /// </summary>
    public const string PageHasHiddenDescendants = "knowledge.page_has_hidden_descendants";

    /// <summary>細項（422，路徑 people 或 people[i]）：人數超過 <see cref="KnowledgeRules.MaxRestrictedPeople"/>，或那個人不在組織裡。</summary>
    public const string RestrictionInvalid = "knowledge.restriction_invalid";

    /// <summary>附件不存在、在別的組織、所在的頁面看不到或在垃圾桶裡（404）。</summary>
    public const string AttachmentNotFound = "knowledge.attachment_not_found";

    /// <summary>檔案超過 <see cref="KnowledgeRules.MaxAttachmentBytes"/>（413）。</summary>
    public const string AttachmentTooLarge = "knowledge.attachment_too_large";

    /// <summary>頁面的附件已經有 <see cref="KnowledgeRules.MaxAttachmentsPerPage"/> 個（409）。</summary>
    public const string AttachmentLimitReached = "knowledge.attachment_limit_reached";

    /// <summary>細項（422，路徑 fileName）：規則和項目的附件相同。</summary>
    public const string AttachmentNameInvalid = "knowledge.attachment_name_invalid";

    /// <summary>組織的附件（項目與知識庫合計）已經到儲存空間的上限（409）。新的上傳被擋，讀取與下載照常。</summary>
    public const string AttachmentQuotaExceeded = "knowledge.attachment_quota_exceeded";

    /// <summary>這個人過去 24 小時上傳的量已經到每日上限（409）。</summary>
    public const string AttachmentDailyQuotaExceeded = "knowledge.attachment_daily_quota_exceeded";

    /// <summary>垃圾桶已經滿了（409）：批數到 <see cref="KnowledgeRules.MaxTrashBatchesPerSpace"/>，或頁數到 <see cref="KnowledgeRules.MaxTrashPagesPerSpace"/>。要空間的管理者先永久刪除幾批。</summary>
    public const string TrashFull = "knowledge.trash_full";

    /// <summary>只有設定這個限制的人或空間的管理者能放寬限制（拿掉限制、加人、搬出有限制的範圍）。對應 403。</summary>
    public const string RestrictionOwnerRequired = "knowledge.restriction_owner_required";
}
