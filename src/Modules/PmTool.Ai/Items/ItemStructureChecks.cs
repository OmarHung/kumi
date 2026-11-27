using System.Text.Json;
using PmTool.Blueprint.Model;
using PmTool.Work;

namespace PmTool.Ai.Items;

/// <summary>
/// 修改建議裡改類型、上層、週期、里程碑的檢查（internal/HANDOFF-ai-coverage.md 5.1）。
/// 上層的類型規則照 <c>PmTool.Work/Items/ParentRules.cs</c>：空的 Parents 是只能放在最上層。
/// </summary>
internal static class ItemStructureChecks
{
    /// <summary>藍圖裡的類型；找不到是 null。</summary>
    public static ItemType? TypeOf(string? key, ProjectDetail project) => project.Blueprint.ItemTypes.FirstOrDefault(t => t.Key == key);

    public static string? CheckType(JsonElement value, ProjectDetail project, bool parentAlsoChanges, ItemChangeFacts facts)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return "type 要是類型的 key。";
        }

        if (TypeOf(value.GetString(), project) is not { } next)
        {
            var known = string.Join("、", project.Blueprint.ItemTypes.Select(t => t.Key));
            return $"這個專案沒有類型「{value.GetString()}」。可用的類型：{known}。";
        }

        if (!parentAlsoChanges && facts.CurrentParent is { } parent && ParentTypeProblem(next, parent.Type, project) is { } parentProblem)
        {
            return $"改成「{next.Name}」之後，現在的上層 {parent.Key} 放不下它：{parentProblem}請一起用 parentKey 換上層，或先不改類型。";
        }

        var blocked = facts.Children.Where(child => ParentTypeProblem(TypeOf(child.Type, project), next.Key, project) is not null).ToList();
        return blocked.Count == 0
            ? null
            : $"下層項目 {string.Join("、", blocked.Select(c => c.Key))} 的類型不能掛在「{next.Name}」底下，不能改成這個類型。";
    }

    /// <param name="effectiveType">這份建議套用後項目的類型（有改類型時是新的）。</param>
    public static string? CheckParent(JsonElement value, WorkItem item, ItemType? effectiveType, ProjectDetail project, ItemChangeFacts facts)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return "parentKey 要是上層項目的編號，null 是移到最上層。";
        }

        if (facts.NewParent is not { } parent)
        {
            return $"找不到上層項目「{value.GetString()}」；上層項目要在這個專案裡、你看得到、沒有刪除。";
        }

        if (facts.NewParentChain.Contains(item.Key))
        {
            return string.Equals(parent.Key, item.Key, StringComparison.OrdinalIgnoreCase)
                ? "項目不能是自己的上層。"
                : $"{parent.Key} 是這個項目的下層，不能把它當上層（會繞圈）。";
        }

        return ParentTypeProblem(effectiveType, parent.Type, project);
    }

    public static string? CheckPlanningId(JsonElement value, string field, string name, IReadOnlySet<Guid>? known)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String || !Guid.TryParse(value.GetString(), out var id))
        {
            return $"{field} 要是{name}的 id（GUID），null 是清掉。";
        }

        if (known is null)
        {
            return $"你讀不到這個專案的{name}。";
        }

        return known.Contains(id) ? null : $"這個專案沒有這個{name}；{field} 要用 get_planning 回的{name}的 id。";
    }

    private static string? ParentTypeProblem(ItemType? type, string? parentTypeKey, ProjectDetail project)
    {
        if (type is null || parentTypeKey is null || type.Parents.Contains(parentTypeKey))
        {
            return null;
        }

        if (type.Parents.Count == 0)
        {
            return $"「{type.Name}」只能放在最上層，不能掛在其他項目底下。";
        }

        var allowed = type.Parents.Select(p => $"「{TypeOf(p, project)?.Name ?? p}」");
        var parentName = TypeOf(parentTypeKey, project)?.Name ?? parentTypeKey;
        return $"「{type.Name}」不能掛在「{parentName}」底下，只能掛在{string.Join("、", allowed)}底下。";
    }
}
