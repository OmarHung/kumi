using PmTool.Abstractions;
using PmTool.Identity.Persistence;

namespace PmTool.Identity.Audit;

/// <summary>要寫進稽核紀錄的一件事。</summary>
internal sealed record AuditRecord(Guid OrganizationId, string Action, string Outcome = AuditOutcomes.Succeeded)
{
    public Guid? ActorPersonId { get; init; }

    public Guid? TargetPersonId { get; init; }

    /// <summary>值都是字串；數字與清單由呼叫端轉成文字（清單用空白隔開）。</summary>
    public IReadOnlyDictionary<string, string>? Details { get; init; }

    /// <summary>不給時用目前請求的來源（<see cref="IRequestSource"/>）；登入這種回應送完才寫的，由呼叫端明確帶。</summary>
    public RequestSource? Source { get; init; }
}

/// <summary>
/// 寫稽核紀錄的唯一入口，各個服務在自己操作的交易裡呼叫，紀錄才和操作一起成功或一起回復。
/// 模組不碰 HTTP，來源從 <see cref="IRequestSource"/> 取。
/// </summary>
internal sealed class AuditTrail(IRequestSource requestSource, TimeProvider time)
{
    /// <summary>細項的值是給人看的，不是存資料用的；截斷之後資料表才不會被一個超長的值撐大。</summary>
    internal const int MaxDetailValueLength = 512;

    /// <summary>
    /// 在呼叫端的資料庫內容裡新增一列並存檔（呼叫端已經開好交易），回傳這件事的編號。
    /// 這個內容上其他還沒存的變更會一起存進去。
    /// </summary>
    public async Task<Guid> AddAsync(IdentityDbContext db, AuditRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(record);

        var source = record.Source ?? requestSource.Current;
        var row = new AuditEventRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = record.OrganizationId,
            OccurredAt = time.GetUtcNow(),
            Action = record.Action,
            Outcome = record.Outcome,
            ActorPersonId = record.ActorPersonId,
            TargetPersonId = record.TargetPersonId,
            SourceAddress = RequestSourceText.Clean(source?.Address, RequestSourceText.MaxAddressLength),
            UserAgent = RequestSourceText.Clean(source?.UserAgent, RequestSourceText.MaxUserAgentLength),
            Details = CleanDetails(record.Details),
        };
        db.AuditEvents.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    /// <summary>jsonb 不收 NUL；控制字元與零寬字元也一併換成空白，細項的值才不會騙人。</summary>
    private static Dictionary<string, string> CleanDetails(IReadOnlyDictionary<string, string>? details) =>
        details is null
            ? []
            : details.ToDictionary(
                pair => pair.Key,
                pair => RequestSourceText.Clean(pair.Value, MaxDetailValueLength) ?? string.Empty,
                StringComparer.Ordinal);
}
