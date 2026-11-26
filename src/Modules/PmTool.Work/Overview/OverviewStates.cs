using PmTool.Blueprint.Model;
using PmTool.Work.Blueprints;
using PmTool.Work.Items;

namespace PmTool.Work.Overview;

/// <summary>某個專案、某個狀態、某個類別的數字（資料庫一次分組算出來的一列）。</summary>
internal sealed record OverviewGroup(
    Guid ProjectId,
    string State,
    string StateCategory,
    int Total,
    int Overdue,
    int DueSoon,
    int Unassigned,
    int Stale,
    int DoneRecently,
    DateTimeOffset LastUpdatedAt);

/// <summary>把分組的列整理成「各狀態的項目數」：名稱、順序、類別照藍圖，同 key 多流程合併。</summary>
internal static class OverviewStates
{
    public static IReadOnlyList<OverviewStateCount> Of(ProjectSchema schema, IReadOnlyList<OverviewGroup> groups)
    {
        var counts = groups.GroupBy(g => g.State, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Sum(x => x.Total), StringComparer.Ordinal);
        var listed = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<OverviewStateCount>();

        // 流程的順序就是畫面上橫條的順序；同一個 key 在後面的流程再出現時，數量已經併在第一次出現的那一筆。
        foreach (var state in schema.Blueprint.Workflows.SelectMany(w => w.States))
        {
            if (listed.Add(state.Key) && counts.TryGetValue(state.Key, out var count) && count > 0)
            {
                result.Add(new OverviewStateCount(state.Key, state.Name, state.Category, count));
            }
        }

        // 藍圖改版拿掉的狀態：項目還在那裡，數字不能憑空消失。接在最後，名稱沒有別的來源就用 key。
        foreach (var group in groups.Where(g => !listed.Contains(g.State)).OrderBy(g => g.State, StringComparer.Ordinal))
        {
            if (listed.Add(group.State))
            {
                result.Add(new OverviewStateCount(group.State, group.State, StateCategoryText.Parse(group.StateCategory), counts[group.State]));
            }
        }

        return result;
    }
}
