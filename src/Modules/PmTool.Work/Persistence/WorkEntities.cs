namespace PmTool.Work.Persistence;

/// <summary>
/// 資料表的一列。只給這個模組的資料存取用，對外一律轉成合約的型別。
/// 列舉存成文字或數字由對照程式負責，這裡保持資料表本來的形狀。
/// </summary>
internal sealed record ProjectEntity
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Template { get; init; }
    public required long CurrentBlueprintVersion { get; init; }

    /// <summary>最後發出去的項目流水號。新增項目時在同一個交易裡加一，同時新增也不會重複。</summary>
    public required long LastItemNumber { get; init; }

    /// <summary>最後發出去的變更集流水號，和項目的流水號一樣在送出的交易裡加一。</summary>
    public required long LastChangeSetNumber { get; init; }

    public required Guid CreatedBy { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>封存的時刻；沒封存是 null。</summary>
    public DateTimeOffset? ArchivedAt { get; init; }

    public Guid? ArchivedBy { get; init; }
}

/// <summary>專案說明。只有寫過的專案才有一列；清掉說明時 Text 是 null，列留著記下誰在何時清的。</summary>
internal sealed record ProjectDescriptionEntity
{
    public required Guid ProjectId { get; init; }
    public required Guid OrganizationId { get; init; }
    public string? Text { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public required Guid UpdatedBy { get; init; }
}

/// <summary>組織自己的說明格式。內建的不在這張表裡。</summary>
internal sealed record DescriptionTemplateEntity
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public required string Name { get; init; }
    public required string Body { get; init; }
    public required bool IsDefault { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required Guid CreatedBy { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public required Guid UpdatedBy { get; init; }
}

internal sealed record BlueprintVersionEntity
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public required Guid ProjectId { get; init; }
    public required long Version { get; init; }

    /// <summary>整份藍圖文件（jsonb），用 BlueprintSerializer 寫出來的形狀。</summary>
    public required string Document { get; init; }

    public required Guid CreatedBy { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

internal sealed record ProjectMemberEntity
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public required Guid ProjectId { get; init; }

    /// <summary>身分模組的人員編號。模組之間不建外鍵。</summary>
    public required Guid PersonId { get; init; }

    public required string Role { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

internal sealed record ItemEntity
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public required Guid ProjectId { get; init; }
    public required long Number { get; init; }
    public required string Type { get; init; }
    public required string Title { get; init; }
    public required string State { get; init; }

    /// <summary>目前狀態的類別（snake_case），跨專案的「我的工作」不用讀藍圖就能篩。</summary>
    public required string StateCategory { get; init; }

    public Guid? AssigneeId { get; init; }

    /// <summary>優先順序的等級，0 是沒有設定；存成數字才能直接依它排序。</summary>
    public required short Priority { get; init; }

    public DateOnly? StartDate { get; init; }
    public DateOnly? DueDate { get; init; }
    public decimal? Estimate { get; init; }
    public Guid? ParentId { get; init; }

    /// <summary>排進的週期；週期被刪除時資料庫把它設成 null（外鍵的 SET NULL）。</summary>
    public Guid? CycleId { get; init; }

    public Guid? MilestoneId { get; init; }

    /// <summary>說明，可能很長；清單不讀這一欄。去掉頭尾空白後是空的就存 null。</summary>
    public string? Description { get; init; }

    /// <summary>自訂欄位的值（jsonb），以欄位的 key 為鍵；沒有值的欄位不出現。</summary>
    public required string Fields { get; init; }

    public required Guid CreatedBy { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>刪除（封存）的時刻；null 是沒刪除。刪除的項目資料留著，但所有的讀取都當它不存在。</summary>
    public DateTimeOffset? DeletedAt { get; init; }

    public Guid? DeletedBy { get; init; }
}

/// <summary>
/// 項目的一筆活動。和造成它的修改寫在同一個交易裡；寫進來之後不再改，只有留言的內容與編輯、刪除的三欄例外。
/// 種類以外的內容（改了哪些欄位、從哪個狀態到哪個狀態、留言）依種類而不同，放在一個 jsonb 欄位。
/// </summary>
internal sealed record ItemActivityEntity
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid ItemId { get; init; }

    /// <summary>做這件事的人（身分模組的人員編號）。</summary>
    public required Guid ActorId { get; init; }

    /// <summary>種類（snake_case），例如 updated。</summary>
    public required string Kind { get; init; }

    public required DateTimeOffset At { get; init; }

    /// <summary>依種類而不同的內容（jsonb），形狀見 ActivityContent。</summary>
    public required string Content { get; init; }

    /// <summary>回覆的對象：同一個項目的最上層留言（回覆只有一層）。最上層的留言與其他活動是 null。</summary>
    public Guid? ReplyToId { get; init; }

    /// <summary>從別的系統搬過來的歷史：來源的名字（例如 monday）；這裡建立的是 null。</summary>
    public string? ImportedFrom { get; init; }

    /// <summary>只有留言用：最後一次編輯的時間；沒編輯過是 null。</summary>
    public DateTimeOffset? EditedAt { get; init; }

    /// <summary>只有留言用：刪除的時間。內容與版本照舊留著，只是不給人讀；復原時清成 null。</summary>
    public DateTimeOffset? DeletedAt { get; init; }

    /// <summary>刪除的人（身分模組的人員編號）；沒刪除是 null。</summary>
    public Guid? DeletedBy { get; init; }
}

/// <summary>
/// 留言編輯之前的舊版本（只存舊的，現在的內容照舊在活動的 content 裡）。現在的版本號是舊版本的數量加一。
/// </summary>
internal sealed record CommentRevisionEntity
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public required Guid ItemId { get; init; }

    /// <summary>留言的活動編號。</summary>
    public required Guid CommentId { get; init; }

    /// <summary>版本號，從 1 起算。</summary>
    public required int Number { get; init; }

    public required string Body { get; init; }

    /// <summary>這個版本成為留言內容的時間。</summary>
    public required DateTimeOffset WrittenAt { get; init; }
}

/// <summary>
/// 一份送出的變更集。送出時就存，之後只改狀態與決定的人、時刻；文件本身不再變。
/// </summary>
internal sealed record ChangeSetEntity
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public required Guid ProjectId { get; init; }

    /// <summary>專案裡的流水號，從 1 開始。</summary>
    public required long Number { get; init; }

    /// <summary>狀態（snake_case），例如 pending。</summary>
    public required string Status { get; init; }

    public required long BaseVersion { get; init; }
    public string? Summary { get; init; }

    /// <summary>整份變更集（jsonb），用 BlueprintSerializer 寫出來的形狀，讀回來再驗證一次就是同一份。</summary>
    public required string Document { get; init; }

    public required Guid ProposedBy { get; init; }
    public required DateTimeOffset ProposedAt { get; init; }

    /// <summary>從哪裡提出的：null 是人自己送的，<c>assistant</c> 是 AI 助理。舊的資料是 null。</summary>
    public string? ProposedVia { get; init; }

    public Guid? DecidedBy { get; init; }
    public DateTimeOffset? DecidedAt { get; init; }
    public long? AppliedVersion { get; init; }
}

/// <summary>
/// 從項目上拿掉的欄位值：套用變更集時欄位被刪除、轉換失敗而指定清空，或項目改了類型、新類型沒有這個欄位。
/// 不在畫面上顯示，留作之後的還原。來源是變更集或改類型的那一筆活動，剛好一個有值（資料表有檢查限制）。
/// </summary>
internal sealed record ArchivedFieldValueEntity
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid ItemId { get; init; }

    /// <summary>欄位的 key。</summary>
    public required string Field { get; init; }

    /// <summary>拿掉之前的值（jsonb），形狀和項目上存的一樣。</summary>
    public required string Value { get; init; }

    /// <summary>套用時拿掉這個值的變更集。</summary>
    public Guid? ChangeSetId { get; init; }

    /// <summary>改類型時拿掉這個值的那一筆 type_changed 活動。</summary>
    public Guid? ItemActivityId { get; init; }

    public required DateTimeOffset ArchivedAt { get; init; }
}

/// <summary>
/// 事件流的一筆（對外的形狀見 <see cref="WorkEvent"/>）。和造成它的寫入在同一個交易裡新增，寫進來之後不再改，
/// 所以執行的角色只有新增與讀取的權限。事件先不清除。
/// </summary>
internal sealed record WorkEventEntity
{
    /// <summary>流水號，資料庫發。只用它排序會漏掉晚提交的交易，讀的順序是（交易編號、流水號）。</summary>
    public long Sequence { get; init; }

    /// <summary>寫入的交易編號（xid8），資料庫用 pg_current_xact_id() 填；讀的方式見 WorkEventStream。</summary>
    public ulong TransactionId { get; init; }

    public required Guid EventId { get; init; }
    public required Guid OrganizationId { get; init; }

    /// <summary>種類（snake_case），例如 item_created。</summary>
    public required string Kind { get; init; }

    public required Guid ActorId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>事件發生當時的樣子（jsonb），形狀是合約的 <see cref="ItemEventData"/>。</summary>
    public required string Content { get; init; }
}

/// <summary>個人儲存的檢視。只有 owner_id 那個人看得到。</summary>
internal sealed record SavedViewEntity
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }

    /// <summary>null 是「我的工作」的檢視：不屬於任何專案。</summary>
    public required Guid? ProjectId { get; init; }

    /// <summary>身分模組的人員編號。模組之間不建外鍵。</summary>
    public required Guid OwnerId { get; init; }

    public required string Name { get; init; }

    /// <summary>table 或 board。</summary>
    public required string Layout { get; init; }

    /// <summary>篩選與排序條件（jsonb），形狀見 SavedViewMapping。</summary>
    public required string Query { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>專案裡一段有開始日與結束日的時間（Scrum 叫衝刺）。同一個專案的週期日期不重疊。</summary>
internal sealed record CycleEntity
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string Name { get; init; }
    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }

    /// <summary>身分模組的人員編號。模組之間不建外鍵。</summary>
    public required Guid CreatedBy { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>專案裡有到期日的目標。</summary>
internal sealed record MilestoneEntity
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string Name { get; init; }
    public required DateOnly DueDate { get; init; }
    public string? Description { get; init; }
    public required Guid CreatedBy { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// 項目的一個附件：資料庫只記檔案的資訊，檔案本身在儲存空間（鍵是 <see cref="Id"/> 的文字）。
/// 項目只會封存不會真的刪，外鍵擋得住孤兒。
/// </summary>
internal sealed record AttachmentEntity
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid ItemId { get; init; }
    public required string FileName { get; init; }

    /// <summary>已經整理過的類型（type/subtype）。</summary>
    public required string ContentType { get; init; }

    public required long SizeBytes { get; init; }

    /// <summary>內容的 SHA-256，小寫十六進位。</summary>
    public required string Sha256 { get; init; }

    /// <summary>身分模組的人員編號。模組之間不建外鍵。</summary>
    public required Guid UploadedBy { get; init; }

    public required DateTimeOffset UploadedAt { get; init; }
}

/// <summary>
/// 歷史匯入記下的外部編號：同一個來源、同一種東西（項目、留言、附件）的外部編號在組織裡只會出現一次，
/// 重跑時靠它認出哪些已經匯入過。項目編號固定記在 <see cref="ItemId"/>（留言、附件記它們所在的項目）。
/// </summary>
internal sealed record HistoryRefEntity
{
    public required Guid OrganizationId { get; init; }

    /// <summary>來源的名字（<see cref="HistorySource"/>）。</summary>
    public required string Source { get; init; }

    /// <summary>item、comment、attachment。</summary>
    public required string Kind { get; init; }

    public required string ExternalId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid ItemId { get; init; }

    /// <summary>項目是項目的編號，留言是活動的編號，附件是附件的編號。</summary>
    public required Guid TargetId { get; init; }

    /// <summary>執行匯入的人（替原作者署名的是他）；事後追查這一筆是誰搬進來的。</summary>
    public required Guid ImportedBy { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
