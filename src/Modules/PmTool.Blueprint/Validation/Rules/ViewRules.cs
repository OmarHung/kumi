using PmTool.Blueprint.Model;
using static PmTool.Blueprint.IssueText;

namespace PmTool.Blueprint.Validation.Rules;

/// <summary>
/// 規則 11：檢視的引用。依檢視順序，每個檢視依 Schema 的屬性順序：
/// groupBy、columns、sort、filter.itemTypes、filter.states、visibleTo。
/// </summary>
internal static class ViewRules
{
    public static IEnumerable<ValidationIssue> Check(ProjectBlueprint blueprint, BlueprintIndex index)
    {
        for (var v = 0; v < blueprint.Views.Count; v++)
        {
            var view = blueprint.Views[v];
            var path = $"/views/{v}";
            IEnumerable<ValidationIssue>[] parts =
            [
                GroupBy(view, path, index),
                ColumnsAndSort(view, path, index),
                Filter(view, path, index),
                VisibleTo(view, path, index),
            ];
            foreach (var issue in parts.SelectMany(part => part))
            {
                yield return issue;
            }
        }
    }

    private static IEnumerable<ValidationIssue> GroupBy(ViewDefinition view, string path, BlueprintIndex index)
    {
        if (view.GroupBy is { } groupBy && !IsGroupable(groupBy, index))
        {
            yield return new ValidationIssue(
                IssueCodes.ViewInvalidGroupBy,
                $"{path}/groupBy",
                $"檢視{Quote(view.Key)}不能依{Quote(groupBy)}分組：只能用 state、assignee、item_type 或一個單選欄位的 key。");
        }
    }

    private static IEnumerable<ValidationIssue> ColumnsAndSort(ViewDefinition view, string path, BlueprintIndex index)
    {
        for (var c = 0; c < view.Columns.Count; c++)
        {
            if (!index.IsField(view.Columns[c]))
            {
                yield return UnknownField(view, view.Columns[c], $"{path}/columns/{c}");
            }
        }

        for (var s = 0; s < view.Sort.Count; s++)
        {
            if (!index.IsField(view.Sort[s].Field))
            {
                yield return UnknownField(view, view.Sort[s].Field, $"{path}/sort/{s}/field");
            }
        }
    }

    // 篩選的狀態只要屬於某個流程就算存在：一個檢視可以同時列出用不同流程的類型。
    private static IEnumerable<ValidationIssue> Filter(ViewDefinition view, string path, BlueprintIndex index)
    {
        var filter = view.Filter ?? new ViewFilter();
        for (var t = 0; t < filter.ItemTypes.Count; t++)
        {
            if (!index.IsItemType(filter.ItemTypes[t]))
            {
                yield return new ValidationIssue(
                    IssueCodes.ViewUnknownItemType,
                    $"{path}/filter/itemTypes/{t}",
                    $"檢視{Quote(view.Key)}篩選的類型{Quote(filter.ItemTypes[t])}沒有在 itemTypes 裡定義。");
            }
        }

        for (var s = 0; s < filter.States.Count; s++)
        {
            if (!index.AllStates.Contains(filter.States[s]))
            {
                yield return new ValidationIssue(
                    IssueCodes.ViewUnknownState,
                    $"{path}/filter/states/{s}",
                    $"檢視{Quote(view.Key)}篩選的狀態{Quote(filter.States[s])}不在任何流程裡。");
            }
        }
    }

    // visibleTo 是 null 表示所有成員都看得到；空清單沒有引用任何角色，也沒有問題可回報。
    private static IEnumerable<ValidationIssue> VisibleTo(ViewDefinition view, string path, BlueprintIndex index)
    {
        var roles = view.VisibleTo ?? [];
        for (var r = 0; r < roles.Count; r++)
        {
            if (!index.IsRole(roles[r]))
            {
                yield return new ValidationIssue(
                    IssueCodes.ViewUnknownRole,
                    $"{path}/visibleTo/{r}",
                    $"檢視{Quote(view.Key)}開放給的角色{Quote(roles[r])}不是內建角色，也沒有在 roles 裡定義。");
            }
        }
    }

    // 只有單選欄位能分組：多選的項目會同時落在好幾組。
    private static bool IsGroupable(string groupBy, BlueprintIndex index) =>
        groupBy is BlueprintVocabulary.State or BlueprintVocabulary.Assignee or BlueprintVocabulary.ItemType
        || (index.Fields.TryGetValue(groupBy, out var field) && field.Type == FieldType.SingleSelect);

    private static ValidationIssue UnknownField(ViewDefinition view, string field, string path) =>
        new(IssueCodes.ViewUnknownField, path, $"檢視{Quote(view.Key)}用到的欄位{Quote(field)}不是核心欄位，也沒有在 fields 裡定義。");
}
