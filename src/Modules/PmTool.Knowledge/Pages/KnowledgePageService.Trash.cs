using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Knowledge.Access;
using PmTool.Knowledge.Common;
using PmTool.Knowledge.Persistence;
using PmTool.Work;

namespace PmTool.Knowledge.Pages;

internal sealed partial class KnowledgePageService
{
    /// <summary>一次從資料庫讀幾批最上面的頁面再逐批檢查看不看得到；看不到的濾掉，不夠一頁就再往下讀。</summary>
    private const int TrashReadChunk = 100;

    public async Task<Result<TrashPage>> ListTrashAsync(Actor actor, string spaceKey, TrashQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(query);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var space = await access.FindAsync(db, actor, spaceKey, cancellationToken);
        if (space is null)
        {
            return KnowledgeFailures.SpaceNotFound(spaceKey);
        }

        var checkedQuery = CheckTrashQuery(query);
        if (checkedQuery.Failure is { } invalid)
        {
            return invalid;
        }

        var (cursor, limit) = checkedQuery.Value;
        var live = await PageScope.LoadLiveAsync(db, actor, space, cancellationToken);
        var entries = new List<(TrashEntry Entry, TrashCursor At)>();
        while (entries.Count <= limit)
        {
            var tops = await ReadTrashTopsAsync(db, actor.OrganizationId, space.Space.Id, cursor, cancellationToken);
            if (tops.Count == 0)
            {
                break;
            }

            cursor = new TrashCursor(tops[^1].DeletedAt!.Value, tops[^1].DeletionId!.Value);
            var scope = await live.WithTrashBatchesAsync(db, actor, space.Space.Id, [.. tops.Select(t => t.DeletionId!.Value)], cancellationToken);
            foreach (var top in tops.DistinctBy(t => t.DeletionId))
            {
                if (scope.CanSee(top))
                {
                    var visibleCount = scope.BatchOf(top.DeletionId!.Value).Count(scope.CanSee);
                    entries.Add((ToEntry(scope, top, visibleCount), new TrashCursor(top.DeletedAt!.Value, top.DeletionId.Value)));
                }
            }
        }

        var items = entries.Take(limit).ToList();
        var next = entries.Count > limit ? items[^1].At.Encode() : null;
        return new TrashPage(
            [.. items.Select(i => i.Entry)], next, live.SeesAll ? await TrashCapacityAsync(db, actor.OrganizationId, space.Space.Id, cancellationToken) : null);
    }

    private static TrashEntry ToEntry(PageScope scope, PageInfo top, int visibleCount) =>
        new(
            top.DeletionId!.Value,
            top.ToReference(),
            visibleCount,
            scope.ById(top.ParentId)?.ToReference(),
            top.DeletedAt!.Value,
            top.DeletedBy!.Value);

    /// <summary>
    /// 每一批最上面的一頁（它的上層不在同一批裡），新刪的在前，接在 <paramref name="after"/> 之後。
    /// 資料成環、整批的頁面都有同批的上層時，那一批不會出現在清單裡（永久刪除仍然清得掉）。
    /// </summary>
    private static async Task<IReadOnlyList<PageInfo>> ReadTrashTopsAsync(
        KnowledgeDbContext db, Guid organizationId, Guid spaceId, TrashCursor? after, CancellationToken cancellationToken)
    {
        var tops = db.Pages.AsNoTracking()
            .Where(p => p.OrganizationId == organizationId && p.SpaceId == spaceId && p.DeletionId != null
                && !db.Pages.Any(parent => parent.OrganizationId == organizationId && parent.Id == p.ParentId && parent.DeletionId == p.DeletionId));
        if (after is { } cursor)
        {
            var at = cursor.DeletedAt;
            var id = cursor.DeletionId;
            tops = tops.Where(p => p.DeletedAt < at || (p.DeletedAt == at && p.DeletionId!.Value.CompareTo(id) < 0));
        }

        var rows = await tops
            .OrderByDescending(p => p.DeletedAt)
            .ThenByDescending(p => p.DeletionId)
            .Select(p => new PageInfo(
                p.Id, p.Number, p.ParentId, p.Position, p.Title, p.IsRestricted, p.UpdatedAt, p.UpdatedBy, p.DeletedAt, p.DeletedBy, p.DeletionId, p.RestrictedBy))
            .Take(TrashReadChunk)
            .ToListAsync(cancellationToken);
        return rows;
    }

    private async Task<TrashCapacity> TrashCapacityAsync(
        KnowledgeDbContext db, Guid organizationId, Guid spaceId, CancellationToken cancellationToken) =>
        new(
            await db.Pages.AsNoTracking()
                .Where(p => p.OrganizationId == organizationId && p.SpaceId == spaceId && p.DeletionId != null)
                .Select(p => p.DeletionId)
                .Distinct()
                .CountAsync(cancellationToken),
            limits.MaxTrashBatchesPerSpace);

    private static Result<(TrashCursor? Cursor, int Limit)> CheckTrashQuery(TrashQuery query)
    {
        if (query.Limit is < 1 or > KnowledgeRules.MaxTrashPageSize)
        {
            return KnowledgeFailures.Invalid(WorkErrorCodes.QueryInvalid, "limit", $"limit 要在 1 到 {KnowledgeRules.MaxTrashPageSize} 之間。");
        }

        if (query.Cursor is null)
        {
            return ((TrashCursor?)null, query.Limit);
        }

        return TrashCursor.TryDecode(query.Cursor, out var cursor)
            ? ((TrashCursor?)cursor, query.Limit)
            : KnowledgeFailures.Invalid(WorkErrorCodes.QueryInvalid, "cursor", "cursor 不正確，請從第一頁重新讀起。");
    }

    /// <summary>分頁的位置：最後一批的刪除時間與編號（和排序同一組），編成不透明的字串。</summary>
    private readonly record struct TrashCursor(DateTimeOffset DeletedAt, Guid DeletionId)
    {
        public string Encode() =>
            Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
                string.Create(CultureInfo.InvariantCulture, $"{DeletedAt.UtcTicks}.{DeletionId:N}")));

        public static bool TryDecode(string text, out TrashCursor cursor)
        {
            cursor = default;
            if (text.Length > 128 || !Base64Url.IsValid(text))
            {
                return false;
            }

            var plain = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(text));
            var parts = plain.Split('.');
            if (parts.Length != 2
                || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || ticks > DateTime.MaxValue.Ticks
                || !Guid.TryParseExact(parts[1], "N", out var id))
            {
                return false;
            }

            cursor = new TrashCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
            return true;
        }
    }
}
