namespace PmTool.Knowledge.Persistence;

/// <summary><c>knowledge.spaces</c> 的一列。代號存大寫；<see cref="ProjectId"/> 有值是專案空間。</summary>
internal sealed record SpaceRow
{
    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required string Key { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public Guid? ProjectId { get; init; }

    /// <summary>下一頁的編號（從 1 起）。編號不重複使用，所以只增不減。</summary>
    public required int NextPageNumber { get; init; }

    public required Guid CreatedBy { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required Guid UpdatedBy { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? ArchivedAt { get; init; }

    public Guid? ArchivedBy { get; init; }
}

/// <summary><c>knowledge.pages</c> 的一列。刪除是標記（<see cref="DeletedAt"/>），同一次刪除的頁面共用 <see cref="DeletionId"/>。</summary>
internal sealed record PageRow
{
    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid SpaceId { get; init; }

    public required int Number { get; init; }

    public Guid? ParentId { get; init; }

    public required int Position { get; init; }

    public required string Title { get; init; }

    public required string Body { get; init; }

    public required long Revision { get; init; }

    public required bool IsRestricted { get; init; }

    /// <summary>設定這一頁限制的人；沒有限制或舊資料查不出來時是 null。只有他與空間的管理者能放寬。</summary>
    public Guid? RestrictedBy { get; init; }

    public required Guid CreatedBy { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required Guid UpdatedBy { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? DeletedAt { get; init; }

    public Guid? DeletedBy { get; init; }

    public Guid? DeletionId { get; init; }
}

/// <summary><c>knowledge.page_restrictions</c> 的一列：這一頁自己的名單裡的一個人。</summary>
internal sealed record PageRestrictionRow
{
    public required Guid OrganizationId { get; init; }

    public required Guid PageId { get; init; }

    public required Guid PersonId { get; init; }

    /// <summary>加入的順序，從 0 起。</summary>
    public required int Position { get; init; }
}

/// <summary><c>knowledge.page_versions</c> 的一列。</summary>
internal sealed record PageVersionRow
{
    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid PageId { get; init; }

    public required int Number { get; init; }

    public required string Title { get; init; }

    public required string Body { get; init; }

    public required Guid AuthorId { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset SavedAt { get; init; }

    /// <summary>從第幾版還原出來的；一般存檔是 null。還原出來的版本，下一次存檔不和它合併。</summary>
    public int? RestoredFrom { get; init; }
}

/// <summary><c>knowledge.page_attachments</c> 的一列。<see cref="Id"/> 也是儲存空間裡的鍵。</summary>
internal sealed record PageAttachmentRow
{
    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid PageId { get; init; }

    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required long SizeBytes { get; init; }

    public required string Sha256 { get; init; }

    public required Guid UploadedBy { get; init; }

    public required DateTimeOffset UploadedAt { get; init; }
}
