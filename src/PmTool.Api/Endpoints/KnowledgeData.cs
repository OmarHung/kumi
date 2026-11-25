using PmTool.Identity;
using PmTool.Knowledge;

namespace PmTool.Api.Endpoints;

/// <summary>知識庫在 API 上的形狀（web/src/api/knowledgeTypes.ts 就是合約）。人一律帶顯示名稱，網頁不必另外查。</summary>
internal sealed record KnowledgeSpacePermissionsData(bool CanEdit, bool CanComment, bool CanManage);

internal sealed record KnowledgeSpaceProjectData(string Key, string Name, bool IsArchived);

internal sealed record KnowledgeSpaceData(
    Guid Id,
    string Key,
    string Name,
    string? Description,
    KnowledgeSpaceProjectData? Project,
    DateTimeOffset CreatedAt,
    PersonReference CreatedBy,
    DateTimeOffset? ArchivedAt,
    KnowledgeSpacePermissionsData MyPermissions)
{
    public static KnowledgeSpaceData From(SpaceSummary space, PeopleNames names) => new(
        space.Id,
        space.Key,
        space.Name,
        space.Description,
        space.Project is { } project ? new KnowledgeSpaceProjectData(project.Key, project.Name, project.IsArchived) : null,
        space.CreatedAt,
        names.Reference(space.CreatedBy),
        space.ArchivedAt,
        new KnowledgeSpacePermissionsData(space.MyPermissions.CanEdit, space.MyPermissions.CanComment, space.MyPermissions.CanManage));
}

internal sealed record KnowledgePageNodeData(
    Guid Id,
    int Number,
    Guid? ParentId,
    int Position,
    string Title,
    bool IsRestricted,
    DateTimeOffset UpdatedAt,
    PersonReference UpdatedBy);

internal sealed record KnowledgePageTreeData(KnowledgeSpaceData Space, IReadOnlyList<KnowledgePageNodeData> Pages)
{
    public static async Task<KnowledgePageTreeData> FromAsync(
        PageTree tree, IPeopleDirectory people, Guid organizationId, CancellationToken cancellationToken)
    {
        var names = await PeopleNames.LookupAsync(
            people, organizationId, tree.Pages.Select(p => p.UpdatedBy).Append(tree.Space.CreatedBy), cancellationToken);
        return new KnowledgePageTreeData(
            KnowledgeSpaceData.From(tree.Space, names),
            [.. tree.Pages.Select(p => new KnowledgePageNodeData(
                p.Id, p.Number, p.ParentId, p.Position, p.Title, p.IsRestricted, p.UpdatedAt, names.Reference(p.UpdatedBy)))]);
    }
}

internal sealed record KnowledgePageRefData(Guid Id, int Number, string Title)
{
    public static KnowledgePageRefData From(PageReference page) => new(page.Id, page.Number, page.Title);

    public static IReadOnlyList<KnowledgePageRefData> FromMany(IEnumerable<PageReference> pages) => [.. pages.Select(From)];
}

internal sealed record KnowledgePagePermissionsData(bool CanEdit, bool CanComment, bool CanDelete);

internal sealed record KnowledgePageData(
    Guid Id,
    KnowledgeSpaceData Space,
    int Number,
    string Title,
    string Body,
    long Revision,
    IReadOnlyList<KnowledgePageRefData> Ancestors,
    bool IsRestricted,
    IReadOnlyList<KnowledgePageRefData> RestrictedAncestors,
    DateTimeOffset CreatedAt,
    PersonReference CreatedBy,
    DateTimeOffset UpdatedAt,
    PersonReference UpdatedBy,
    int VersionCount,
    KnowledgePagePermissionsData MyPermissions)
{
    public static async Task<KnowledgePageData> FromAsync(
        PageDetail page, IPeopleDirectory people, Guid organizationId, CancellationToken cancellationToken)
    {
        var names = await PeopleNames.LookupAsync(
            people, organizationId, [page.CreatedBy, page.UpdatedBy, page.Space.CreatedBy], cancellationToken);
        return new KnowledgePageData(
            page.Id,
            KnowledgeSpaceData.From(page.Space, names),
            page.Number,
            page.Title,
            page.Body,
            page.Revision,
            KnowledgePageRefData.FromMany(page.Ancestors),
            page.IsRestricted,
            KnowledgePageRefData.FromMany(page.RestrictedAncestors),
            page.CreatedAt,
            names.Reference(page.CreatedBy),
            page.UpdatedAt,
            names.Reference(page.UpdatedBy),
            page.VersionCount,
            new KnowledgePagePermissionsData(page.MyPermissions.CanEdit, page.MyPermissions.CanComment, page.MyPermissions.CanDelete));
    }
}

internal sealed record KnowledgePageDeletionData(Guid DeletionId, KnowledgePageRefData Page, int PageCount)
{
    public static KnowledgePageDeletionData From(PageDeletion deletion) =>
        new(deletion.DeletionId, KnowledgePageRefData.From(deletion.Page), deletion.PageCount);
}

internal sealed record KnowledgeTrashEntryData(
    Guid DeletionId,
    KnowledgePageRefData Page,
    int PageCount,
    KnowledgePageRefData? FormerParent,
    DateTimeOffset DeletedAt,
    PersonReference DeletedBy)
{
    public static async Task<IReadOnlyList<KnowledgeTrashEntryData>> FromManyAsync(
        IReadOnlyList<TrashEntry> entries, IPeopleDirectory people, Guid organizationId, CancellationToken cancellationToken)
    {
        var names = await PeopleNames.LookupAsync(people, organizationId, entries.Select(e => e.DeletedBy), cancellationToken);
        return
        [
            .. entries.Select(e => new KnowledgeTrashEntryData(
                e.DeletionId,
                KnowledgePageRefData.From(e.Page),
                e.PageCount,
                e.FormerParent is { } parent ? KnowledgePageRefData.From(parent) : null,
                e.DeletedAt,
                names.Reference(e.DeletedBy))),
        ];
    }
}

internal sealed record KnowledgeTrashCapacityData(int Batches, int MaxBatches);

/// <summary>垃圾桶的一頁：capacity 只有空間的管理者才有，其他人是 null。</summary>
internal sealed record KnowledgeTrashPageData(
    IReadOnlyList<KnowledgeTrashEntryData> Items, string? NextCursor, KnowledgeTrashCapacityData? Capacity)
{
    public static async Task<KnowledgeTrashPageData> FromAsync(
        TrashPage page, IPeopleDirectory people, Guid organizationId, CancellationToken cancellationToken) =>
        new(
            await KnowledgeTrashEntryData.FromManyAsync(page.Items, people, organizationId, cancellationToken),
            page.NextCursor,
            page.Capacity is { } capacity ? new KnowledgeTrashCapacityData(capacity.Batches, capacity.MaxBatches) : null);
}

internal sealed record KnowledgeTrashPurgeData(int PurgedBatches, int PurgedPages, int PurgedAttachments, int RemainingBatches, bool HasMoreExpired)
{
    public static KnowledgeTrashPurgeData From(TrashPurge purge) =>
        new(purge.PurgedBatches, purge.PurgedPages, purge.PurgedAttachments, purge.RemainingBatches, purge.HasMoreExpired);
}

/// <summary>限制的名單把人員編號換成 <c>{ personId, displayName }</c>；沒有設是 null。</summary>
internal sealed record KnowledgePageRestrictionData(
    IReadOnlyList<PersonReference>? People,
    IReadOnlyList<KnowledgePageRefData> RestrictedAncestors,
    PersonReference? RestrictedBy,
    bool CanRelax)
{
    public static async Task<KnowledgePageRestrictionData> FromAsync(
        PageRestriction restriction, IPeopleDirectory people, Guid organizationId, CancellationToken cancellationToken)
    {
        var shown = (restriction.People ?? []).Concat(restriction.RestrictedBy is { } owner ? new[] { owner } : Array.Empty<Guid>());
        var names = await PeopleNames.LookupAsync(people, organizationId, shown, cancellationToken);
        return new KnowledgePageRestrictionData(
            restriction.People is { } listed ? [.. listed.Select(names.Reference)] : null,
            KnowledgePageRefData.FromMany(restriction.RestrictedAncestors),
            restriction.RestrictedBy is { } by ? names.Reference(by) : null,
            restriction.CanRelax);
    }
}

internal sealed record KnowledgeAttachmentData(
    Guid Id,
    Guid PageId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    PersonReference UploadedBy,
    DateTimeOffset UploadedAt)
{
    public static async Task<IReadOnlyList<KnowledgeAttachmentData>> FromManyAsync(
        IReadOnlyList<PageAttachment> attachments, IPeopleDirectory people, Guid organizationId, CancellationToken cancellationToken)
    {
        var names = await PeopleNames.LookupAsync(people, organizationId, attachments.Select(a => a.UploadedBy), cancellationToken);
        return
        [
            .. attachments.Select(a => new KnowledgeAttachmentData(
                a.Id, a.PageId, a.FileName, a.ContentType, a.SizeBytes, a.Sha256, names.Reference(a.UploadedBy), a.UploadedAt)),
        ];
    }
}
