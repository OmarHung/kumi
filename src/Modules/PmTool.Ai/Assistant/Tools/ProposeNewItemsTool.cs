using System.Text.Json;
using Microsoft.Extensions.AI;
using PmTool.Ai.NewItems;

namespace PmTool.Ai.Assistant.Tools;

/// <summary>
/// 建議新增一到幾個項目：只建立建議，使用者在網頁按「全部建立」才會建立。檢查與建立的邏輯在 <see cref="INewItemProposalCreator"/>，
/// MCP 的同名工具用同一份。
/// </summary>
internal sealed class ProposeNewItemsTool(AssistantToolContext context) : AssistantTool(
    AssistantToolNames.ProposeNewItems,
    $"提出新增項目的建議（不會真的建立）：使用者在網頁上看到後按「全部建立」才會建立。items 是要建立的項目（1 到 {AiRules.MaxNewItemsPerProposal} 個，依順序建立）："
    + "每一項有 type（藍圖裡的項目類型 key）、title，可以有 description（Markdown）、assignee（me 是發問的人、none 是不指派、或人員 id：專案成員，或放進 addMembers 的人）、"
    + "priority（none、low、medium、high、urgent）、startDate、dueDate（yyyy-MM-dd）、estimate（數字）、fields（自訂欄位 {key: 值}）、cycleId、milestoneId（get_project 回的週期、里程碑的 id）。"
    + "負責人還不是專案成員、而使用者有 project.manage 時，用 addMembers（[{person: 人員 id, role?: 角色 key，沒給是 contributor}]）在建立項目之前先把他加進專案；已經是成員的會略過、不改他的角色。"
    + "要掛在既有項目底下用 parentKey（例如 WEB-12）；要掛在同一份建議裡前面的項目底下，前面那一項給 ref（小寫英文、數字、-、_），這一項用 parentRef 指它，兩個只能給一個。"
    + "狀態一律從流程的第一個狀態開始，不能指定。建議之前先用 get_blueprint 看類型與哪些類型能掛在哪些底下。提出之後告訴使用者「請按全部建立」，不要說已經建好了。",
    $$$$"""
    {"type":"object","properties":{"items":{"type":"array","description":"要建立的項目，最多 {{{{AiRules.MaxNewItemsPerProposal}}}} 個，依建立的順序","items":{"type":"object","properties":{"ref":{"type":"string","description":"這一項的代稱，讓後面的項目用 parentRef 掛在它底下"},"type":{"type":"string","description":"項目類型的 key"},"title":{"type":"string"},"description":{"type":"string","description":"說明（Markdown）"},"assignee":{"type":"string","description":"me、none，或人員 id（專案成員，或放進 addMembers 的人）"},"priority":{"type":"string","enum":["none","low","medium","high","urgent"]},"startDate":{"type":"string","description":"yyyy-MM-dd"},"dueDate":{"type":"string","description":"yyyy-MM-dd"},"estimate":{"type":"number"},"parentKey":{"type":"string","description":"既有項目的編號，例如 WEB-12"},"parentRef":{"type":"string","description":"同一份建議裡前面某一項的 ref"},"cycleId":{"type":"string","description":"週期的 id（get_project 的 cycles）"},"milestoneId":{"type":"string","description":"里程碑的 id（get_project 的 milestones）"},"fields":{"description":"自訂欄位 {key: 值}"}},"required":["type","title"],"additionalProperties":false}},"addMembers":{"type":"array","description":"建立項目之前先加進專案的人，最多 {{{{AiRules.MaxMemberChangesPerProposal}}}} 個","items":{"type":"object","properties":{"person":{"type":"string","description":"人員 id"},"role":{"type":"string","description":"角色 key，沒給是 contributor"}},"required":["person"],"additionalProperties":false}}},"required":["items"],"additionalProperties":false}
    """,
    context)
{
    public const string CreatedNote = "已經建立新增項目的建議，使用者在網頁上按「全部建立」才會建立。不要說已經建好了。";

    protected override async Task<string> RunAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        if (ToolArguments.Array(arguments, "items") is not { } elements)
        {
            return ToolResults.Error("參數 items 要是項目的陣列。");
        }

        // 先擋數量：超過上限的請求不必逐項讀取，也不必為了錯誤訊息去查成員。
        if (elements.Count > AiRules.MaxNewItemsPerProposal)
        {
            return ToolResults.Error($"一份建議最多 {AiRules.MaxNewItemsPerProposal} 個項目，items 有 {elements.Count} 個。");
        }

        var parsed = await ReadItemsAsync(elements, cancellationToken);
        if (parsed.Error is { } error)
        {
            return ToolResults.Error(error);
        }

        var additions = MemberArguments.ReadAdditions(arguments);
        if (additions.Error is { } additionsError)
        {
            return ToolResults.Error(additionsError);
        }

        var draft = new NewItemProposalDraft(Context.RequiredProjectKey, parsed.Items!)
        {
            ConversationId = Context.ConversationId,
            AddMembers = additions.Value!,
        };
        var created = await Context.NewItemProposals.CreateAsync(Context.Actor, draft, cancellationToken);
        if (created.Failure is { } failure)
        {
            return ToolResults.FromFailure(failure);
        }

        var proposal = created.Value;
        return ToolResults.Ok(new
        {
            proposalId = proposal.Id,
            projectKey = proposal.ProjectKey,
            items = proposal.Items.Select(item => new { @ref = item.Ref, type = item.Type, title = item.Title, parentKey = item.ParentKey, parentRef = item.ParentRef }),
            note = CreatedNote,
        });
    }

    private async Task<(IReadOnlyList<NewItemDraft>? Items, string? Error)> ReadItemsAsync(
        IReadOnlyList<JsonElement> elements, CancellationToken cancellationToken)
    {
        var drafts = new List<NewItemDraft>(elements.Count);
        for (var i = 0; i < elements.Count; i++)
        {
            var read = await ReadItemAsync(elements[i], i, cancellationToken);
            if (read.Error is { } error)
            {
                return (null, error);
            }

            drafts.Add(read.Item!);
        }

        return (drafts, null);
    }

    private async Task<(NewItemDraft? Item, string? Error)> ReadItemAsync(JsonElement element, int index, CancellationToken cancellationToken)
    {
        var path = $"items[{index}]";
        if (element.ValueKind != JsonValueKind.Object)
        {
            return (null, $"{path} 要是物件。");
        }

        if (element.EnumerateObject().Select(p => p.Name).FirstOrDefault(name => !KnownProperties.Contains(name)) is { } unknown)
        {
            return (null, $"{path} 不認得的屬性：{unknown}（狀態一律從流程的第一個狀態開始，不能指定）。");
        }

        if (Text(element, "type") is not { } type || Text(element, "title") is not { } title)
        {
            return (null, $"{path} 要有字串的 type 與 title。");
        }

        var assignee = await ReadAssigneeAsync(element, path, cancellationToken);
        if (assignee.Error is { } assigneeError)
        {
            return (null, assigneeError);
        }

        if (OptionalProblem(element, path) is { } problem)
        {
            return (null, problem);
        }

        var item = new NewItemDraft(type, title)
        {
            Ref = Text(element, "ref"),
            Description = Text(element, "description"),
            AssigneeId = assignee.Id,
            Priority = Text(element, "priority"),
            StartDate = Text(element, "startDate"),
            DueDate = Text(element, "dueDate"),
            Estimate = element.TryGetProperty("estimate", out var estimate) && estimate.ValueKind == JsonValueKind.Number && estimate.TryGetDecimal(out var number) ? number : null,
            ParentKey = Text(element, "parentKey"),
            ParentRef = Text(element, "parentRef"),
            CycleId = MemberArguments.OptionalGuid(element, "cycleId"),
            MilestoneId = MemberArguments.OptionalGuid(element, "milestoneId"),
            Fields = FieldsOf(element),
        };
        return (item, null);
    }

    private static readonly HashSet<string> KnownProperties = new(StringComparer.Ordinal)
    {
        "ref", "type", "title", "description", "assignee", "priority", "startDate", "dueDate", "estimate", "parentKey", "parentRef", "cycleId", "milestoneId", "fields",
    };

    // 型別不對的選填欄位照實告訴模型，不默默丟掉（默默丟掉會建出少了欄位的項目）。
    private static string? OptionalProblem(JsonElement element, string path)
    {
        foreach (var name in new[] { "ref", "description", "priority", "startDate", "dueDate", "parentKey", "parentRef", "cycleId", "milestoneId" })
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                return $"{path}.{name} 要是字串。";
            }
        }

        foreach (var name in new[] { "cycleId", "milestoneId" })
        {
            if (Text(element, name) is { } text && !Guid.TryParse(text, out _))
            {
                return $"{path}.{name} 要是 get_project 回的 id。";
            }
        }

        if (element.TryGetProperty("estimate", out var estimate) && estimate.ValueKind is not (JsonValueKind.Number or JsonValueKind.Null))
        {
            return $"{path}.estimate 要是數字。";
        }

        return element.TryGetProperty("fields", out var fields) && fields.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null)
            ? $"{path}.fields 要是 {{key: 值}} 的物件。"
            : null;
    }

    private async Task<(Guid? Id, string? Error)> ReadAssigneeAsync(JsonElement element, string path, CancellationToken cancellationToken)
    {
        if (element.TryGetProperty("assignee", out var raw) && raw.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
        {
            return (null, $"{path}.assignee 要是字串（me、none 或人員 id）。");
        }

        var text = Text(element, "assignee")?.Trim();
        if (string.IsNullOrEmpty(text) || text.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return (null, null);
        }

        if (text.Equals("me", StringComparison.OrdinalIgnoreCase))
        {
            return (Context.Actor.PersonId, null);
        }

        if (Guid.TryParse(text, out var id))
        {
            return (id, null);
        }

        // 專案是空的、模型手上沒有成員名單：列出來讓它照著改。
        var project = await Context.Projects.GetAsync(Context.Actor, Context.RequiredProjectKey, cancellationToken);
        var members = project.Failure is null
            ? NewItemMembers.Describe(await NewItemMembers.ActiveAsync(Context.People, Context.Actor.OrganizationId, project.Value.Members, cancellationToken))
            : string.Empty;
        return (null, $"{path}.assignee 要是 me、none 或人員 id（專案成員，或放進 addMembers 的人），不認得「{text}」。{members}");
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static Dictionary<string, JsonElement> FieldsOf(JsonElement element) =>
        element.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Object
            ? fields.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal)
            : [];
}
