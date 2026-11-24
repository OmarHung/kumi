using PmTool.Blueprint.Model;
using static PmTool.Blueprint.IssueText;

namespace PmTool.Blueprint.Validation.Rules;

/// <summary>
/// 規則 10：類型的引用。依類型順序，每個類型依序：流程、欄位（依列出順序）、上層（依列出順序）。
/// 上層的問題在同一個位置只回報一種：不存在優先，否則看它有沒有把圈閉合。
/// </summary>
internal static class ItemTypeRules
{
    public static IEnumerable<ValidationIssue> Check(ProjectBlueprint blueprint, BlueprintIndex index)
    {
        var closingEdges = ParentCycles.FindClosingEdges(blueprint.ItemTypes, index.ItemTypePositions);
        for (var i = 0; i < blueprint.ItemTypes.Count; i++)
        {
            var itemType = blueprint.ItemTypes[i];
            var path = $"/itemTypes/{i}";
            if (!index.WorkflowPositions.ContainsKey(itemType.Workflow))
            {
                yield return new ValidationIssue(
                    IssueCodes.ItemTypeUnknownWorkflow,
                    $"{path}/workflow",
                    $"類型{Quote(itemType.Key)}使用的流程{Quote(itemType.Workflow)}沒有在 workflows 裡定義。");
            }

            foreach (var issue in Fields(itemType, path, index).Concat(Parents(itemType, i, path, index, closingEdges)))
            {
                yield return issue;
            }
        }
    }

    // 同一個欄位第二次出現時回報重複；第一次出現時才看它存不存在，同一個位置不會有兩個問題。
    // 類型的 fields 只列自訂欄位：核心欄位每種類型本來就有，列出來是寫錯了。
    private static IEnumerable<ValidationIssue> Fields(ItemType itemType, string path, BlueprintIndex index)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var f = 0; f < itemType.Fields.Count; f++)
        {
            var field = itemType.Fields[f];
            if (!seen.Add(field))
            {
                yield return new ValidationIssue(
                    IssueCodes.ItemTypeDuplicateField,
                    $"{path}/fields/{f}",
                    $"類型{Quote(itemType.Key)}的欄位{Quote(field)}列了不只一次。");
            }
            else if (!index.Fields.ContainsKey(field))
            {
                yield return new ValidationIssue(
                    IssueCodes.ItemTypeUnknownField,
                    $"{path}/fields/{f}",
                    $"類型{Quote(itemType.Key)}的欄位{Quote(field)}沒有在 fields 裡定義。");
            }
        }
    }

    private static IEnumerable<ValidationIssue> Parents(
        ItemType itemType,
        int position,
        string path,
        BlueprintIndex index,
        IReadOnlySet<(int Type, int Parent)> closingEdges)
    {
        for (var p = 0; p < itemType.Parents.Count; p++)
        {
            var parent = itemType.Parents[p];
            if (!index.IsItemType(parent))
            {
                yield return new ValidationIssue(
                    IssueCodes.ItemTypeUnknownParent,
                    $"{path}/parents/{p}",
                    $"類型{Quote(itemType.Key)}的上層類型{Quote(parent)}沒有在 itemTypes 裡定義。");
            }
            else if (closingEdges.Contains((position, p)))
            {
                yield return new ValidationIssue(
                    IssueCodes.ItemTypeParentCycle,
                    $"{path}/parents/{p}",
                    $"類型{Quote(itemType.Key)}把{Quote(parent)}當成上層，上層關係因此繞成一圈。");
            }
        }
    }
}
