using PmTool.Abstractions;
using PmTool.Blueprint.Model;
using PmTool.Work.Access;
using PmTool.Work.Blueprints;
using PmTool.Work.Common;
using PmTool.Work.Persistence;
using static PmTool.Work.Common.TextRules;

namespace PmTool.Work.Items;

/// <summary>
/// 改狀態的規則，依序：流程有沒有這條轉換、呼叫者被指派的角色在不在允許的角色裡、要求先填的欄位有沒有值。
/// 角色看的是專案裡被指派的角色，組織管理員也一樣：否則限定角色的轉換對管理員形同虛設。
/// </summary>
internal static class TransitionGuard
{
    private const string RolePath = "role";

    /// <param name="role">呼叫者在專案裡被指派的角色；不是成員時是 null。</param>
    public static Result<WorkflowState> Check(ProjectSchema schema, ItemType itemType, ItemEntity item, string? role, string? toState)
    {
        var workflow = schema.WorkflowOf(itemType);
        var from = StateName(workflow, item.State);
        var to = workflow.States.FirstOrDefault(s => s.Key == toState);
        if (to is null)
        {
            return NotAllowed($"{Quote(workflow.Name)}沒有{Quote(toState)}這個狀態。");
        }

        if (to.Key == item.State)
        {
            return NotAllowed($"這個項目已經是{Quote(to.Name)}了。");
        }

        if (workflow.Transitions is not { } transitions)
        {
            return to;
        }

        var transition = transitions.FirstOrDefault(t => t.From == item.State && t.To == to.Key);
        if (transition is null)
        {
            var targets = transitions.Where(t => t.From == item.State).Select(t => Quote(StateName(workflow, t.To))).ToArray();
            var next = targets.Length == 0 ? $"{Quote(from)}沒有可以轉去的狀態。" : $"從{Quote(from)}可以轉到：{string.Join("、", targets)}。";
            return NotAllowed($"{Quote(from)}不能直接轉到{Quote(to.Name)}。{next}");
        }

        if (transition.AllowedRoles.Count > 0 && (role is null || !transition.AllowedRoles.Contains(role)))
        {
            return RoleRequired(schema, transition, from, to, role);
        }

        var missing = MissingFields(item, transition.RequireFields);
        return missing.Count == 0 ? to : FieldsRequired(schema, missing, to);
    }

    private static Failure RoleRequired(ProjectSchema schema, WorkflowTransition transition, string from, WorkflowState to, string? role)
    {
        var names = string.Join("、", transition.AllowedRoles.Select(r => Quote(ProjectRoles.DisplayName(r, schema))));
        var mine = role is null ? "你不是這個專案的成員。" : $"你在這個專案的角色是{Quote(ProjectRoles.DisplayName(role, schema))}。";
        return new Failure(FailureKind.Forbidden, WorkErrorCodes.TransitionRoleRequired, $"從{Quote(from)}轉到{Quote(to.Name)}只有{names}可以做，{mine}")
        {
            Details = [.. transition.AllowedRoles.Select(r => new FailureDetail(
                WorkErrorCodes.TransitionRoleRequired, RolePath, $"{Quote(ProjectRoles.DisplayName(r, schema))}（{r}）"))],
        };
    }

    private static Failure FieldsRequired(ProjectSchema schema, IReadOnlyList<string> missing, WorkflowState to)
    {
        var details = missing
            .Select(f => new FailureDetail(WorkErrorCodes.TransitionFieldsRequired, f, $"要先填{Quote(FieldName(schema, f))}。"))
            .ToArray();
        var names = string.Join("、", missing.Select(f => Quote(FieldName(schema, f))));
        return WorkFailures.Invalid(details) with { Message = $"要先填{names}才能進入{Quote(to.Name)}。" };
    }

    /// <summary>
    /// 核心欄位看它本身有沒有值（優先順序「無」算沒填、空白的說明算沒填）；自訂欄位看鍵在不在，空值本來就不存。
    /// 說明與上層項目是保留的名字，藍圖不允許自訂欄位用，所以不會和自訂欄位混淆。
    /// </summary>
    internal static IReadOnlyList<string> MissingFields(ItemEntity item, IReadOnlyList<string> required)
    {
        if (required.Count == 0)
        {
            return [];
        }

        var fields = FieldValues.Parse(item.Fields);
        return [.. required.Where(field => !(field switch
        {
            CoreFields.Title or CoreFields.State or CoreFields.ItemType => true,
            CoreFields.Assignee => item.AssigneeId is not null,
            CoreFields.Priority => item.Priority != (short)Priority.None,
            CoreFields.StartDate => item.StartDate is not null,
            CoreFields.DueDate => item.DueDate is not null,
            CoreFields.Estimate => item.Estimate is not null,
            CoreFields.Description => !string.IsNullOrWhiteSpace(item.Description),
            CoreFields.Parent => item.ParentId is not null,
            _ => fields.ContainsKey(field),
        }))];
    }

    private static string FieldName(ProjectSchema schema, string field) =>
        schema.Fields.GetValueOrDefault(field)?.Name ?? CoreFields.DisplayName(field) ?? field;

    private static string StateName(Workflow workflow, string state) =>
        workflow.States.FirstOrDefault(s => s.Key == state)?.Name ?? state;

    private static Failure NotAllowed(string message) =>
        WorkFailures.Invalid(WorkErrorCodes.TransitionNotAllowed, CoreFields.State, message);
}
