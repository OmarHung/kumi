using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Identity;

namespace PmTool.Api.Mcp.Tools;

/// <summary>
/// 給本機與主機的引擎（<c>agent:run</c>）的四個專案寫入建議：週期與里程碑、留言的編輯刪除復原、復原已刪除的項目、改專案（名稱、封存、說明）。
/// 都只建立建議、不動資料，使用者在網頁按「套用」才以自己的身分寫入；檢查與建立在 AI 模組，內建助理的同名工具用同一份。
/// 規格見 <c>internal/HANDOFF-ai-coverage.md</c> 5.3～5.6。
/// </summary>
internal static class ProjectWriteProposalTools
{
    private const string PlanningNote = "已經建立週期與里程碑的建議，使用者在網頁上按「套用」才會寫入。不要說已經改好了。";

    private const string CommentNote = "已經建立留言的建議，使用者在網頁上按「套用」才會寫入。不要說已經改好了。";

    private const string RestoreNote = "已經建立復原建議，使用者在網頁上按「復原」才會復原。不要說已經復原了。";

    private const string ProjectNote = "已經建立改專案的建議，使用者在網頁上按「套用」才會寫入。不要說已經改好了。";

    private static readonly HashSet<string> PlanningProperties = new(StringComparer.Ordinal)
    {
        "target", "action", "id", "name", "startDate", "endDate", "dueDate", "description",
    };

    private static readonly HashSet<string> CommentProperties = new(StringComparer.Ordinal) { "itemKey", "commentId", "action", "body" };

    public static IEnumerable<McpTool> All { get; } =
    [
        new("propose_planning_changes",
            $"建議新增、修改、刪除週期與里程碑（不會真的改）：使用者在網頁上看到後按「套用」才會以他自己的身分寫入。一份 1 到 {AiRules.MaxPlanningChangesPerProposal} 筆，依順序套用。" +
            "每筆 {target: cycle 或 milestone, action: create、update 或 delete, id, name, startDate, endDate, dueDate, description}：週期要 name、startDate、endDate；里程碑要 name、dueDate，description 可以不給。同一個專案的週期日期不能重疊、名稱不能重複（不分大小寫），里程碑名稱也不能重複，做不到的排法不要當成選項給使用者。" +
            "update、delete 的 id 用 get_planning 回的；update 沒給的欄位維持原樣；delete 時裡面的項目會變成沒有週期或里程碑，要先告訴使用者。要有改項目的權限而且看得到全部項目才能提。" +
            "提出之後告訴使用者「請按套用」，不要說已經改好了。",
            McpSchemas.Object(
                required: ["projectKey", "changes"],
                ("projectKey", McpSchemas.Text("專案代號，例如 WEB。")),
                ("changes", McpSchemas.ArrayOf(
                    "要做的事，依套用的順序。",
                    McpSchemas.ObjectOf(
                        required: ["target", "action"],
                        ("target", McpSchemas.Enum("週期或里程碑。", "cycle", "milestone")),
                        ("action", McpSchemas.Enum("新增、修改或刪除。", "create", "update", "delete")),
                        ("id", McpSchemas.Text("update、delete 的對象（get_planning 回的 id）；create 不能給。")),
                        ("name", McpSchemas.Text("名稱。")),
                        ("startDate", McpSchemas.Text("週期的開始日，yyyy-MM-dd。")),
                        ("endDate", McpSchemas.Text("週期的結束日，yyyy-MM-dd。")),
                        ("dueDate", McpSchemas.Text("里程碑的到期日，yyyy-MM-dd。")),
                        ("description", McpSchemas.Text("里程碑的說明；修改時給空字串是清掉。"))))),
                ("reason", McpSchemas.Text("為什麼這樣排，一行（最長 200 字），給卡片顯示。"))),
            AccessTokenScope.AgentRun, ReadOnly: false, ProposePlanningAsync),
        new("propose_comment_changes",
            $"建議編輯、刪除或復原留言（不會真的改）：使用者在網頁上看到後按「套用」才會以他自己的身分寫入。一份 1 到 {AiRules.MaxCommentChangesPerProposal} 筆，可以跨同一個專案的幾個項目。" +
            "每筆 {itemKey, commentId, action: edit、delete 或 restore, body}：commentId 用 list_comments 或 get_item 回的 id；只有 edit 要給 body（新的完整內容，Markdown）。" +
            "只能編輯自己寫的、沒刪除的、不是匯入的留言；刪別人的留言要有管理專案的權限。新的留言與回覆不是用這個，用 propose_item_changes 的 comment 與 replyTo。提出之後告訴使用者「請按套用」，不要說已經改好了。",
            McpSchemas.Object(
                required: ["projectKey", "changes"],
                ("projectKey", McpSchemas.Text("專案代號，例如 WEB。")),
                ("changes", McpSchemas.ArrayOf(
                    "要做的事。",
                    McpSchemas.ObjectOf(
                        required: ["itemKey", "commentId", "action"],
                        ("itemKey", McpSchemas.Text("留言所在的項目編號，例如 WEB-12。")),
                        ("commentId", McpSchemas.Text("留言的 id。")),
                        ("action", McpSchemas.Enum("編輯、刪除或復原。", "edit", "delete", "restore")),
                        ("body", McpSchemas.Text("edit 才給：新的內容（Markdown）。")))))),
            AccessTokenScope.AgentRun, ReadOnly: false, ProposeCommentsAsync),
        new("propose_item_restores",
            $"建議復原已刪除的項目（不會真的復原）：使用者在網頁上看到後按「復原」才會以他自己的身分復原。一份 1 到 {AiRules.MaxItemsPerDeletionProposal} 個、不重複，項目編號用 list_deleted_items 回的。" +
            "要復原的項目上層也被刪了、又不在這份裡時會被擋下，錯誤訊息會列出上層：問使用者要不要一起復原，不要自己決定。提出之後告訴使用者「請按復原」，不要說已經復原了。",
            McpSchemas.Object(
                required: ["projectKey", "itemKeys"],
                ("projectKey", McpSchemas.Text("專案代號，例如 WEB。")),
                ("itemKeys", McpSchemas.ArrayOf("要復原的項目編號。", McpSchemas.Text("項目編號，例如 WEB-12。"))),
                ("reason", McpSchemas.Text("為什麼復原，一行（最長 200 字），給卡片顯示。"))),
            AccessTokenScope.AgentRun, ReadOnly: false, ProposeRestoresAsync),
        new("propose_project_changes",
            "建議修改專案本身（不會真的改）：名稱、封存或取消封存、專案說明（資訊頁，Markdown）。使用者在網頁上看到後按「套用」才會以他自己的身分寫入。" +
            "至少要給一個，而且和現在不同；要有管理專案的權限。description 是整份說明（先用 get_project_info 讀現在的，改完整份送回來）；要清掉說明給空字串，不改就不要給（null 也是不改）；說明超過 " +
            $"{AiRules.MaxEditableProjectDescriptionLength} 字的專案不能改說明。說明裡不能放密碼、金鑰。提出之後告訴使用者「請按套用」，不要說已經改好了。",
            McpSchemas.Object(
                required: ["projectKey"],
                ("projectKey", McpSchemas.Text("專案代號，例如 WEB。")),
                ("name", McpSchemas.Text("新的專案名稱。")),
                ("archived", McpSchemas.Boolean("true 是封存、false 是取消封存。")),
                ("description", McpSchemas.Text("新的專案說明（整份，Markdown）；空字串是清掉，不改就不要給。")),
                ("reason", McpSchemas.Text("為什麼這樣改，一行（最長 200 字），給卡片顯示。"))),
            AccessTokenScope.AgentRun, ReadOnly: false, ProposeProjectChangesAsync),
    ];

    private static async Task<McpToolResult> ProposePlanningAsync(McpToolContext context, McpArguments arguments, CancellationToken cancellationToken)
    {
        var projectKey = context.Project(arguments.RequiredPlain("projectKey"));
        var changes = ReadEntries(arguments, "changes", AiRules.MaxPlanningChangesPerProposal, PlanningProperties, ReadPlanningChange);
        var draft = new PlanningProposalDraft(projectKey, changes) { Reason = arguments.OptionalPlain("reason"), ConversationId = context.ConversationId };
        var created = await context.Services.PlanningProposals.CreateAsync(context.Actor, draft, cancellationToken);
        return created.Failure is { } failure
            ? McpToolResult.FromFailure(failure)
            : Created(created.Value.Id, created.Value.ProjectKey, PlanningNote, new { changes = created.Value.Changes.Count });
    }

    private static async Task<McpToolResult> ProposeCommentsAsync(McpToolContext context, McpArguments arguments, CancellationToken cancellationToken)
    {
        var projectKey = context.Project(arguments.RequiredPlain("projectKey"));
        var changes = ReadEntries(arguments, "changes", AiRules.MaxCommentChangesPerProposal, CommentProperties, entry => ReadCommentChange(context, entry));
        var draft = new CommentProposalDraft(projectKey, changes) { ConversationId = context.ConversationId };
        var created = await context.Services.CommentProposals.CreateAsync(context.Actor, draft, cancellationToken);
        return created.Failure is { } failure
            ? McpToolResult.FromFailure(failure)
            : Created(created.Value.Id, created.Value.ProjectKey, CommentNote, new { changes = created.Value.Changes.Count });
    }

    private static async Task<McpToolResult> ProposeRestoresAsync(McpToolContext context, McpArguments arguments, CancellationToken cancellationToken)
    {
        var projectKey = context.Project(arguments.RequiredPlain("projectKey"));
        var elements = arguments.OptionalArray("itemKeys") ?? throw new McpArgumentException("缺少參數 itemKeys，它要是一個 JSON 陣列。");
        if (elements.Count > AiRules.MaxItemsPerDeletionProposal)
        {
            throw new McpArgumentException($"一份建議最多 {AiRules.MaxItemsPerDeletionProposal} 個項目，itemKeys 有 {elements.Count} 個。");
        }

        var keys = new List<string>(elements.Count);
        foreach (var element in elements)
        {
            keys.Add(ReadItemKey(context, element, $"itemKeys[{keys.Count}]"));
        }

        var draft = new ItemRestoreProposalDraft(projectKey, keys) { Reason = arguments.OptionalPlain("reason"), ConversationId = context.ConversationId };
        var created = await context.Services.ItemRestoreProposals.CreateAsync(context.Actor, draft, cancellationToken);
        return created.Failure is { } failure
            ? McpToolResult.FromFailure(failure)
            : Created(created.Value.Id, created.Value.ProjectKey, RestoreNote, new { itemKeys = created.Value.Items.Select(item => item.ItemKey) });
    }

    private static async Task<McpToolResult> ProposeProjectChangesAsync(McpToolContext context, McpArguments arguments, CancellationToken cancellationToken)
    {
        var projectKey = context.Project(arguments.RequiredPlain("projectKey"));
        // 模型常把沒用到的參數寫成 null：沒給與 null 都是不改，要清掉說明得明確給空字串。
        var description = arguments.OptionalString("description") is { } text ? new Optional<string?>(text) : default;
        var draft = new ProjectChangeProposalDraft(projectKey, arguments.OptionalString("name"), arguments.OptionalBool("archived"), description)
        {
            Reason = arguments.OptionalPlain("reason"),
            ConversationId = context.ConversationId,
        };
        var created = await context.Services.ProjectChangeProposals.CreateAsync(context.Actor, draft, cancellationToken);
        return created.Failure is { } failure
            ? McpToolResult.FromFailure(failure)
            : Created(created.Value.Id, created.Value.ProjectKey, ProjectNote, new { name = created.Value.Name, archived = created.Value.Archived, changesDescription = created.Value.ChangesDescription });
    }

    private static McpToolResult Created(Guid proposalId, string projectKey, string note, object summary) =>
        McpToolResult.Ok(new { proposalId, projectKey, summary, note, notice = ItemReadTools.UserContentNotice });

    private static PlanningChangeDraft ReadPlanningChange(McpArguments entry)
    {
        var target = entry.RequiredPlain("target") switch
        {
            "cycle" => PlanningTarget.Cycle,
            "milestone" => PlanningTarget.Milestone,
            _ => throw new McpArgumentException("target 要是 cycle 或 milestone。"),
        };
        var action = entry.RequiredPlain("action") switch
        {
            "create" => PlanningAction.Create,
            "update" => PlanningAction.Update,
            "delete" => PlanningAction.Delete,
            _ => throw new McpArgumentException("action 要是 create、update 或 delete。"),
        };
        return new PlanningChangeDraft(target, action, OptionalGuid(entry, "id"))
        {
            Name = entry.OptionalString("name"),
            StartDate = entry.OptionalDate("startDate"),
            EndDate = entry.OptionalDate("endDate"),
            DueDate = entry.OptionalDate("dueDate"),
            Description = entry.OptionalString("description"),
        };
    }

    private static CommentChangeDraft ReadCommentChange(McpToolContext context, McpArguments entry)
    {
        var action = entry.RequiredPlain("action") switch
        {
            "edit" => CommentAction.Edit,
            "delete" => CommentAction.Delete,
            "restore" => CommentAction.Restore,
            _ => throw new McpArgumentException("action 要是 edit、delete 或 restore。"),
        };
        var commentId = OptionalGuid(entry, "commentId") ?? throw new McpArgumentException("缺少參數 commentId。");
        return new CommentChangeDraft(context.Item(entry.RequiredPlain("itemKey")), commentId, action, entry.OptionalString("body"));
    }

    internal static Guid? OptionalGuid(McpArguments arguments, string name) =>
        arguments.OptionalPlain(name) is { } text
            ? Guid.TryParse(text, out var id) ? id : throw new McpArgumentException($"{name} 要是 id（GUID）。")
            : null;

    /// <summary>陣列參數的讀法：先擋數量，再逐筆擋不認得的屬性並讀成草稿；錯誤訊息帶是第幾筆。</summary>
    private static List<T> ReadEntries<T>(
        McpArguments arguments, string name, int max, HashSet<string> allowed, Func<McpArguments, T> read)
    {
        var elements = arguments.OptionalArray(name) ?? throw new McpArgumentException($"缺少參數 {name}，它要是一個 JSON 陣列。");
        if (elements.Count > max)
        {
            throw new McpArgumentException($"一份建議最多 {max} 筆，{name} 有 {elements.Count} 筆。");
        }

        var entries = new List<T>(elements.Count);
        foreach (var element in elements)
        {
            var path = $"{name}[{entries.Count}]";
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new McpArgumentException($"{path} 要是一個物件。");
            }

            var values = element.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone());
            if (values.Keys.FirstOrDefault(key => !allowed.Contains(key)) is { } unknown)
            {
                throw new McpArgumentException($"{path} 不認得的屬性：{unknown}。");
            }

            try
            {
                entries.Add(read(new McpArguments(values)));
            }
            catch (McpArgumentException exception)
            {
                throw new McpArgumentException($"{path}：{exception.Message}");
            }
        }

        return entries;
    }

    private static string ReadItemKey(McpToolContext context, JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
        {
            throw new McpArgumentException($"{path} 要是項目編號（字串）。");
        }

        var key = element.GetString()!;
        return key.Any(char.IsControl)
            ? throw new McpArgumentException($"{path} 不能含有 NUL 或其他控制字元。")
            : context.Item(key);
    }
}
