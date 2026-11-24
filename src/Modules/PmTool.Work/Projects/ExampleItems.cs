using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Blueprint.Model;
using PmTool.Work.Access;
using PmTool.Work.Common;
using PmTool.Work.Items;
using PmTool.Work.Persistence;
using PmTool.Work.Templates;

namespace PmTool.Work.Projects;

/// <summary>
/// 開專案時附上的範例項目。只把它們加進 <paramref name="db"/> 的變更追蹤，和專案一起儲存，
/// 要嘛全部成功、要嘛連專案也沒有；每一筆走 <see cref="ItemCreation"/>，留下的活動與事件和一般建立的一樣。
/// </summary>
internal static class ExampleItems
{
    public static void AddTo(
        WorkDbContext db, Actor actor, ProjectAccess access, ExampleSet set, DateTimeOffset createdAt, TimeZoneInfo zone)
    {
        // 日期只是示意，但「今天」要是使用者的今天：容器是 UTC，台灣早上八點前建立的專案，用 UTC 算會早一天。
        var today = OrganizationTime.Today(zone, createdAt);
        var cycleIds = AddCycles(db, access, set.Cycles, today, createdAt);
        var milestoneIds = AddMilestones(db, access, set.Milestones, today, createdAt);
        var examples = set.Items;
        var idOfRef = new Dictionary<string, Guid>(StringComparer.Ordinal);
        for (var index = 0; index < examples.Count; index++)
        {
            var example = examples[index];
            var type = access.Schema.ItemTypes[example.Type];
            var item = ToEntity(access, type, example, example.Resolve(access.Schema, today), index, createdAt, idOfRef) with
            {
                CycleId = example.Cycle is { } cycle ? cycleIds[cycle] : null,
                MilestoneId = example.Milestone is { } milestone ? milestoneIds[milestone] : null,
            };
            // 範例的說明不放提及：沒有真的人可以提及，也就不必去查通知對象。
            ItemCreation.Add(db, actor, access, type, item, []);
            if (example.Ref is { } reference)
            {
                idOfRef[reference] = item.Id;
            }
        }
    }

    private static Dictionary<string, Guid> AddCycles(
        WorkDbContext db, ProjectAccess access, IReadOnlyList<ExampleCycle> cycles, DateOnly today, DateTimeOffset createdAt)
    {
        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var cycle in cycles)
        {
            var resolved = cycle.Resolve(today);
            var at = createdAt.AddTicks(cycle.Position * TimeSpan.TicksPerMicrosecond);
            ids[cycle.Ref] = Guid.CreateVersion7(at);
            db.Cycles.Add(new CycleEntity
            {
                Id = ids[cycle.Ref],
                OrganizationId = access.Project.OrganizationId,
                ProjectId = access.Project.Id,
                Name = resolved.Name,
                StartDate = resolved.StartDate,
                EndDate = resolved.EndDate,
                CreatedBy = access.Project.CreatedBy,
                CreatedAt = at,
                UpdatedAt = at,
            });
        }

        return ids;
    }

    private static Dictionary<string, Guid> AddMilestones(
        WorkDbContext db, ProjectAccess access, IReadOnlyList<ExampleMilestone> milestones, DateOnly today, DateTimeOffset createdAt)
    {
        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var milestone in milestones)
        {
            var resolved = milestone.Resolve(today);
            var at = createdAt.AddTicks(milestone.Position * TimeSpan.TicksPerMicrosecond);
            ids[milestone.Ref] = Guid.CreateVersion7(at);
            db.Milestones.Add(new MilestoneEntity
            {
                Id = ids[milestone.Ref],
                OrganizationId = access.Project.OrganizationId,
                ProjectId = access.Project.Id,
                Name = resolved.Name,
                DueDate = resolved.DueDate,
                Description = resolved.Description,
                CreatedBy = access.Project.CreatedBy,
                CreatedAt = at,
                UpdatedAt = at,
            });
        }

        return ids;
    }

    // 每一筆晚一微秒：UUIDv7 只到毫秒，同一毫秒裡的主鍵順序是亂的，所以排序靠的是 CreatedAt 加 Number，不是主鍵。
    private static ItemEntity ToEntity(
        ProjectAccess access,
        ItemType type,
        ExampleItem example,
        ResolvedExample resolved,
        int index,
        DateTimeOffset createdAt,
        Dictionary<string, Guid> idOfRef)
    {
        var at = createdAt.AddTicks((index + 1) * TimeSpan.TicksPerMicrosecond);
        var state = access.Schema.WorkflowOf(type).States.First(s => example.State is null || s.Key == example.State);
        return new ItemEntity
        {
            Id = Guid.CreateVersion7(at),
            OrganizationId = access.Project.OrganizationId,
            ProjectId = access.Project.Id,
            Number = index + 1,
            Type = type.Key,
            Title = resolved.Title,
            Description = resolved.Description,
            State = state.Key,
            StateCategory = StateCategoryText.ToText(state.Category),
            Priority = (short)example.Priority,
            StartDate = resolved.StartDate,
            DueDate = resolved.DueDate,
            Estimate = resolved.Estimate,
            ParentId = example.Parent is { } parent ? idOfRef[parent] : null,
            Fields = FieldValues.Serialize(FieldValues.Merge(
                FieldValues.None, resolved.Fields.ToDictionary(pair => pair.Key, pair => (JsonElement?)pair.Value, StringComparer.Ordinal))),
            CreatedBy = access.Project.CreatedBy,
            CreatedAt = at,
            UpdatedAt = at,
        };
    }
}
