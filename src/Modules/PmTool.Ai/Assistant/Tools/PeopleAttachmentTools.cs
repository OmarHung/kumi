using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace PmTool.Ai.Assistant.Tools;

/// <summary>
/// 建議新增人員、改組織角色、改職稱與負責範圍、停用與啟用帳號（不會真的改）：只建立建議，使用者在網頁按「套用」才會寫入。
/// 初始密碼不經過模型：卡片上會請管理員自己輸入。檢查與建立在 <c>PeopleActionHandler</c>，MCP 的同名工具用同一份。
/// </summary>
internal sealed class ProposePeopleChangesTool(AssistantToolContext context) : AssistantTool(
    AssistantToolNames.ProposePeopleChanges,
    $"對組織的人員提出新增、改組織角色、改職稱與負責範圍、停用、啟用的建議（不會真的改）：使用者（要是組織管理員）在網頁上看到後按「套用」才會寫入。"
    + $"changes 是 1 到 {AiRules.MaxActionSteps} 筆，依順序套用。每筆的 action 是：create {{displayName, email, role, jobTitle?, responsibilities?}}（新增人員，職稱與負責範圍可以一起給，不用等帳號建好再提一次）、set_role {{person, role}}（改組織角色）、"
    + "set_profile {person, jobTitle?, responsibilities?}（改既有的人的職稱、負責範圍，空字串是清掉，不改就不要給）、disable {person}（停用帳號）、enable {person}（重新啟用）。"
    + "role 是 owner（擁有者）、admin（管理員）、member（一般成員）、guest（訪客）；person 用 list_people 回的人員編號。"
    + "重要：新增人員的初始密碼絕對不要問使用者、也不要放進參數（放了會被擋下）——卡片上會請管理員自己輸入。"
    + "只有組織管理員能提；新增、停用擁有者或管理員、改到或改自擁有者與管理員的角色，只有擁有者能做；不能改自己的角色、不能停用自己。"
    + "停用後那個人所有的登入立刻失效，但他的工作項目與留言還在。權限不夠或內容不合規定時會被擋下，照實轉告。提出之後告訴使用者「請按套用」（新增人員時還要在卡片上輸入密碼），不要說已經改好了。",
    $$$"""
    {"type":"object","properties":{"changes":{"type":"array","description":"要做的人員變更，依套用的順序","items":{"type":"object","properties":{"action":{"type":"string","enum":["create","set_role","set_profile","disable","enable"],"description":"create 新增人員、set_role 改組織角色、set_profile 改職稱與負責範圍、disable 停用、enable 啟用"},"displayName":{"type":"string","description":"create 才給：顯示名稱"},"email":{"type":"string","description":"create 才給：Email（登入用）"},"role":{"type":"string","enum":["owner","admin","member","guest"],"description":"create、set_role 才給：組織角色"},"person":{"type":"string","description":"set_role、set_profile、disable、enable 才給：人員編號（list_people 回的）"},"jobTitle":{"type":"string","description":"create、set_profile：職稱，一行字，最多 60 字；空字串是清掉，不改就不要給"},"responsibilities":{"type":"string","description":"create、set_profile：負責範圍，一行字，最多 200 字；空字串是清掉，不改就不要給"}},"required":["action"],"additionalProperties":false}},"reason":{"type":"string","description":"為什麼這樣做，一行（最長 200 字），給卡片顯示"}},"required":["changes"],"additionalProperties":false}
    """,
    context)
{
    public const string CreatedNote = "已經建立人員建議，使用者（組織管理員）在網頁上按「套用」才會生效；新增人員要在卡片上輸入初始密碼。不要說已經改好了。";

    protected override Task<string> RunAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) =>
        PeopleAttachmentToolRunner.RunAsync(Context, arguments, ActionKinds.People, projectKey: null, CreatedNote, cancellationToken);
}

/// <summary>
/// 建議把使用者在這則對話上傳的檔案附到項目或知識庫頁面，或刪除項目的附件（不會真的做）。
/// 檢查與建立在 <c>AttachmentActionHandler</c>，MCP 的同名工具用同一份。
/// </summary>
internal sealed class ProposeAttachmentChangesTool(AssistantToolContext context) : AssistantTool(
    AssistantToolNames.ProposeAttachmentChanges,
    $"建議把使用者在這則對話裡上傳的檔案附到項目或知識庫頁面，或刪除項目上的附件（不會真的做）：使用者在網頁上看到後按「套用」才會寫入。changes 是 1 到 {AiRules.MaxActionSteps} 筆，依順序套用。每筆的 action 是："
    + "attach {attachmentId, itemKey}（附到項目）、attach {attachmentId, spaceKey, pageNumber}（附到知識庫頁面）、delete {attachmentId, itemKey}（刪除項目的附件）。"
    + "attach 的 attachmentId 是使用者在這則對話裡上傳的檔案：訊息裡會寫「附件 N：檔名」，直接用檔名當 attachmentId 就好（同名不只一個時錯誤訊息會列出編號）；別則對話的、別人的檔案一律找不到。"
    + "delete 的 attachmentId 是項目附件的編號（get_item 的附件清單）或檔名；刪除不能復原（檔案會從儲存空間移除），提出前要先讓使用者知道。"
    + "要有改項目的權限（附到頁面要能編輯那一頁）；專案封存時不能改。附件類型與大小的限制和使用者自己上傳時相同。權限不夠或內容不合規定時會被擋下，照實轉告。"
    + "提出之後告訴使用者「請按套用」，不要說已經附好或刪掉了。",
    $$$"""
    {"type":"object","properties":{"changes":{"type":"array","description":"要做的附件變更，依套用的順序","items":{"type":"object","properties":{"action":{"type":"string","enum":["attach","delete"],"description":"attach 把使用者上傳的檔案附上去、delete 刪除項目的附件"},"attachmentId":{"type":"string","description":"attach：這則對話裡使用者上傳的檔案（檔名或編號）；delete：項目附件的編號或檔名"},"itemKey":{"type":"string","description":"項目編號，例如 WEB-12（附到項目、刪除項目附件時給）"},"spaceKey":{"type":"string","description":"附到知識庫頁面時給：空間代號"},"pageNumber":{"type":"integer","description":"附到知識庫頁面時給：頁面編號"}},"required":["action","attachmentId"],"additionalProperties":false}},"reason":{"type":"string","description":"為什麼這樣做，一行（最長 200 字），給卡片顯示"}},"required":["changes"],"additionalProperties":false}
    """,
    context)
{
    public const string CreatedNote = "已經建立附件建議，使用者在網頁上按「套用」才會生效。不要說已經附好或刪掉了。";

    protected override async Task<string> RunAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var projectKey = ProjectKeyOf(arguments, out var error);
        if (error is not null)
        {
            return ToolResults.Error(error);
        }

        // 專案對話只能碰自己的專案：別的專案的項目連「找不到附件」的訊息（會列檔名）都不能讓它看到。
        if (projectKey is not null && Context.ProjectKey is { } own && !string.Equals(own, projectKey, StringComparison.OrdinalIgnoreCase))
        {
            return ToolResults.Error($"這則對話只能處理專案 {own} 的項目。");
        }

        // 組織對話讀了某個專案的項目（delete 的訊息會列附件檔名）要記下來，之後使用者看不到全部項目時整則對話才會被刪。
        if (projectKey is not null && Context.ProjectKey is null)
        {
            await Context.Services.GetRequiredService<IAssistantConversationReads>()
                .RememberAsync(Context.Actor, Context.ConversationId, [projectKey], cancellationToken);
        }

        return await PeopleAttachmentToolRunner.RunAsync(Context, arguments, ActionKinds.Attachments, projectKey, CreatedNote, cancellationToken);
    }

    /// <summary>
    /// 建議屬於哪個專案：附到項目的是那個項目的專案（組織對話沒有專案，專案對話裡也可能提別的專案的項目，以項目為準）；
    /// 只碰知識庫頁面的不屬於專案。一份建議只能有一個專案。
    /// </summary>
    private static string? ProjectKeyOf(AIFunctionArguments arguments, out string? error)
    {
        error = null;
        var keys = (ToolArguments.Array(arguments, "changes") ?? [])
            .Where(change => change.ValueKind == JsonValueKind.Object)
            .Select(change => change.TryGetProperty("itemKey", out var key) && key.ValueKind == JsonValueKind.String ? key.GetString() : null)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key!.Trim())
            .Select(key => key.LastIndexOf('-') is var dash and > 0 ? key[..dash] : null)
            .ToList();
        if (keys.Any(key => key is null))
        {
            error = "itemKey 要是項目編號，例如 WEB-12。";
            return null;
        }

        var distinct = keys.Select(key => key!.ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Count > 1)
        {
            error = $"一份附件建議的項目要在同一個專案（現在有 {string.Join("、", distinct)}），請分成幾份提。";
            return null;
        }

        return distinct.Count == 1 ? distinct[0] : null;
    }
}

/// <summary>兩個動作建議工具共用：讀參數、呼叫 <see cref="IActionProposalCreator"/>、組結果。</summary>
internal static class PeopleAttachmentToolRunner
{
    public static async Task<string> RunAsync(
        AssistantToolContext context,
        AIFunctionArguments arguments,
        string kind,
        string? projectKey,
        string note,
        CancellationToken cancellationToken)
    {
        if (arguments.Keys.FirstOrDefault(k => k is not ("changes" or "reason")) is { } extra)
        {
            return ToolResults.Error($"不認得的參數：{extra}。只有 changes 與 reason。");
        }

        if (ToolArguments.Array(arguments, "changes") is not { } changes)
        {
            return ToolResults.Error("參數 changes 要是陣列。");
        }

        var payload = JsonSerializer.SerializeToElement(new { changes });
        var draft = new ActionProposalDraft(kind, projectKey, payload)
        {
            ConversationId = context.ConversationId,
            Reason = ToolArguments.String(arguments, "reason"),
        };
        var created = await context.Services.GetRequiredService<IActionProposalCreator>().CreateAsync(context.Actor, draft, cancellationToken);
        if (created.Failure is { } failure)
        {
            return ToolResults.FromFailure(failure);
        }

        var proposal = created.Value;
        return ToolResults.Ok(new
        {
            proposalId = proposal.Id,
            kind = proposal.Kind,
            projectKey = proposal.ProjectKey,
            title = proposal.Title,
            count = proposal.Steps.Count,
            needsInput = proposal.Inputs.Count > 0,
            note,
        });
    }
}
