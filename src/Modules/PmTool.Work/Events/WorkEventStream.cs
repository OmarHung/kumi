using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PmTool.Work.Persistence;

namespace PmTool.Work.Events;

/// <summary>
/// 事件流的讀法。事件的位置是（交易編號、流水號），只讀交易編號比「目前還在進行的最舊交易」更小的：
/// 交易編號在第一次寫入時就發了，提交的順序卻可能不同；只看流水號、讀的人往前跳過之後，
/// 先拿到號碼卻晚提交的事件就永遠讀不到了。比最舊的進行中交易更早的，一定都已經提交或中止，不會再有新的插進來。
/// 代價：有一個很久不結束的交易時，流會停在它前面（只是延遲，不會漏）。
///
/// 還原到新的資料庫之後，新資料庫的交易編號從頭算起，比備份裡事件的、也比讀的人存的位置小，
/// 新事件會排在讀的人的位置前面、永遠讀不到。同一個資料庫裡事件的交易編號不可能大於現在的，
/// 所以讀之前先把「大於現在」的事件歸成 1（真實的交易編號最小是 3，1 不會撞到新事件；歷史照流水號排在所有新事件前面），
/// 傳進來「大於現在」的位置也當成 1 比較（讀的人存的是舊資料庫的編號，流水號是延續的）。
/// 已知的邊界：備份當下若有兩個寫入交易的交易編號順序和流水號順序相反，歸一後那一兩筆可能重送或漏送一次；
/// 還原很少發生，比起通知整個停掉，這個代價可以接受。
/// </summary>
internal sealed class WorkEventStream(IDbContextFactory<WorkDbContext> contexts, ILogger<WorkEventStream> logger) : IWorkEventStream
{
    // 兩個程序同時發現要歸一時，只讓一個做（另一個等鎖，之後的 UPDATE 讀到新的快照、找不到要改的）。鍵是隨意選的固定值。
    private const long NormalizeLockKey = 0x504D_4556_0001;

    // 快照的 xmax 是「下一個會發的交易編號」，已提交的事件一定比它小。用快照而不用 pg_current_xact_id()，因為後者會替這個唯讀的查詢配發交易編號。
    private const string NeedsNormalizingSql = """
        SELECT EXISTS (SELECT 1 FROM work.events WHERE transaction_id >= pg_snapshot_xmax(pg_current_snapshot())) AS "Value"
        """;

    private const string NormalizeSql = """
        UPDATE work.events SET transaction_id = '1'::xid8 WHERE transaction_id >= pg_snapshot_xmax(pg_current_snapshot())
        """;

    // 欄位名稱照命名慣例用 snake_case，才對得上 EventRow 的屬性。xid8 沒有對應的 EF 型別：位置用文字帶進去、再轉回 xid8 比較，讀出來也轉成文字再變成數字。
    // 讀者存的位置若來自還原前的資料庫（交易編號不小於現在的快照 xmax），當成（1、原本的流水號）。{0} 是位置的交易編號（文字）。
    private const string ResolvedTransactionSql =
        "(CASE WHEN {0}::xid8 >= pg_snapshot_xmax(pg_current_snapshot()) THEN '1'::xid8 ELSE {0}::xid8 END)";

    private const string ResolveSql = "SELECT " + ResolvedTransactionSql + "::text AS \"Value\"";

    // ORDER BY 一定要用資料表別名限定：輸出欄位也叫 transaction_id（文字），沒限定的話 PostgreSQL 會拿輸出欄位排序，
    // "1000" 排在 "999" 前面。交易編號多一位數的時候（999 到 1000、9999 到 10000）一批裡的順序就亂了，
    // 加上 LIMIT 之後位置往前走會跳過事件。
    private const string ReadSql = $$"""
        SELECT e.transaction_id::text AS transaction_id, e.sequence, e.event_id, e.organization_id, e.kind, e.actor_id, e.occurred_at, e.content::text AS content
        FROM work.events AS e
        WHERE (e.transaction_id, e.sequence) > ({{ResolvedTransactionSql}}, {1})
          AND e.transaction_id < pg_snapshot_xmin(pg_current_snapshot())
        ORDER BY e.transaction_id, e.sequence
        LIMIT {2}
        """;

    // 和 ReadSql 同一個「已經確定」的條件，所以接著從這個位置讀，不會漏也不會重複。
    private const string LatestSql = """
        SELECT e.transaction_id::text AS transaction_id, e.sequence
        FROM work.events AS e
        WHERE e.transaction_id < pg_snapshot_xmin(pg_current_snapshot())
        ORDER BY e.transaction_id DESC, e.sequence DESC
        LIMIT 1
        """;

    public async Task<IReadOnlyList<WorkEvent>> ReadAfterAsync(WorkEventPosition after, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(after);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await NormalizeRestoredHistoryAsync(db, cancellationToken);
        var rows = await db.Database
            .SqlQueryRaw<EventRow>(ReadSql, after.TransactionId.ToString(CultureInfo.InvariantCulture), after.Sequence, limit)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(ToEvent)];
    }

    public async Task<WorkEventPosition> GetLatestPositionAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await NormalizeRestoredHistoryAsync(db, cancellationToken);
        var rows = await db.Database.SqlQueryRaw<PositionRow>(LatestSql).ToListAsync(cancellationToken);
        return rows.Count == 0
            ? WorkEventPosition.Start
            : new WorkEventPosition(long.Parse(rows[0].TransactionId, CultureInfo.InvariantCulture), rows[0].Sequence);
    }

    public async Task<WorkEventPosition> ResolvePositionAsync(WorkEventPosition stored, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stored);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var resolved = await db.Database
            .SqlQueryRaw<string>(ResolveSql, stored.TransactionId.ToString(CultureInfo.InvariantCulture))
            .SingleAsync(cancellationToken);
        return stored with { TransactionId = long.Parse(resolved, CultureInfo.InvariantCulture) };
    }

    private async Task NormalizeRestoredHistoryAsync(WorkDbContext db, CancellationToken cancellationToken)
    {
        var needed = await db.Database.SqlQueryRaw<bool>(NeedsNormalizingSql).SingleAsync(cancellationToken);
        if (!needed)
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [NormalizeLockKey], cancellationToken);
        var changed = await db.Database.ExecuteSqlRawAsync(NormalizeSql, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (changed > 0)
        {
            logger.LogWarning("資料庫看起來是從別處還原的：已把 {Count} 筆事件的交易編號歸一，讀的人會從還原前的位置接著讀。", changed);
        }
    }

    private static WorkEvent ToEvent(EventRow row)
    {
        var position = new WorkEventPosition(long.Parse(row.TransactionId, CultureInfo.InvariantCulture), row.Sequence);
        var kind = ItemEvents.ParseKind(row.Kind);
        if (kind != WorkEventKind.Unknown && TryReadData(row.Content) is { } data)
        {
            return new WorkEvent(row.EventId, position, kind, row.OrganizationId, row.ActorId, row.OccurredAt, data);
        }

        // 不認得的種類，或認得但內容讀不懂：照樣交給讀者（種類是 Unknown），位置才會往前走。
        return new WorkEvent(row.EventId, position, WorkEventKind.Unknown, row.OrganizationId, row.ActorId, row.OccurredAt, ItemEvents.Placeholder);
    }

    private static ItemEventData? TryReadData(string content)
    {
        try
        {
            return ItemEvents.ReadData(content);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private sealed record PositionRow(string TransactionId, long Sequence);

    private sealed record EventRow(
        string TransactionId, long Sequence, Guid EventId, Guid OrganizationId, string Kind, Guid ActorId, DateTimeOffset OccurredAt, string Content);
}
