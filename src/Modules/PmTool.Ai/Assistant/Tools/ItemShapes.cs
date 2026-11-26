using System.Text.Json;
using PmTool.Ai.Items;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Ai.Assistant.Tools;

/// <summary>項目、活動回給模型的形狀。人員帶顯示名稱，模型不必另外查。</summary>
internal static class ItemShapes
{
    /// <summary>每則留言最多回這麼多字：整份結果有長度上限，一則超長的留言不能把後面的成員與其他留言擠掉。</summary>
    private const int MaxCommentCharacters = 2_000;

    private const string UnknownPerson = "（已不在組織）";

    public static async Task<PersonNames> NamesAsync(
        AssistantToolContext context,
        IEnumerable<WorkItem> items,
        ProjectDetail? project,
        IEnumerable<ItemActivity> activities,
        CancellationToken cancellationToken)
    {
        var materialized = items.ToArray();
        var ids = materialized.Where(item => item.AssigneeId is not null).Select(item => item.AssigneeId!.Value)
            .Concat(materialized.Select(item => item.CreatedBy))
            .Concat(project?.Members.Select(member => member.PersonId) ?? [])
            .Concat(activities.Select(activity => activity.ActorId))
            .Distinct()
            .ToArray();
        var found = ids.Length == 0
            ? new Dictionary<Guid, PersonSummary>()
            : await context.People.FindManyAsync(context.Actor.OrganizationId, ids, cancellationToken);
        return new PersonNames(found);
    }

    public static object Summary(WorkItem item, PersonNames names) => new
    {
        key = item.Key,
        title = item.Title,
        type = item.Type,
        state = item.State,
        stateCategory = item.StateCategory,
        assignee = item.AssigneeId is { } id ? new { id, name = names.NameOf(id) } : null,
    };

    public static object Detail(WorkItem item, PersonNames names) => new
    {
        key = item.Key,
        projectKey = item.ProjectKey,
        type = item.Type,
        title = item.Title,
        state = item.State,
        stateCategory = item.StateCategory,
        assignee = item.AssigneeId is { } id ? new { id, name = names.NameOf(id) } : null,
        createdBy = new { id = item.CreatedBy, name = names.NameOf(item.CreatedBy) },
        priority = item.Priority,
        startDate = item.StartDate?.ToString("yyyy-MM-dd"),
        dueDate = item.DueDate?.ToString("yyyy-MM-dd"),
        estimate = item.Estimate,
        parentKey = item.ParentKey,
        fields = item.Fields,
        description = item.Description is { } text ? AiText.Cut(text, ItemProposalLimits.MaxEditableDescriptionLength) : null,
        descriptionTruncated = item.Description?.Length > ItemProposalLimits.MaxEditableDescriptionLength,
        descriptionRevision = item.DescriptionRevision,
        createdAt = item.CreatedAt,
        updatedAt = item.UpdatedAt,
    };

    public static object Activity(ItemActivity activity, PersonNames names) => new
    {
        kind = activity.Kind,
        actor = new { id = activity.ActorId, name = names.NameOf(activity.ActorId) },
        at = activity.At,
        changes = activity.Changes.Select(change => new { field = change.Field, from = change.From, to = change.To }),
        fromState = activity.FromState,
        toState = activity.ToState,
        comment = activity.Comment is { } comment ? AiText.Cut(comment, MaxCommentCharacters) : null,
        commentTruncated = activity.Comment?.Length > MaxCommentCharacters,
        // 刪除的留言內容已經在工作引擎藏起來；助理只需要知道有這個旗標，不要把空白當成沒內容的留言。
        edited = activity.EditedAt is not null,
        deleted = activity.DeletedAt is not null,
    };

    internal sealed class PersonNames(IReadOnlyDictionary<Guid, PersonSummary> people)
    {
        public string NameOf(Guid personId) => people.TryGetValue(personId, out var person) ? person.DisplayName : UnknownPerson;

        public string? JobTitleOf(Guid personId) => people.TryGetValue(personId, out var person) ? person.JobTitle : null;

        /// <summary>現在能被指派、被提及的人：查得到（還在組織裡）而且沒有停用。和 MCP 的 get_item 同一套規則。</summary>
        public bool IsAssignable(Guid personId) => people.TryGetValue(personId, out var person) && !person.IsDisabled;
    }
}
