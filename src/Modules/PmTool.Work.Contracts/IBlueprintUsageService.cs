using PmTool.Abstractions;

namespace PmTool.Work;

/// <summary>
/// 專案的設定用得怎麼樣：各類型、各狀態有幾個項目，各欄位有幾個項目填了。只有數字，不含項目的內文，
/// 給 AI 助理判斷「沒人用的欄位」「走不到的狀態」與變更集會動到多少資料用（設計規劃書第 9 節的 describe_usage）。
/// </summary>
public interface IBlueprintUsageService
{
    /// <summary>
    /// 需要 blueprint.read（和變更集的資料影響看得到的數字同一個權限）。看不到的專案回 NotFound。
    /// 只算沒有刪除的項目；封存的專案照樣算。
    /// </summary>
    Task<Result<BlueprintUsage>> DescribeAsync(Actor actor, string projectKey, CancellationToken cancellationToken);
}

/// <param name="BlueprintVersion">算的時候專案的藍圖版本。</param>
/// <param name="ItemCount">沒有刪除的項目總數。</param>
/// <param name="States">每個（類型、狀態）有幾個項目，只列至少一個的；依類型、狀態的 key 排序。</param>
/// <param name="Fields">藍圖裡每個自訂欄位有幾個項目填了值（沒有任何項目填的也列，數字是 0）；依欄位的 key 排序。</param>
public sealed record BlueprintUsage(
    long BlueprintVersion, long ItemCount, IReadOnlyList<StateUsage> States, IReadOnlyList<FieldUsage> Fields);

/// <param name="ItemType">類型的 key。</param>
/// <param name="State">狀態的 key。</param>
public sealed record StateUsage(string ItemType, string State, long Items);

/// <param name="Field">欄位的 key。</param>
/// <param name="FilledItems">這個欄位有值的項目數（空字串、空陣列不算有值）。</param>
public sealed record FieldUsage(string Field, long FilledItems);
