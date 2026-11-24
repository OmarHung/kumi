using PmTool.Abstractions;
using PmTool.Work.Common;

namespace PmTool.Work.Items;

/// <summary>批次請求本身的檢查（item.bulk_invalid）：還沒看專案，結果不依賴專案存不存在。</summary>
internal static class BulkRequestRules
{
    /// <returns>請求不合格時的失敗；合格是 null。</returns>
    public static Failure? Check(BulkItemUpdate update)
    {
        if (CheckKeys(update.Keys) is { } keys)
        {
            return keys;
        }

        var others = OtherPropertiesGiven(update);
        if (update.State.HasValue)
        {
            return update.State.Value is null
                ? Invalid("state", "要給要改成的狀態。")
                : others ? Invalid("state", "改狀態時不能同時改其他的屬性；要改其他屬性請另外送一次。") : null;
        }

        return others ? null : Invalid(string.Empty, "沒有要改的內容：要給狀態，或負責人、優先度、日期、週期、里程碑其中一個以上。");
    }

    public static Failure? CheckKeys(IReadOnlyList<string>? keys) => keys switch
    {
        null or { Count: 0 } => Invalid("keys", "要給至少一個項目編號。"),
        { Count: > BulkItemUpdate.MaxItems } => Invalid("keys", $"一次最多 {BulkItemUpdate.MaxItems} 個項目，這次給了 {keys.Count} 個。"),
        _ => null,
    };

    public static WorkItemPatch ToPatch(BulkItemUpdate update) => new()
    {
        AssigneeId = update.AssigneeId,
        Priority = update.Priority,
        StartDate = update.StartDate,
        DueDate = update.DueDate,
        CycleId = update.CycleId,
        MilestoneId = update.MilestoneId,
    };

    private static bool OtherPropertiesGiven(BulkItemUpdate update) =>
        update.AssigneeId.HasValue || update.Priority.HasValue || update.StartDate.HasValue
        || update.DueDate.HasValue || update.CycleId.HasValue || update.MilestoneId.HasValue;

    private static Failure Invalid(string path, string message) => WorkFailures.Invalid(WorkErrorCodes.BulkInvalid, path, message);
}
