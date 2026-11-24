using PmTool.Abstractions;
using PmTool.Blueprint.Model;
using PmTool.Work.Access;
using PmTool.Work.Common;
using PmTool.Work.Persistence;

namespace PmTool.Work.Items;

/// <summary>
/// 新增與修改項目時的檢查。所有問題一起回報，使用者不必改一個、送一次；Details 的路徑是欄位的 key。
/// 新增時 <paramref name="current"/> 是 null。
/// </summary>
internal static class ItemRules
{
    public static async Task<Result<ItemChanges>> CheckAsync(
        WorkDbContext db,
        ProjectAccess access,
        ItemType itemType,
        ItemEntity? current,
        WorkItemPatch patch,
        CancellationToken cancellationToken)
    {
        var title = CoreFieldRules.Title(patch.Title);
        var description = CoreFieldRules.Description(patch.Description);
        var priority = CoreFieldRules.Priority(patch.Priority);
        var estimate = CoreFieldRules.Estimate(patch.Estimate);
        var schedule = CoreFieldRules.Schedule(patch.StartDate, patch.DueDate, current);
        var fields = CustomFieldRules.Check(access.Schema, itemType, patch.Fields);
        var members = await MemberRules.CheckAsync(db, access, [.. AssigneeOf(patch), .. fields.People], cancellationToken);
        var parent = await ParentRules.CheckAsync(db, access, itemType, current, patch.ParentKey, cancellationToken);
        var planning = await PlanningReferenceRules.CheckAsync(db, access.Project, patch, cancellationToken);
        FailureDetail[] problems =
        [
            .. new[] { title.Problem, description.Problem, priority.Problem, schedule, estimate.Problem, parent.Problem }.OfType<FailureDetail>(),
            .. fields.Problems,
            .. members,
            .. planning,
        ];
        if (problems.Length > 0)
        {
            return WorkFailures.Invalid(problems);
        }

        return new ItemChanges
        {
            Title = title.Value,
            Description = description.Value,
            AssigneeId = patch.AssigneeId,
            Priority = priority.Value,
            StartDate = patch.StartDate,
            DueDate = patch.DueDate,
            Estimate = estimate.Value,
            Parent = parent.Value,
            CycleId = patch.CycleId,
            MilestoneId = patch.MilestoneId,
            Fields = fields.Changes,
        };
    }

    private static IEnumerable<PersonReference> AssigneeOf(WorkItemPatch patch) =>
        patch.AssigneeId is { HasValue: true, Value: { } assignee }
            ? [new PersonReference(CoreFields.Assignee, CoreFields.DisplayName(CoreFields.Assignee)!, assignee)]
            : [];
}
