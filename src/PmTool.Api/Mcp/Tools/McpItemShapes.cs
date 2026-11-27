using PmTool.Abstractions;
using PmTool.Api.Endpoints;
using PmTool.Work;

namespace PmTool.Api.Mcp.Tools;

/// <summary>項目在 MCP 上的形狀：和 API 同一份（<see cref="ItemData"/>），負責人與建立者帶顯示名稱，代理不必另外查人員。</summary>
internal static class McpItemShapes
{
    /// <summary>get_item 帶回的附件數上限；只給中繼資料，不給內容。</summary>
    public const int MaxAttachmentsInItem = 50;

    public static async Task<ItemDetailData> DetailAsync(McpToolContext context, WorkItem item, CancellationToken cancellationToken)
    {
        var list = await ListAsync(context, [item], cancellationToken);
        return new ItemDetailData(list[0], item.Description);
    }

    public static Task<IReadOnlyList<ItemData>> ListAsync(
        McpToolContext context, IReadOnlyList<WorkItem> items, CancellationToken cancellationToken) =>
        ItemData.FromManyAsync(items, context.Services.People, context.Actor.OrganizationId, cancellationToken);

    /// <summary>項目所在的週期與里程碑（id 加名稱）；沒有就是 null。名稱要查專案的週期與里程碑，所以只在項目真的有時才查。</summary>
    public static async Task<(object? Cycle, object? Milestone)> PlanningOfAsync(
        McpToolContext context, WorkItem item, CancellationToken cancellationToken)
    {
        object? cycle = null;
        object? milestone = null;
        if (item.CycleId is { } cycleId)
        {
            var cycles = await context.Services.Planning.ListCyclesAsync(context.Actor, item.ProjectKey, cancellationToken);
            // 名稱讀不到不讓整個 get_item 失敗：id 照給，名稱是 null。
            cycle = new { id = cycleId, name = cycles.Failure is null ? cycles.Value.FirstOrDefault(found => found.Id == cycleId)?.Name : null };
        }

        if (item.MilestoneId is { } milestoneId)
        {
            var milestones = await context.Services.Planning.ListMilestonesAsync(context.Actor, item.ProjectKey, cancellationToken);
            milestone = new { id = milestoneId, name = milestones.Failure is null ? milestones.Value.FirstOrDefault(found => found.Id == milestoneId)?.Name : null };
        }

        return (cycle, milestone);
    }

    /// <summary>附件的中繼資料（最多 <see cref="MaxAttachmentsInItem"/> 個），不給內容與雜湊。</summary>
    public static async Task<List<object>> AttachmentsOfAsync(
        McpToolContext context, string itemKey, CancellationToken cancellationToken)
    {
        var result = await context.Services.Attachments.ListAsync(context.Actor, itemKey, cancellationToken);
        if (result.Failure is not null)
        {
            // 附件讀不到時給空陣列，其他照常回（內建的 get_item 也是這樣）。
            return [];
        }

        List<object> shaped =
        [
            .. result.Value.Take(MaxAttachmentsInItem).Select(attachment => (object)new
            {
                id = attachment.Id,
                fileName = attachment.FileName,
                contentType = attachment.ContentType,
                sizeBytes = attachment.SizeBytes,
                uploadedAt = attachment.UploadedAt,
            }),
        ];
        return shaped;
    }

    /// <summary>活動的形狀和 API 的一樣，多一個 mine：是不是授權的人自己做的（要編輯、刪除自己的留言前先知道）。</summary>
    public static async Task<IReadOnlyList<McpActivityData>> ActivitiesAsync(
        McpToolContext context, IReadOnlyList<ItemActivity> activities, CancellationToken cancellationToken)
    {
        var data = await ActivityData.FromManyAsync(activities, context.Services.People, context.Actor.OrganizationId, cancellationToken);
        return [.. data.Select(activity => new McpActivityData(activity, activity.Actor.PersonId == context.Actor.PersonId))];
    }
}

/// <summary>get_item 的活動：<see cref="ActivityData"/> 的欄位加 <c>mine</c>。</summary>
internal sealed record McpActivityData(
    Guid Id,
    ActivityKind Kind,
    PersonReference Actor,
    DateTimeOffset At,
    IReadOnlyList<FieldChangeData> Changes,
    string? FromState,
    string? ToState,
    string? Comment,
    Guid? ReplyToId,
    string? ImportedFrom,
    long? BlueprintVersion,
    DateTimeOffset? EditedAt,
    DateTimeOffset? DeletedAt,
    PersonReference? DeletedBy,
    bool Mine)
{
    public McpActivityData(ActivityData activity, bool mine)
        : this(
            activity.Id, activity.Kind, activity.Actor, activity.At, activity.Changes, activity.FromState, activity.ToState,
            activity.Comment, activity.ReplyToId, activity.ImportedFrom, activity.BlueprintVersion, activity.EditedAt,
            activity.DeletedAt, activity.DeletedBy, mine)
    {
    }
}
