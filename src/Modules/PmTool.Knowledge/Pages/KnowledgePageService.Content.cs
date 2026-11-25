using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Knowledge.Access;
using PmTool.Knowledge.Common;
using PmTool.Knowledge.Persistence;

namespace PmTool.Knowledge.Pages;

internal sealed partial class KnowledgePageService
{
    private const int FirstRevision = 1;

    public async Task<Result<PageDetail>> CreateAsync(Actor actor, string spaceKey, NewPage page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(page);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var space = await access.FindAsync(db, actor, spaceKey, cancellationToken);
        if (space is null)
        {
            return KnowledgeFailures.SpaceNotFound(spaceKey);
        }

        if (space.CheckEdit() is { } denied)
        {
            return denied;
        }

        var content = KnowledgeText.CheckContent(page.Title, page.Body, checkTitle: true, checkBody: true);
        if (content.Failure is { } invalid)
        {
            return invalid;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await KnowledgeLocks.SpaceAsync(db, space.Space.Id, cancellationToken);
        var scope = await PageScope.LoadLiveAsync(db, actor, space, cancellationToken);
        var placement = PlaceNewPage(scope, page);
        if (placement.Failure is { } misplaced)
        {
            return misplaced;
        }

        var (title, body) = (content.Value.Title!, content.Value.Body!);
        var (info, now) = await InsertPageAsync(db, actor, space.Space.Id, placement.Value, (title, body), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ComposeDetail(space, scope, info, body, FirstRevision, now, actor.PersonId, versionCount: 1);
    }

    /// <summary>寫入新頁面、它的第一版，並把那一層重新編號。呼叫端在空間的鎖裡。</summary>
    private async Task<(PageInfo Info, DateTimeOffset Now)> InsertPageAsync(
        KnowledgeDbContext db,
        Actor actor,
        Guid spaceId,
        (PageInfo? Parent, IReadOnlyList<(Guid Id, int Current)> Siblings, int Index) placement,
        (string Title, string Body) content,
        CancellationToken cancellationToken)
    {
        var (parent, siblings, index) = placement;
        var number = await TakeNextNumberAsync(db, actor.OrganizationId, spaceId, cancellationToken);
        var now = clock.GetUtcNow();
        var row = new PageRow
        {
            Id = Guid.CreateVersion7(now),
            OrganizationId = actor.OrganizationId,
            SpaceId = spaceId,
            Number = number,
            ParentId = parent?.Id,
            Position = index,
            Title = content.Title,
            Body = content.Body,
            Revision = FirstRevision,
            IsRestricted = false,
            CreatedBy = actor.PersonId,
            CreatedAt = now,
            UpdatedBy = actor.PersonId,
            UpdatedAt = now,
        };
        db.Pages.Add(row);
        PageVersions.AddFirst(db, actor.OrganizationId, row.Id, content.Title, content.Body, actor.PersonId, now);
        await db.SaveChangesAsync(cancellationToken);
        await PageOrder.RenumberAsync(db, actor.OrganizationId, spaceId, PageOrder.Insert(siblings, index, row.Id, index), cancellationToken);
        return (new PageInfo(row.Id, number, row.ParentId, index, content.Title, false, now, actor.PersonId, null, null, null), now);
    }

    public async Task<Result<PageDetail>> UpdateAsync(
        Actor actor, string spaceKey, int pageNumber, PageChanges changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(changes);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var found = await FindLiveAsync(db, actor, spaceKey, pageNumber, cancellationToken);
        if (found.Failure is { } notFound)
        {
            return notFound;
        }

        var target = found.Value;
        if (target.Space.CheckEdit() is { } denied)
        {
            return denied;
        }

        var content = KnowledgeText.CheckContent(changes.Title, changes.Body, checkTitle: changes.Title is not null, checkBody: changes.Body is not null);
        if (content.Failure is { } invalid)
        {
            return invalid;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var saved = await SaveContentAsync(db, actor, target, content.Value, changes.ExpectedRevision, cancellationToken);
        if (saved.Failure is { } failure)
        {
            return failure;
        }

        await transaction.CommitAsync(cancellationToken);
        return await BuildDetailAsync(db, target with { Page = saved.Value }, cancellationToken);
    }

    /// <summary>
    /// 鎖住那一頁、比對 revision、寫入新內容與版本。回傳要顯示的頁面骨架（內容沒變時就是原本的）。
    /// 呼叫端在交易裡。
    /// </summary>
    private async Task<Result<PageInfo>> SaveContentAsync(
        KnowledgeDbContext db, Actor actor, PageTarget target, (string? Title, string? Body) content, long expectedRevision, CancellationToken cancellationToken)
    {
        await KnowledgeLocks.PageAsync(db, actor.OrganizationId, target.Page.Id, cancellationToken);
        var row = await db.Pages.AsNoTracking()
            .SingleAsync(p => p.OrganizationId == actor.OrganizationId && p.Id == target.Page.Id, cancellationToken);
        if (row.DeletedAt is not null)
        {
            return KnowledgeFailures.PageNotFound(target.Page.Number);
        }

        if (row.Revision != expectedRevision)
        {
            return KnowledgeFailures.Conflict(
                KnowledgeErrorCodes.PageConflict, "這一頁在你開始編輯之後被別人（或你的另一個分頁）改過了，這次沒有儲存。請看新的內容，再決定要不要覆蓋。");
        }

        var title = content.Title ?? row.Title;
        var body = content.Body ?? row.Body;
        if (title == row.Title && body == row.Body)
        {
            return target.Page;
        }

        var now = clock.GetUtcNow();
        await db.Pages.Where(p => p.OrganizationId == actor.OrganizationId && p.Id == row.Id)
            .ExecuteUpdateAsync(
                set => set.SetProperty(p => p.Title, title)
                    .SetProperty(p => p.Body, body)
                    .SetProperty(p => p.Revision, row.Revision + 1)
                    .SetProperty(p => p.UpdatedBy, actor.PersonId)
                    .SetProperty(p => p.UpdatedAt, now),
                cancellationToken);
        await PageVersions.RecordAsync(db, actor.OrganizationId, row.Id, title, body, actor.PersonId, now, cancellationToken);
        return target.Page with { Title = title, UpdatedAt = now, UpdatedBy = actor.PersonId };
    }

    /// <summary>新頁面的位置：上層要看得到、沒刪、深度夠；「放在它前面」的頁面要是那個上層底下看得到的頁面；空間還沒滿。</summary>
    private static Result<(PageInfo? Parent, IReadOnlyList<(Guid Id, int Current)> Siblings, int Index)> PlaceNewPage(PageScope scope, NewPage page)
    {
        if (scope.LiveCount >= KnowledgeRules.MaxPagesPerSpace)
        {
            return KnowledgeFailures.Conflict(KnowledgeErrorCodes.PageLimitReached, $"一個空間最多 {KnowledgeRules.MaxPagesPerSpace} 頁。");
        }

        var parent = scope.FindVisible(page.ParentNumber);
        if (page.ParentNumber is not null && parent is not { IsDeleted: false })
        {
            return KnowledgeFailures.Invalid(KnowledgeErrorCodes.PagePositionInvalid, "parentNumber", "找不到要放在它底下的頁面。");
        }

        if (parent is not null && scope.DepthOf(parent) + 1 > KnowledgeRules.MaxDepth)
        {
            return KnowledgeFailures.Invalid(
                KnowledgeErrorCodes.PageTooDeep, "parentNumber", $"頁面最多 {KnowledgeRules.MaxDepth} 層，放在這裡會超過。");
        }

        var siblings = scope.LiveChildren(parent?.Id);
        var index = IndexBefore(scope, siblings, page.BeforeNumber, exclude: null);
        return index is null
            ? KnowledgeFailures.Invalid(KnowledgeErrorCodes.PagePositionInvalid, "beforeNumber", "要放在它前面的頁面，必須是同一層看得到的頁面。")
            : (parent, PageOrder.Of(siblings), index.Value);
    }

    /// <summary>「放在這一頁前面」換算成同一層的索引；null 是放最後。找不到、看不到、不在這一層是 null（呼叫端回 422）。</summary>
    private static int? IndexBefore(PageScope scope, IReadOnlyList<PageInfo> siblings, int? beforeNumber, PageInfo? exclude)
    {
        if (beforeNumber is null)
        {
            return siblings.Count;
        }

        var before = scope.FindVisible(beforeNumber);
        if (before is null || before == exclude)
        {
            return null;
        }

        var index = siblings.ToList().FindIndex(s => s.Id == before.Id);
        return index < 0 ? null : index;
    }

    /// <summary>配發空間的下一個編號。編號只增不減、不重複使用（刪掉的頁面不會把號碼讓出來）。</summary>
    private static async Task<int> TakeNextNumberAsync(KnowledgeDbContext db, Guid organizationId, Guid spaceId, CancellationToken cancellationToken)
    {
        var taken = await db.Database
            .SqlQuery<int>(
                $"UPDATE knowledge.spaces SET next_page_number = next_page_number + 1 WHERE organization_id = {organizationId} AND id = {spaceId} RETURNING next_page_number - 1 AS \"Value\"")
            .ToListAsync(cancellationToken);
        return taken.Single();
    }
}
