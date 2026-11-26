using PmTool.Abstractions;
using PmTool.Api.Endpoints;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Api.Mcp.Tools;

/// <summary>改項目的工具（<c>items:write</c>）。權限仍然是授權的人在專案裡的角色：範圍只會讓能做的事更少，不會更多。</summary>
internal static class ItemWriteTools
{
    private const string FieldsDescription =
        "自訂欄位：{欄位的 key: 值}。值的形狀依欄位型別：文字與網址是字串，數字是數字，日期是 YYYY-MM-DD，單選是選項的 key，" +
        "多選是選項 key 的陣列，成員是人員編號，勾選是布林。欄位的 key 與型別看 get_blueprint。";

    public static IEnumerable<McpTool> All { get; } =
    [
        new("create_item",
            "在專案裡建立一個項目。狀態一律從流程的第一個狀態開始。類型的 key 與自訂欄位看 get_blueprint。建立前先跟使用者確認內容。",
            McpSchemas.Object(
                required: ["projectKey", "type", "title"],
                ("projectKey", McpSchemas.Text("專案代號，例如 WEB。")),
                ("type", McpSchemas.Text("項目類型的 key。")),
                ("title", McpSchemas.Text("標題。")),
                ("description", McpSchemas.Text("說明，Markdown。")),
                ("assignee", McpSchemas.Text("負責人：me、none、專案成員的 Email 或人員編號（get_item 的 members）。沒給是沒有負責人。")),
                ("priority", McpSchemas.Enum<Priority>("優先順序。沒給是 none。")),
                ("startDate", McpSchemas.Text("開始日，YYYY-MM-DD。")),
                ("dueDate", McpSchemas.Text("到期日，YYYY-MM-DD。")),
                ("parentKey", McpSchemas.Text("上層項目的編號，例如 WEB-3。")),
                ("fields", McpSchemas.FreeObject(FieldsDescription))),
            AccessTokenScope.ItemsWrite, ReadOnly: false, CreateItemAsync),
        new("update_item",
            "修改一個項目，只改有給的欄位；給 null 是清掉那個欄位（標題除外）。不能用它改狀態（用 move_item）。" +
            "要改說明時，把 get_item 回的 descriptionRevision 放進 expectedDescriptionRevision：別人在這中間改過說明就會被擋下，不會蓋掉別人的修改。",
            McpSchemas.Object(
                required: ["itemKey"],
                ("itemKey", McpSchemas.Text("項目編號，例如 WEB-12。")),
                ("title", McpSchemas.Text("新標題。")),
                ("description", McpSchemas.Text("新說明，Markdown。null 或空字串是清掉。")),
                ("expectedDescriptionRevision", McpSchemas.Text("改說明時帶 get_item 回的 descriptionRevision。")),
                ("assignee", McpSchemas.Text("負責人：me、none、專案成員的 Email 或人員編號（get_item 的 members）。")),
                ("priority", McpSchemas.Enum<Priority>("優先順序；none 是清掉。")),
                ("startDate", McpSchemas.Text("開始日，YYYY-MM-DD；null 是清掉。")),
                ("dueDate", McpSchemas.Text("到期日，YYYY-MM-DD；null 是清掉。")),
                ("parentKey", McpSchemas.Text("上層項目的編號；null 是清掉。")),
                ("fields", McpSchemas.FreeObject(FieldsDescription + "值給 null 是清掉那個欄位。"))),
            AccessTokenScope.ItemsWrite, ReadOnly: false, UpdateItemAsync),
        new("move_item",
            "改項目的狀態。流程有沒有這條轉換、你的角色能不能做、必填的欄位有沒有填，都由 Kumi 檢查，不通過時錯誤會說明原因。",
            McpSchemas.Object(
                required: ["itemKey", "state"],
                ("itemKey", McpSchemas.Text("項目編號，例如 WEB-12。")),
                ("state", McpSchemas.Text("要改成的狀態的 key（看 get_blueprint 的流程）。"))),
            AccessTokenScope.ItemsWrite, ReadOnly: false, MoveItemAsync),
        new("add_comment",
            "在項目底下留言（Markdown）。留言會記在授權的人名下，留之前先跟使用者確認內容。",
            McpSchemas.Object(
                required: ["itemKey", "body"],
                ("itemKey", McpSchemas.Text("項目編號，例如 WEB-12。")),
                ("body", McpSchemas.Text("留言的內容。"))),
            AccessTokenScope.ItemsWrite, ReadOnly: false, AddCommentAsync),
    ];

    private static async Task<McpToolResult> CreateItemAsync(McpToolContext context, McpArguments arguments, CancellationToken cancellationToken)
    {
        var projectKey = context.Project(arguments.RequiredPlain("projectKey"));
        var assigneeText = arguments.OptionalPlain("assignee");
        var item = new NewWorkItem
        {
            Type = arguments.RequiredPlain("type"),
            Title = arguments.RequiredString("title"),
            Description = arguments.OptionalString("description"),
            AssigneeId = assigneeText is null ? null : await McpAssignees.ResolveAsync(context, projectKey, "assignee", assigneeText, cancellationToken),
            Priority = arguments.OptionalEnum<Priority>("priority") ?? Priority.None,
            StartDate = arguments.OptionalDate("startDate"),
            DueDate = arguments.OptionalDate("dueDate"),
            ParentKey = arguments.OptionalPlain("parentKey"),
            Fields = arguments.OptionalObject("fields"),
        };
        var result = await context.Services.Items.CreateAsync(context.Actor, projectKey, item, cancellationToken);
        return await ItemResultAsync(context, result, cancellationToken);
    }

    private static async Task<McpToolResult> UpdateItemAsync(McpToolContext context, McpArguments arguments, CancellationToken cancellationToken)
    {
        var itemKey = context.Item(arguments.RequiredPlain("itemKey"));
        var patch = await ReadPatchAsync(context, itemKey, arguments, cancellationToken);
        var result = await context.Services.Items.UpdateAsync(context.Actor, itemKey, patch, cancellationToken);
        return await ItemResultAsync(context, result, cancellationToken);
    }

    private static async Task<WorkItemPatch> ReadPatchAsync(
        McpToolContext context, string itemKey, McpArguments arguments, CancellationToken cancellationToken)
    {
        var patch = new WorkItemPatch
        {
            Title = arguments.Has("title") ? new Optional<string>(arguments.RequiredString("title")) : default,
            Description = arguments.Has("description") ? new Optional<string?>(arguments.OptionalString("description")) : default,
            ExpectedDescriptionRevision = arguments.OptionalPlain("expectedDescriptionRevision"),
            Priority = arguments.Has("priority") ? new Optional<Priority>(arguments.OptionalEnum<Priority>("priority") ?? Priority.None) : default,
            StartDate = arguments.Has("startDate") ? new Optional<DateOnly?>(arguments.OptionalDate("startDate")) : default,
            DueDate = arguments.Has("dueDate") ? new Optional<DateOnly?>(arguments.OptionalDate("dueDate")) : default,
            ParentKey = arguments.Has("parentKey")
                ? new Optional<string?>(arguments.OptionalPlain("parentKey") is { Length: > 0 } parent ? parent : null)
                : default,
            Fields = arguments.OptionalObject("fields"),
        };

        if (arguments.Has("assignee"))
        {
            var text = arguments.OptionalPlain("assignee");
            var id = text is null ? null : await McpAssignees.ResolveAsync(context, McpAssignees.ProjectKeyOf(itemKey), "assignee", text, cancellationToken);
            patch = patch with { AssigneeId = new Optional<Guid?>(id) };
        }

        return HasChanges(patch) ? patch : throw new McpArgumentException("沒有要修改的欄位：至少要給一個要改的欄位。");
    }

    private static bool HasChanges(WorkItemPatch patch) =>
        patch.Title.HasValue || patch.Description.HasValue || patch.AssigneeId.HasValue || patch.Priority.HasValue
        || patch.StartDate.HasValue || patch.DueDate.HasValue || patch.ParentKey.HasValue || patch.Fields.Count > 0;

    private static async Task<McpToolResult> MoveItemAsync(McpToolContext context, McpArguments arguments, CancellationToken cancellationToken)
    {
        var result = await context.Services.Items.TransitionAsync(
            context.Actor, context.Item(arguments.RequiredPlain("itemKey")), arguments.RequiredPlain("state"), cancellationToken);
        return await ItemResultAsync(context, result, cancellationToken);
    }

    private static async Task<McpToolResult> AddCommentAsync(McpToolContext context, McpArguments arguments, CancellationToken cancellationToken)
    {
        var result = await context.Services.Activity.AddCommentAsync(
            context.Actor, context.Item(arguments.RequiredPlain("itemKey")), arguments.RequiredString("body"), replyTo: null, cancellationToken);
        if (result.Failure is { } failure)
        {
            return McpToolResult.FromFailure(failure);
        }

        var comment = await ActivityData.FromManyAsync(
            [result.Value], context.Services.People, context.Actor.OrganizationId, cancellationToken);
        return McpToolResult.Ok(new { comment = comment[0] });
    }

    private static async Task<McpToolResult> ItemResultAsync(
        McpToolContext context, Result<WorkItem> result, CancellationToken cancellationToken) =>
        result.Failure is { } failure
            ? McpToolResult.FromFailure(failure)
            : McpToolResult.Ok(new { item = await McpItemShapes.DetailAsync(context, result.Value, cancellationToken) });
}
