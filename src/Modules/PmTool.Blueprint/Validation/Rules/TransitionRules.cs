using PmTool.Blueprint.Model;
using static PmTool.Blueprint.IssueText;

namespace PmTool.Blueprint.Validation.Rules;

/// <summary>規則 6、8、9：個別的轉換。三條規則各自依流程、再依轉換的順序走一遍。</summary>
internal static class TransitionRules
{
    /// <summary>規則 6。每個轉換依序：from 不存在、to 不存在、自己轉到自己、和前面的轉換重複。</summary>
    public static IEnumerable<ValidationIssue> CheckEndpoints(ProjectBlueprint blueprint, BlueprintIndex index)
    {
        for (var w = 0; w < blueprint.Workflows.Count; w++)
        {
            var workflow = blueprint.Workflows[w];
            var transitions = workflow.Transitions ?? [];
            var states = index.StatesOfWorkflowAt(w);
            var seen = new HashSet<(string From, string To)>();
            for (var t = 0; t < transitions.Count; t++)
            {
                var path = $"/workflows/{w}/transitions/{t}";
                foreach (var issue in Endpoints(workflow.Key, transitions[t], path, states, seen))
                {
                    yield return issue;
                }
            }
        }
    }

    /// <summary>規則 8。</summary>
    public static IEnumerable<ValidationIssue> CheckRoles(ProjectBlueprint blueprint, BlueprintIndex index)
    {
        foreach (var (_, transition, path) in Transitions(blueprint))
        {
            for (var r = 0; r < transition.AllowedRoles.Count; r++)
            {
                var role = transition.AllowedRoles[r];
                if (!index.IsRole(role))
                {
                    yield return new ValidationIssue(
                        IssueCodes.TransitionUnknownRole,
                        $"{path}/allowedRoles/{r}",
                        $"角色{Quote(role)}不是內建角色，也沒有在 roles 裡定義。");
                }
            }
        }
    }

    /// <summary>
    /// 規則 9。每個要求的欄位：不存在就回報不存在；存在的話，依文件順序列出使用這個流程、卻填不了這個欄位的類型。
    /// 核心欄位每種類型都有，可以要求先填；上層項目例外，parents 是空的類型不能有上層，要求它就永遠轉不過去。
    /// </summary>
    public static IEnumerable<ValidationIssue> CheckRequiredFields(ProjectBlueprint blueprint, BlueprintIndex index)
    {
        // 同一個欄位常被同一個流程的很多轉換要求。缺它的類型每個（流程, 欄位）只算一次，
        // 工作量才不會是「轉換 × 欄位 × 類型」；問題數量的上限由呼叫端收集時處理。
        var missing = new Dictionary<(int Workflow, string Field), IReadOnlyList<ItemType>>();
        foreach (var (w, transition, path) in Transitions(blueprint))
        {
            for (var f = 0; f < transition.RequireFields.Count; f++)
            {
                var field = transition.RequireFields[f];
                var fieldPath = $"{path}/requireFields/{f}";
                foreach (var issue in RequiredField(blueprint.Workflows[w].Key, w, field, fieldPath, index, missing))
                {
                    yield return issue;
                }
            }
        }
    }

    private static IEnumerable<ValidationIssue> Endpoints(
        string workflow,
        WorkflowTransition transition,
        string path,
        IReadOnlySet<string> states,
        HashSet<(string From, string To)> seen)
    {
        if (!states.Contains(transition.From))
        {
            yield return UnknownState(workflow, transition.From, $"{path}/from");
        }

        if (!states.Contains(transition.To))
        {
            yield return UnknownState(workflow, transition.To, $"{path}/to");
        }

        if (transition.From == transition.To)
        {
            yield return new ValidationIssue(
                IssueCodes.TransitionSelf, path, $"流程{Quote(workflow)}的轉換不能從{Quote(transition.From)}轉到自己。");
        }

        if (!seen.Add((transition.From, transition.To)))
        {
            yield return new ValidationIssue(
                IssueCodes.TransitionDuplicate,
                path,
                $"流程{Quote(workflow)}裡從{Quote(transition.From)}到{Quote(transition.To)}的轉換重複了。");
        }
    }

    private static IEnumerable<ValidationIssue> RequiredField(
        string workflow,
        int position,
        string field,
        string path,
        BlueprintIndex index,
        Dictionary<(int Workflow, string Field), IReadOnlyList<ItemType>> missing)
    {
        var isParent = field == BlueprintVocabulary.Parent;
        if (!isParent && BlueprintVocabulary.CoreFields.Contains(field))
        {
            yield break;
        }

        if (!isParent && !index.Fields.ContainsKey(field))
        {
            yield return new ValidationIssue(
                IssueCodes.TransitionUnknownField, path, $"轉換要求先填的欄位{Quote(field)}不是核心欄位，也沒有在 fields 裡定義。");
            yield break;
        }

        foreach (var itemType in TypesMissing(position, field, index, missing))
        {
            var reason = isParent
                ? $"但這種類型不能有上層項目，轉換要求先填的{Quote(field)}永遠填不了。"
                : $"但沒有這個轉換要求先填的欄位{Quote(field)}。";
            yield return new ValidationIssue(
                IssueCodes.TransitionFieldMissingOnType, path, $"類型{Quote(itemType.Key)}使用流程{Quote(workflow)}，{reason}");
        }
    }

    private static IReadOnlyList<ItemType> TypesMissing(
        int position,
        string field,
        BlueprintIndex index,
        Dictionary<(int Workflow, string Field), IReadOnlyList<ItemType>> missing)
    {
        if (!missing.TryGetValue((position, field), out var types))
        {
            types = index.ItemTypesUsingWorkflowAt(position).Where(t => !CanFill(t, field, index)).ToArray();
            missing[(position, field)] = types;
        }

        return types;
    }

    /// <summary>上層項目要這種類型能掛在別的類型底下；自訂欄位要這種類型列了它。</summary>
    private static bool CanFill(ItemType itemType, string field, BlueprintIndex index) =>
        field == BlueprintVocabulary.Parent ? itemType.Parents.Count > 0 : index.ItemTypeHasField(itemType, field);

    private static IEnumerable<(int Workflow, WorkflowTransition Transition, string Path)> Transitions(ProjectBlueprint blueprint)
    {
        for (var w = 0; w < blueprint.Workflows.Count; w++)
        {
            var transitions = blueprint.Workflows[w].Transitions ?? [];
            for (var t = 0; t < transitions.Count; t++)
            {
                yield return (w, transitions[t], $"/workflows/{w}/transitions/{t}");
            }
        }
    }

    private static ValidationIssue UnknownState(string workflow, string state, string path) =>
        new(IssueCodes.TransitionUnknownState, path, $"{Quote(state)}不是流程{Quote(workflow)}的狀態。");
}
