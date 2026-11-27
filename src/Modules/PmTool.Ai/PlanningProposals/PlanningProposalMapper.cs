using System.Text.Json;
using System.Text.Json.Serialization;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.PlanningProposals;

/// <summary>
/// 資料列裡每一筆的樣子：<see cref="PlanningChange"/> 加上「這一筆處理過了」的記號。
/// 套用到一半程序結束時，只有這個記號分得出哪些筆做過了（修改、刪除沒有別的痕跡）。
/// </summary>
internal sealed record StoredPlanningChange(
    PlanningTarget Target,
    PlanningAction Action,
    Guid? Id,
    PlanningValues? Before,
    PlanningValues? After,
    int? ItemCount,
    string? Error)
{
    public bool Done { get; init; }

    public PlanningChange ToChange() => new(Target, Action, Id, Before, After, ItemCount, Error);

    public static StoredPlanningChange Of(PlanningChange change) =>
        new(change.Target, change.Action, change.Id, change.Before, change.After, change.ItemCount, change.Error);
}

/// <summary>資料列與合約型別之間的轉換，以及 <c>changes</c> 欄位的 JSON。</summary>
internal static class PlanningProposalMapper
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string WriteChanges(IReadOnlyList<StoredPlanningChange> changes) => JsonSerializer.Serialize(changes, Options);

    public static IReadOnlyList<StoredPlanningChange> ReadChanges(string json) =>
        JsonSerializer.Deserialize<List<StoredPlanningChange>>(json, Options) ?? [];

    public static string StatusName(NewItemProposalStatus status) => status switch
    {
        NewItemProposalStatus.Pending => PlanningProposalRow.PendingStatus,
        NewItemProposalStatus.Applying => PlanningProposalRow.ApplyingStatus,
        NewItemProposalStatus.Applied => PlanningProposalRow.AppliedStatus,
        _ => PlanningProposalRow.DismissedStatus,
    };

    /// <summary>
    /// 停在「建立中」超過 <see cref="AiRules.NewItemProposalApplyStale"/> 的（程序在中途結束）讀出來一律是已套用，
    /// 還沒處理過的幾筆標上「過程中斷了」。最後一次有動靜的時間是 <c>DecidedAt</c>（每處理一筆就刷新）。
    /// </summary>
    public static PlanningProposal ToProposal(PlanningProposalRow row, DateTimeOffset now)
    {
        var stored = ReadChanges(row.Changes);
        var isStale = IsStale(row, now);
        var changes = isStale
            ? stored.Select(c => c.Done ? c.ToChange() : c.ToChange() with { Error = Interrupted }).ToList()
            : stored.Select(c => c.ToChange()).ToList();
        var status = isStale ? NewItemProposalStatus.Applied : row.Status switch
        {
            PlanningProposalRow.ApplyingStatus => NewItemProposalStatus.Applying,
            PlanningProposalRow.AppliedStatus => NewItemProposalStatus.Applied,
            PlanningProposalRow.DismissedStatus => NewItemProposalStatus.Dismissed,
            _ => NewItemProposalStatus.Pending,
        };
        return new PlanningProposal(row.Id, row.ProjectKey, changes, status, row.CreatedAt)
        {
            Reason = row.Reason,
            DecidedAt = row.DecidedAt,
            Error = isStale ? InterruptedSummary(stored) : row.Error,
        };
    }

    public static bool IsStale(PlanningProposalRow row, DateTimeOffset now) =>
        row.Status == PlanningProposalRow.ApplyingStatus && now - (row.DecidedAt ?? row.CreatedAt) > AiRules.NewItemProposalApplyStale;

    private const string Interrupted = "套用的過程中斷了，不確定這一筆有沒有套用，請到週期與里程碑頁確認。";

    private static string InterruptedSummary(IReadOnlyList<StoredPlanningChange> stored)
    {
        var done = stored.Count(c => c.Done);
        return $"套用的過程中斷了：{stored.Count} 筆裡處理了 {done} 筆，其餘不確定有沒有套用，請到週期與里程碑頁確認。";
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
