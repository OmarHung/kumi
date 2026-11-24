namespace PmTool.Blueprint.Planning;

/// <summary>
/// 套用一份變更集之前算出來的計畫：會改到哪些設定、會動到哪些既有資料。
/// <see cref="Report"/> 有問題時不能送去核准；最常見的原因是會動到資料的修改沒有寫明怎麼處理。
/// </summary>
/// <param name="Changes">新藍圖相對於目前藍圖的差異，順序固定。</param>
/// <param name="DataImpacts">套用時要對既有資料做的事，每一項都已經有明確的處理方式。</param>
/// <param name="Report">擋下這份計畫的問題，路徑指向變更集文件。</param>
public sealed record BlueprintPlan(
    IReadOnlyList<BlueprintChange> Changes,
    IReadOnlyList<DataImpact> DataImpacts,
    ValidationReport Report);
