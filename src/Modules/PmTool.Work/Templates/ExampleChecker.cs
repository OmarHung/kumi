using PmTool.Blueprint.Model;
using PmTool.Work.Blueprints;
using PmTool.Work.Items;
using static PmTool.Work.Common.TextRules;

namespace PmTool.Work.Templates;

/// <summary>
/// 範例的內容檢查：類型與狀態存在、參照對得上、欄位的值合規。長度、控制字元與欄位型別用的就是一般新增項目的規則，
/// 範例才不會存進一般使用者存不進去的東西。所有問題一起回報，改檔的人不必改一個、跑一次。
/// </summary>
internal static class ExampleChecker
{
    public static IReadOnlyList<string> Check(
        string templateKey,
        ProjectSchema schema,
        IReadOnlyList<ExampleItem> items,
        DateOnly today,
        IReadOnlyList<ExampleCycle> cycles,
        IReadOnlyList<ExampleMilestone> milestones)
    {
        var cycleRefs = cycles.Select(c => c.Ref).ToHashSet(StringComparer.Ordinal);
        var milestoneRefs = milestones.Select(m => m.Ref).ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();
        var typeOfRef = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            void Add(string path, string message) => problems.Add(ExampleProblems.Item(templateKey, item.Position, path, message));

            CheckRef(item, typeOfRef, Add);
            CheckPlanningRef(item.Cycle, cycleRefs, "cycle", "週期", "cycles", Add);
            CheckPlanningRef(item.Milestone, milestoneRefs, "milestone", "里程碑", "milestones", Add);
            if (!schema.ItemTypes.TryGetValue(item.Type, out var type))
            {
                Add("type", $"模板沒有{Quote(item.Type)}這種項目類型。");
                continue;
            }

            CheckState(schema, type, item, Add);
            CheckParent(schema, type, item, typeOfRef, Add);
            CheckCoreFields(item, today, Add);
            foreach (var problem in ExampleFields.Check(schema, type, item.Fields, today).Problems)
            {
                Add($"fields.{problem.Path}", problem.Message);
            }

            RememberRef(item, typeOfRef);
        }

        return problems;
    }

    private static void CheckPlanningRef(
        string? reference, HashSet<string> known, string path, string label, string listKey, Action<string, string> add)
    {
        if (reference is not null && !known.Contains(reference))
        {
            add(path, $"{label}{Quote(reference)}要是 {listKey} 裡出現過的 ref。");
        }
    }

    private static void CheckRef(ExampleItem item, Dictionary<string, string> typeOfRef, Action<string, string> add)
    {
        if (item.Ref is { } reference && typeOfRef.ContainsKey(reference))
        {
            add("ref", $"ref {Quote(reference)}已經用過了，同一個檔案裡不能重複。");
        }
    }

    private static void RememberRef(ExampleItem item, Dictionary<string, string> typeOfRef)
    {
        if (item.Ref is { } reference)
        {
            typeOfRef.TryAdd(reference, item.Type);
        }
    }

    private static void CheckState(ProjectSchema schema, ItemType type, ExampleItem item, Action<string, string> add)
    {
        if (item.State is { } state && schema.StateOf(type, state) is null)
        {
            var known = string.Join("、", schema.WorkflowOf(type).States.Select(s => s.Key));
            add("state", $"{Quote(type.Name)}的流程沒有{Quote(state)}這個狀態。可以用的狀態：{known}。");
        }
    }

    private static void CheckParent(
        ProjectSchema schema, ItemType type, ExampleItem item, Dictionary<string, string> typeOfRef, Action<string, string> add)
    {
        if (item.Parent is not { } parent)
        {
            return;
        }

        if (!typeOfRef.TryGetValue(parent, out var parentType))
        {
            add("parent", $"上層{Quote(parent)}要是前面已經出現過的 ref。");
            return;
        }

        if (!type.Parents.Contains(parentType))
        {
            var parentName = schema.ItemTypes.GetValueOrDefault(parentType)?.Name ?? parentType;
            add("parent", $"{Quote(type.Name)}不能掛在{Quote(parentName)}底下。");
        }
    }

    private static void CheckCoreFields(ExampleItem item, DateOnly today, Action<string, string> add)
    {
        if (CoreFieldRules.Title(item.Title).Problem is { } title)
        {
            add("title", title.Message);
        }

        if (CoreFieldRules.Description(item.Description).Problem is { } description)
        {
            add("description", description.Message);
        }

        if (CoreFieldRules.Estimate(item.Estimate).Problem is { } estimate)
        {
            add("estimate", estimate.Message);
        }

        var start = item.StartInDays is { } s ? today.AddDays(s) : (DateOnly?)null;
        var due = item.DueInDays is { } d ? today.AddDays(d) : (DateOnly?)null;
        if (CoreFieldRules.Schedule(start, due, null) is { } schedule)
        {
            add("dueInDays", schedule.Message);
        }
    }
}
