using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace PmTool.Ai.Assistant.Tools;

/// <summary>
/// 建議新增、修改、刪除週期與里程碑：只建立建議，使用者在網頁按「套用」才會生效。檢查在 <see cref="IPlanningProposalCreator"/>，
/// MCP 的同名工具用同一份。
/// </summary>
internal sealed class ProposePlanningChangesTool(AssistantToolContext context) : AssistantTool(
    AssistantToolNames.ProposePlanningChanges,
    $"提出週期與里程碑的變動建議（不會真的改）：使用者在網頁上看到後按「套用」才會生效。changes 是 1 到 {AiRules.MaxPlanningChangesPerProposal} 筆，依順序套用，每一筆："
    + "target（cycle 週期或 milestone 里程碑）、action（create 新增、update 修改、delete 刪除）；update、delete 要給 id（get_planning 或 get_project 回的）。"
    + "週期的欄位是 name、startDate、endDate（yyyy-MM-dd，頭尾都算，最長 365 天）；里程碑的欄位是 name、dueDate（yyyy-MM-dd）、description（可以不給；修改時給空字串是清掉）。"
    + "create 要給齊必要的欄位；update 只給要改的欄位，沒給的照現在的值，至少要有一個欄位和現在不同；delete 不要帶其他欄位，刪掉之後裡面的項目變成沒有週期或里程碑（項目不會被刪）。"
    + "同一個專案的週期日期不能重疊、名稱不能重複（不分大小寫），里程碑名稱也不能重複，做不到的排法不要當成選項給使用者。一份建議裡同一個週期或里程碑只能出現一次。要使用者有修改項目的權限、而且看得到專案的全部項目才能提，不行時錯誤會寫出誰能做，照實轉告。"
    + "提出之後告訴使用者「請按套用」，不要說已經改好了。",
    $$$$"""
    {"type":"object","properties":{"changes":{"type":"array","description":"週期與里程碑的變動，最多 {{{{AiRules.MaxPlanningChangesPerProposal}}}} 筆，依順序套用","items":{"type":"object","properties":{"target":{"type":"string","enum":["cycle","milestone"]},"action":{"type":"string","enum":["create","update","delete"]},"id":{"type":"string","description":"修改、刪除的對象的 id"},"name":{"type":"string"},"startDate":{"type":"string","description":"週期的開始日，yyyy-MM-dd"},"endDate":{"type":"string","description":"週期的結束日，yyyy-MM-dd"},"dueDate":{"type":"string","description":"里程碑的到期日，yyyy-MM-dd"},"description":{"type":"string","description":"里程碑的說明；修改時給空字串是清掉"}},"required":["target","action"],"additionalProperties":false}},"reason":{"type":"string","description":"為什麼這樣排（一行，最長 200 字），給使用者在卡片上看"}},"required":["changes"],"additionalProperties":false}
    """,
    context)
{
    public const string CreatedNote = "已經建立週期與里程碑的建議，使用者在網頁上按「套用」才會生效。不要說已經改好了。";

    private static readonly HashSet<string> KnownProperties = new(StringComparer.Ordinal)
    {
        "target", "action", "id", "name", "startDate", "endDate", "dueDate", "description",
    };

    protected override async Task<string> RunAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        if (ToolArguments.Array(arguments, "changes") is not { } elements)
        {
            return ToolResults.Error("參數 changes 要是週期與里程碑變動的陣列。");
        }

        if (elements.Count > AiRules.MaxPlanningChangesPerProposal)
        {
            return ToolResults.Error($"一份建議最多 {AiRules.MaxPlanningChangesPerProposal} 筆，changes 有 {elements.Count} 筆。");
        }

        var drafts = new List<PlanningChangeDraft>(elements.Count);
        for (var i = 0; i < elements.Count; i++)
        {
            var read = ReadChange(elements[i], $"changes[{i}]");
            if (read.Error is { } error)
            {
                return ToolResults.Error(error);
            }

            drafts.Add(read.Change!);
        }

        var draft = new PlanningProposalDraft(Context.RequiredProjectKey, drafts)
        {
            ConversationId = Context.ConversationId,
            Reason = ToolArguments.String(arguments, "reason"),
        };
        var created = await Context.PlanningProposals.CreateAsync(Context.Actor, draft, cancellationToken);
        if (created.Failure is { } failure)
        {
            return ToolResults.FromFailure(failure);
        }

        var proposal = created.Value;
        return ToolResults.Ok(new
        {
            notice = StaffingNotices.UserContent,
            proposalId = proposal.Id,
            projectKey = proposal.ProjectKey,
            changes = proposal.Changes.Select(c => new
            {
                target = Lower(c.Target.ToString()),
                action = Lower(c.Action.ToString()),
                id = c.Id,
                before = c.Before,
                after = c.After,
                itemCount = c.ItemCount,
            }),
            note = CreatedNote,
        });
    }

    private static (PlanningChangeDraft? Change, string? Error) ReadChange(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return (null, $"{path} 要是物件。");
        }

        if (element.EnumerateObject().Select(p => p.Name).FirstOrDefault(name => !KnownProperties.Contains(name)) is { } unknown)
        {
            return (null, $"{path} 不認得的屬性：{unknown}。");
        }

        var target = Enum(element, "target", ["cycle", "milestone"]);
        var action = Enum(element, "action", ["create", "update", "delete"]);
        if (target is null || action is null)
        {
            return (null, $"{path} 要有 target（cycle 或 milestone）與 action（create、update 或 delete）。");
        }

        foreach (var name in KnownProperties.Where(n => n is not ("target" or "action")))
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                return (null, $"{path}.{name} 要是字串。");
            }
        }

        var id = Text(element, "id");
        if (id is not null && !Guid.TryParse(id, out _))
        {
            return (null, $"{path}.id 要是週期或里程碑的 id（get_planning 回的）。");
        }

        var dates = new Dictionary<string, DateOnly?>(StringComparer.Ordinal);
        foreach (var name in new[] { "startDate", "endDate", "dueDate" })
        {
            var parsed = ReadDate(element, name);
            if (parsed.Invalid)
            {
                return (null, $"{path}.{name} 要寫成 yyyy-MM-dd。");
            }

            dates[name] = parsed.Date;
        }

        var change = new PlanningChangeDraft(
            target == "cycle" ? PlanningTarget.Cycle : PlanningTarget.Milestone,
            action switch { "create" => PlanningAction.Create, "update" => PlanningAction.Update, _ => PlanningAction.Delete },
            id is null ? null : Guid.Parse(id))
        {
            Name = Text(element, "name"),
            StartDate = dates["startDate"],
            EndDate = dates["endDate"],
            DueDate = dates["dueDate"],
            Description = Text(element, "description"),
        };
        return (change, null);
    }

    private static (DateOnly? Date, bool Invalid) ReadDate(JsonElement element, string name)
    {
        if (Text(element, name) is not { } text)
        {
            return (null, false);
        }

        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? (date, false)
            : (null, true);
    }

    private static string? Enum(JsonElement element, string name, string[] allowed) =>
        Text(element, name) is { } text && allowed.Contains(text) ? text : null;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Lower(string name) => name.ToLowerInvariant();
}
