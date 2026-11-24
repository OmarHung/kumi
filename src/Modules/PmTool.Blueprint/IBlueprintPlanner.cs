using PmTool.Blueprint.Model;
using PmTool.Blueprint.Planning;

namespace PmTool.Blueprint;

/// <summary>管線裡「計畫」這一步：算出差異與對既有資料的影響。純邏輯，不讀寫任何資料。</summary>
public interface IBlueprintPlanner
{
    /// <param name="current">專案目前的藍圖。新專案還沒有藍圖，傳 null。</param>
    /// <param name="currentVersion">目前藍圖的版本。新專案是 0。</param>
    /// <param name="changeSet">已經通過 <see cref="IBlueprintValidator.ValidateChangeSet"/> 的變更集；這裡不重做驗證。</param>
    BlueprintPlan Plan(ProjectBlueprint? current, long currentVersion, ChangeSetRequest changeSet);
}
