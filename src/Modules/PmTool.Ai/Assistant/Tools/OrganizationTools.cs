using Microsoft.Extensions.AI;

namespace PmTool.Ai.Assistant.Tools;

/// <summary>不選專案的組織對話用的工具：看得到的專案清單、提出新專案建議。</summary>
internal sealed class ListProjectsTool(AssistantToolContext context) : AssistantTool(
    AssistantToolNames.ListProjects,
    "列出使用者看得到的專案：代號、名稱、是否封存。提出新專案之前先看，避免用到已經有的代號（封存的專案也佔著代號）；使用者問「我有哪些專案」時也用它。",
    NoParametersSchema,
    context)
{
    protected override async Task<string> RunAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var projects = await Context.Projects.ListAsync(Context.Actor, cancellationToken);
        return ToolResults.Ok(new
        {
            projects = projects.Select(project => new
            {
                key = project.Key,
                name = project.Name,
                archived = project.ArchivedAt is not null,
            }),
        });
    }
}

/// <summary>
/// 提出新專案建議：只建立建議，使用者在網頁按「建立專案」才會建立。檢查與建立的邏輯在 <see cref="IProjectProposalCreator"/>，
/// MCP 的同名工具用同一份。
/// </summary>
internal sealed class ProposeProjectTool(AssistantToolContext context) : AssistantTool(
    AssistantToolNames.ProposeProject,
    "提出一份新專案建議（不會真的建立）：使用者在網頁上看到後按「建立專案」才會建立，而且只有組織管理員建得成。"
    + "key 是專案代號（大寫英文字母開頭、只能有大寫字母與數字、2 到 10 個字元），name 是專案名稱，template 是 list_templates 回的模板 key，"
    + "includeExamples 是要不要一起建立模板的範例項目（預設 false，使用者要範例才設 true），description 是專案說明（Markdown，可省略）。"
    + "提出之前先用 list_projects 確認代號沒有人用。提出之後告訴使用者「請按建立專案」，不要說已經建好了。一則回覆最多提出一份。",
    """
    {"type":"object","properties":{"key":{"type":"string","description":"專案代號，例如 WEB"},"name":{"type":"string","description":"專案名稱"},"template":{"type":"string","description":"模板的 key，例如 simple_tasks"},"includeExamples":{"type":"boolean","description":"是否一起建立範例項目，預設 false"},"description":{"type":"string","description":"專案說明（Markdown），可省略"}},"required":["key","name","template"],"additionalProperties":false}
    """,
    context)
{
    public const string CreatedNote = "已經建立新專案建議，使用者在網頁上按「建立專案」才會建立。不要說已經建好了。";

    private static readonly string TooManyError =
        $"這一則回覆已經提出過新專案建議了，一則回覆最多只能提出 {AiRules.MaxProjectProposalsPerReply} 份。請先請使用者看過，再請他在下一則訊息裡要你繼續。";

    // 每則回覆建一組工具，所以這個計數就是「這則回覆」的份數；模型可能同時呼叫好幾次：先用 CompareExchange 佔位，沒成功就放回去。
    private int _proposed;

    protected override async Task<string> RunAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        if (!TryReserveSlot())
        {
            return ToolResults.Error(TooManyError);
        }

        var proposed = false;
        try
        {
            var (text, ok) = await ProposeAsync(arguments, cancellationToken);
            proposed = ok;
            return text;
        }
        finally
        {
            if (!proposed)
            {
                Interlocked.Decrement(ref _proposed);
            }
        }
    }

    private bool TryReserveSlot()
    {
        while (true)
        {
            var current = Volatile.Read(ref _proposed);
            if (current >= AiRules.MaxProjectProposalsPerReply)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _proposed, current + 1, current) == current)
            {
                return true;
            }
        }
    }

    private async Task<(string Text, bool Proposed)> ProposeAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var key = ToolArguments.String(arguments, "key");
        var name = ToolArguments.String(arguments, "name");
        var template = ToolArguments.String(arguments, "template");
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(template))
        {
            return (ToolResults.Error("缺少參數：key、name、template 都要有。"), false);
        }

        var draft = new ProjectProposalDraft(key, name, template, ToolArguments.Bool(arguments, "includeExamples") ?? false, ToolArguments.String(arguments, "description"))
        {
            ConversationId = Context.ConversationId,
        };
        var created = await Context.ProjectProposals.CreateAsync(Context.Actor, draft, cancellationToken);
        if (created.Failure is { } failure)
        {
            return (ToolResults.FromFailure(failure), false);
        }

        var proposal = created.Value;
        return (ToolResults.Ok(new
        {
            proposalId = proposal.Id,
            key = proposal.Key,
            name = proposal.Name,
            template = proposal.Template,
            includeExamples = proposal.IncludeExamples,
            note = CreatedNote,
        }), true);
    }
}
