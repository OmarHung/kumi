using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Identity.Persistence;

namespace PmTool.Identity.Audit;

internal sealed class AuditLog(IDbContextFactory<IdentityDbContext> contexts) : IAuditLog
{
    private const string CursorPath = "cursor";

    private const string LimitPath = "limit";

    private static readonly Failure ForbiddenFailure =
        new(FailureKind.Forbidden, IdentityErrorCodes.Forbidden, "只有組織的擁有者能看稽核紀錄。");

    public async Task<Result<AuditPage>> ListAsync(Actor actor, AuditQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(query);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // 每次查資料庫：角色剛被降級的人，不能靠舊的結果繼續讀。先擋權限，不讓沒資格的人從參數的回應猜到什麼。
        var isOwner = await db.People.AnyAsync(
            p => p.Id == actor.PersonId && p.OrganizationId == actor.OrganizationId && p.Role == OrganizationRole.Owner,
            cancellationToken);
        if (!isOwner)
        {
            return ForbiddenFailure;
        }

        if (CheckQuery(query) is { } invalid)
        {
            return invalid;
        }

        var position = query.Cursor is null ? null : AuditCursor.TryParse(query.Cursor);
        var rows = await Page(db, actor.OrganizationId, query, position).ToListAsync(cancellationToken);

        var items = rows.Take(query.Limit).Select(ToEntry).ToList();
        var nextCursor = rows.Count > query.Limit ? AuditCursor.Format(rows[query.Limit - 1]) : null;
        return new AuditPage(items, nextCursor);
    }

    // 登入的紀錄之後才接上；先不寫，介面已經固定下來讓別的包可以照著呼叫。
    public Task RecordSignInAsync(SignInAttempt attempt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        return Task.CompletedTask;
    }

    private static Failure? CheckQuery(AuditQuery query)
    {
        if (query.Limit is < 1 or > AuditRules.MaxLimit)
        {
            return Invalid(LimitPath, $"每頁筆數要在 1 到 {AuditRules.MaxLimit} 之間。");
        }

        return query.Cursor is not null && AuditCursor.TryParse(query.Cursor) is null
            ? Invalid(CursorPath, "分頁的游標不對。")
            : null;
    }

    private static Failure Invalid(string path, string message) =>
        new(FailureKind.Invalid, IdentityErrorCodes.AuditQueryInvalid, message)
        {
            Details = [new FailureDetail(IdentityErrorCodes.AuditQueryInvalid, path, message)],
        };

    /// <summary>多取一筆，看有沒有下一頁。游標比的是資料庫自己的排序（uuid 在資料庫與 .NET 的大小順序不同），所以比較留給資料庫。</summary>
    private static IQueryable<AuditEventRow> Page(IdentityDbContext db, Guid organizationId, AuditQuery query, AuditCursor? position)
    {
        var source = position is { } at
            ? db.AuditEvents.FromSql($"SELECT * FROM identity.audit_events WHERE (occurred_at, id) < ({at.OccurredAt}, {at.Id})")
            : db.AuditEvents;

        var filtered = source.AsNoTracking().Where(e => e.OrganizationId == organizationId);
        if (query.PersonId is { } personId)
        {
            filtered = filtered.Where(e => e.ActorPersonId == personId || e.TargetPersonId == personId);
        }

        return filtered.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id).Take(query.Limit + 1);
    }

    private static AuditEntry ToEntry(AuditEventRow row) =>
        new(row.Id, row.OccurredAt, row.Action, row.Outcome, row.ActorPersonId, row.TargetPersonId, row.SourceAddress,
            new Dictionary<string, string>(row.Details, StringComparer.Ordinal));

    /// <summary>游標：最後一筆的（時間, 編號），編成不透明的字串。</summary>
    private sealed record AuditCursor(DateTimeOffset OccurredAt, Guid Id)
    {
        public static string Format(AuditEventRow last) =>
            Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
                string.Create(CultureInfo.InvariantCulture, $"{last.OccurredAt.UtcTicks}|{last.Id:N}")));

        public static AuditCursor? TryParse(string cursor)
        {
            if (cursor.Length > 128 || !Base64Url.IsValid(cursor, out var length))
            {
                return null;
            }

            var text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
            var parts = text.Split('|');
            return length > 0
                && parts.Length == 2
                && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && ticks > 0 && ticks <= DateTime.MaxValue.Ticks
                && Guid.TryParseExact(parts[1], "N", out var id)
                    ? new AuditCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id)
                    : null;
        }
    }
}
