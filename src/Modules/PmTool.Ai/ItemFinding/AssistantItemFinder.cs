using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Ai.ItemFinding;

/// <summary>
/// 助理跨專案查項目（規格見 <see cref="IAssistantItemFinder"/>）。權限一律由工作引擎照發問的人（<c>ItemScope</c>）過濾，這裡不另外判斷；
/// 這裡只負責條件的檢查、補上專案名稱與人名，以及把「讀到別人的項目」的專案記進對話。
/// </summary>
internal sealed class AssistantItemFinder(
    IWorkItemService items,
    IProjectService projects,
    IPeopleDirectory people,
    IOrganizationClock clock,
    IDbContextFactory<AiDbContext> contexts,
    TimeProvider time) : IAssistantItemFinder
{
    private const string UnknownPersonName = "（未知的人）";

    public async Task<Result<ItemFindResult>> FindAsync(
        Actor actor, Guid? conversationId, ItemFindQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(query);

        var zone = await clock.TimeZoneAsync(actor.OrganizationId, cancellationToken);
        var work = ItemFindArguments.ToWorkQuery(query, actor.PersonId, OrganizationTime.Today(zone, time.GetUtcNow()));
        if (work.Failure is { } invalid)
        {
            return invalid;
        }

        if (await CheckPeopleAsync(actor, query, cancellationToken) is { } unknownPerson)
        {
            return unknownPerson;
        }

        var listed = query.ProjectKey is { } key
            ? await items.ListAsync(actor, key, work.Value, cancellationToken)
            : await items.ListAcrossProjectsAsync(actor, CrossProjectScope.All, work.Value, cancellationToken);
        if (listed.Failure is { } failure)
        {
            return failure;
        }

        var found = listed.Value.Items.Take(query.Limit).ToList();
        var hasMore = listed.Value.Items.Count > query.Limit || listed.Value.NextCursor is not null;
        var visibleProjects = await projects.ListAsync(actor, cancellationToken);
        var byKey = visibleProjects.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);
        var names = await NamesAsync(actor, found, cancellationToken);
        await RememberOthersProjectsAsync(actor, conversationId, found, byKey, cancellationToken);

        var result = found.Select(item => new FoundItem(
            item.Key,
            item.ProjectKey,
            byKey.TryGetValue(item.ProjectKey, out var project) ? project.Name : item.ProjectKey,
            item.Title,
            item.Type,
            item.State,
            ItemFindArguments.CategoryName(item.StateCategory),
            item.DueDate,
            ItemFindArguments.PriorityName(item.Priority),
            item.AssigneeId is { } assigneeId ? Person(assigneeId, names) : null,
            Person(item.CreatedBy, names))).ToList();
        return new ItemFindResult(result, hasMore);
    }

    /// <summary>負責人、建立者要是這個組織的人：不在組織裡的編號不查，也不透露它存不存在別處。</summary>
    private async Task<Failure?> CheckPeopleAsync(Actor actor, ItemFindQuery query, CancellationToken cancellationToken)
    {
        var wanted = new Dictionary<string, Guid>();
        if (query.Assignee == ItemFindAssignee.Person && query.AssigneeId is { } assigneeId)
        {
            wanted["assignee"] = assigneeId;
        }

        if (query.CreatedBy is { } createdBy)
        {
            wanted["createdBy"] = createdBy;
        }

        if (wanted.Count == 0)
        {
            return null;
        }

        var found = await people.FindManyAsync(actor.OrganizationId, [.. wanted.Values.Distinct()], cancellationToken);
        var missing = wanted.FirstOrDefault(entry => !found.ContainsKey(entry.Value));
        return missing.Key is null ? null : ItemFindArguments.Invalid(missing.Key, "找不到這個人，請用 list_people 查人員編號。");
    }

    private async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(
        Actor actor, IReadOnlyList<WorkItem> found, CancellationToken cancellationToken)
    {
        var ids = found.SelectMany(item => new[] { (Guid?)item.CreatedBy, item.AssigneeId }).OfType<Guid>().Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var summaries = await people.FindManyAsync(actor.OrganizationId, ids, cancellationToken);
        return summaries.ToDictionary(entry => entry.Key, entry => entry.Value.DisplayName);
    }

    private static FoundPerson Person(Guid id, IReadOnlyDictionary<Guid, string> names) =>
        new(id, names.GetValueOrDefault(id, UnknownPersonName));

    /// <summary>
    /// 結果裡有不是他負責、也不是他建立的項目，那個專案就記進這則對話讀過的專案。同一個專案只記一次（唯一鍵，重複的略過）；
    /// 只在給了對話編號、而且對話是他的時候記（對話不存在或不是他的就一筆都不寫）。
    /// </summary>
    private async Task RememberOthersProjectsAsync(
        Actor actor,
        Guid? conversationId,
        IReadOnlyList<WorkItem> found,
        IReadOnlyDictionary<string, ProjectSummary> projectsByKey,
        CancellationToken cancellationToken)
    {
        if (conversationId is not { } conversation)
        {
            return;
        }

        var projectIds = found
            .Where(item => item.AssigneeId != actor.PersonId && item.CreatedBy != actor.PersonId)
            .Select(item => projectsByKey.TryGetValue(item.ProjectKey, out var project) ? project.Id : (Guid?)null)
            .OfType<Guid>()
            .Distinct()
            .ToList();
        if (projectIds.Count == 0)
        {
            return;
        }

        await ConversationReadWriter.WriteAsync(contexts, actor, conversation, projectIds, time.GetUtcNow(), cancellationToken);
    }
}
