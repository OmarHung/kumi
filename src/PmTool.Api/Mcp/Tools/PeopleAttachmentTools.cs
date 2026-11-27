using System.Text.Json;
using PmTool.Ai;
using PmTool.Identity;

namespace PmTool.Api.Mcp.Tools;

/// <summary>
/// 給本機與主機的引擎（<c>agent:run</c>）的兩個動作建議工具：人員管理、附件。都只建立建議、不動資料，
/// 使用者在網頁按「套用」才以自己的身分寫入；檢查與建立在 AI 模組的處理器，內建助理的同名工具用同一份。
/// 規格見 <c>internal/HANDOFF-ai-coverage-2.md</c> 4.2。
/// </summary>
internal static class PeopleAttachmentTools
{
    private const string PeopleNote = "已經建立人員建議，使用者（組織管理員）在網頁上按「套用」才會生效；新增人員要在卡片上輸入初始密碼。不要說已經改好了。";

    private const string AttachmentNote = "已經建立附件建議，使用者在網頁上按「套用」才會生效。不要說已經附好或刪掉了。";

    public static IReadOnlyList<McpTool> All { get; } =
    [
        new("propose_people_changes",
            "對組織的人員提出新增、改組織角色、改職稱與負責範圍、停用、啟用的建議（不會真的改）：使用者（要是組織管理員）在網頁上看到後按「套用」才會以他自己的身分寫入。" +
            $"changes 是 1 到 {AiRules.MaxActionSteps} 筆，依順序套用。每筆的 action 是：create {{displayName, email, role, jobTitle?, responsibilities?}}（新增人員，職稱與負責範圍可以一起給，不用等帳號建好再提一次）、set_role {{person, role}}（改組織角色）、" +
            "set_profile {person, jobTitle?, responsibilities?}（空字串是清掉，不改就不要給）、disable {person}（停用帳號）、enable {person}（重新啟用）。role 是 owner、admin、member、guest；person 用 list_people 回的人員編號。" +
            "重要：新增人員的初始密碼絕對不要問使用者、也不要放進參數（放了會被擋下），卡片上會請管理員自己輸入。" +
            "只有組織管理員能提；新增、停用擁有者或管理員、改到或改自擁有者與管理員的角色只有擁有者能做；不能改自己的角色、不能停用自己。提出之後告訴使用者「請按套用」，不要說已經改好了。",
            McpSchemas.Object(
                required: ["changes"],
                ("changes", McpSchemas.ArrayOf(
                    "要做的人員變更，依套用的順序。",
                    McpSchemas.ObjectOf(
                        required: ["action"],
                        ("action", McpSchemas.Enum("新增、改角色、改職稱與負責範圍、停用或啟用。", "create", "set_role", "set_profile", "disable", "enable")),
                        ("displayName", McpSchemas.Text("create 才給：顯示名稱。")),
                        ("email", McpSchemas.Text("create 才給：Email（登入用）。")),
                        ("role", McpSchemas.Enum("create、set_role 才給：組織角色。", "owner", "admin", "member", "guest")),
                        ("person", McpSchemas.Text("set_role、set_profile、disable、enable 才給：人員編號（list_people 回的）。")),
                        ("jobTitle", McpSchemas.Text("create、set_profile：職稱，一行字，最多 60 字；空字串是清掉，不改就不要給。")),
                        ("responsibilities", McpSchemas.Text("create、set_profile：負責範圍，一行字，最多 200 字；空字串是清掉，不改就不要給。"))))),
                ("reason", McpSchemas.Text("為什麼這樣做，一行（最長 200 字），給卡片顯示。"))),
            AccessTokenScope.AgentRun, ReadOnly: false, ProposePeopleAsync),
        new("propose_attachment_changes",
            "建議把使用者在這則對話裡上傳的檔案附到項目或知識庫頁面，或刪除項目上的附件（不會真的做）：使用者在網頁上看到後按「套用」才會以他自己的身分寫入。" +
            $"changes 是 1 到 {AiRules.MaxActionSteps} 筆，依順序套用。每筆的 action 是：attach {{attachmentId, itemKey}}（附到項目）、attach {{attachmentId, spaceKey, pageNumber}}（附到知識庫頁面）、delete {{attachmentId, itemKey}}（刪除項目的附件）。" +
            "attach 的 attachmentId 是使用者在這則對話裡上傳的檔案（訊息裡寫的「附件 N：檔名」，直接用檔名就好；同名不只一個時錯誤訊息會列出編號）；別則對話的、別人的檔案一律找不到。" +
            "delete 的 attachmentId 是項目附件的編號（get_item 的附件清單）或檔名；刪除不能復原，提出前要先讓使用者知道。要有改項目的權限（附到頁面要能編輯）；專案封存時不能改。" +
            "一份建議裡的項目要在同一個專案。提出之後告訴使用者「請按套用」，不要說已經附好或刪掉了。",
            McpSchemas.Object(
                required: ["changes"],
                ("changes", McpSchemas.ArrayOf(
                    "要做的附件變更，依套用的順序。",
                    McpSchemas.ObjectOf(
                        required: ["action", "attachmentId"],
                        ("action", McpSchemas.Enum("附上檔案或刪除項目的附件。", "attach", "delete")),
                        ("attachmentId", McpSchemas.Text("attach：這則對話裡使用者上傳的檔案（檔名或編號）；delete：項目附件的編號或檔名。")),
                        ("itemKey", McpSchemas.Text("項目編號，例如 WEB-12（附到項目、刪除項目附件時給）。")),
                        ("spaceKey", McpSchemas.Text("附到知識庫頁面時給：空間代號。")),
                        ("pageNumber", McpSchemas.Integer("附到知識庫頁面時給：頁面編號。"))))),
                ("reason", McpSchemas.Text("為什麼這樣做，一行（最長 200 字），給卡片顯示。"))),
            AccessTokenScope.AgentRun, ReadOnly: false, ProposeAttachmentsAsync),
    ];

    private static Task<McpToolResult> ProposePeopleAsync(McpToolContext context, McpArguments arguments, CancellationToken cancellationToken) =>
        CreateAsync(context, arguments, ActionKinds.People, projectKey: null, PeopleNote, cancellationToken);

    private static Task<McpToolResult> ProposeAttachmentsAsync(McpToolContext context, McpArguments arguments, CancellationToken cancellationToken)
    {
        var changes = ReadChanges(arguments);
        return CreateAsync(context, arguments, ActionKinds.Attachments, ProjectKeyOf(context, changes), AttachmentNote, cancellationToken);
    }

    private static async Task<McpToolResult> CreateAsync(
        McpToolContext context, McpArguments arguments, string kind, string? projectKey, string note, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToElement(new { changes = ReadChanges(arguments) });
        var draft = new ActionProposalDraft(kind, projectKey, payload)
        {
            Reason = arguments.OptionalPlain("reason"),
            ConversationId = context.ConversationId,
        };
        var created = await context.Services.ActionProposals.CreateAsync(context.Actor, draft, cancellationToken);
        return created.Failure is { } failure
            ? McpToolResult.FromFailure(failure)
            : McpToolResult.Ok(new
            {
                proposalId = created.Value.Id,
                kind = created.Value.Kind,
                projectKey = created.Value.ProjectKey,
                title = created.Value.Title,
                count = created.Value.Steps.Count,
                needsInput = created.Value.Inputs.Count > 0,
                note,
            });
    }

    private static IReadOnlyList<JsonElement> ReadChanges(McpArguments arguments)
    {
        var elements = arguments.OptionalArray("changes") ?? throw new McpArgumentException("缺少參數 changes，它要是一個 JSON 陣列。");
        if (elements.Count > AiRules.MaxActionSteps)
        {
            throw new McpArgumentException($"一份建議最多 {AiRules.MaxActionSteps} 筆，changes 有 {elements.Count} 筆。");
        }

        return elements;
    }

    /// <summary>
    /// 附到項目的建議屬於那個項目的專案（一份只能有一個專案）；只碰知識庫頁面的不屬於專案。
    /// 項目編號先過範圍檢查（<see cref="McpToolContext.Item"/>）：限定專案的權杖碰不到別的專案。
    /// </summary>
    private static string? ProjectKeyOf(McpToolContext context, IReadOnlyList<JsonElement> changes)
    {
        var projects = new List<string>();
        for (var i = 0; i < changes.Count; i++)
        {
            if (changes[i].ValueKind != JsonValueKind.Object
                || !changes[i].TryGetProperty("itemKey", out var value)
                || value.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(value.GetString()))
            {
                continue;
            }

            var key = value.GetString()!.Trim();
            if (key.Any(char.IsControl) || key.LastIndexOf('-') is not (var dash and > 0))
            {
                throw new McpArgumentException($"changes[{i}].itemKey 要是項目編號，例如 WEB-12。");
            }

            projects.Add(context.Item(key)[..dash].ToUpperInvariant());
        }

        var distinct = projects.Distinct(StringComparer.Ordinal).ToList();
        return distinct.Count > 1
            ? throw new McpArgumentException($"一份附件建議的項目要在同一個專案（現在有 {string.Join("、", distinct)}），請分成幾份提。")
            : distinct.FirstOrDefault();
    }
}
