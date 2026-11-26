using PmTool.Ai;
using PmTool.Identity;

namespace PmTool.Api.Mcp.Tools;

/// <summary>
/// 給本機與主機的引擎（<c>agent:run</c>）的工具：在不選專案的組織對話裡提出新專案建議。只建立建議、不建立專案；
/// 使用者在網頁按「建立專案」才以自己的身分與權限建立。一般的 MCP 用戶端沒有這個範圍，看不到也叫不到它。
/// 檢查與建立的邏輯在 AI 模組（<see cref="IProjectProposalCreator"/>），內建助理的同名工具用同一份。
/// </summary>
internal static class ProjectProposalTools
{
    private const string CreatedNote = "已經建立新專案建議，使用者在網頁上按「建立專案」才會建立。不要說專案已經建好了。";

    public static IEnumerable<McpTool> All { get; } =
    [
        new("propose_project",
            "提出一個新專案的建議（不會真的建立）：使用者在 Kumi 網頁上看到後按「建立專案」才會建立。只能在不選專案的組織對話裡用，一則回覆最多提出 " +
            $"{AiRules.MaxProjectProposalsPerReply} 份。提出前先用 list_projects 避開重複的代號、用 list_templates 選模板，並問清楚使用者要的代號與名稱；" +
            "使用者不是組織管理員時不要提出（只有組織管理員能建立專案）。提出之後告訴使用者「請按建立專案」，不要說專案已經建好了。",
            McpSchemas.Object(
                required: ["key", "name", "template"],
                ("key", McpSchemas.Text("新專案的代號：英文字母開頭、2 到 10 個英文字母或數字，例如 WEB。")),
                ("name", McpSchemas.Text("新專案的名稱。")),
                ("template", McpSchemas.Text("模板的 key，用 list_templates 查，例如 simple_tasks。")),
                ("includeExamples", McpSchemas.Boolean("要不要附模板的範例項目，預設 false。")),
                ("description", McpSchemas.Text("一起寫進專案的說明（Markdown），可以省略。"))),
            AccessTokenScope.AgentRun, ReadOnly: false, ProposeAsync),
    ];

    private static async Task<McpToolResult> ProposeAsync(McpToolContext context, McpArguments arguments, CancellationToken cancellationToken)
    {
        // 對話編號由伺服器從工作找到；沒有對話或對話屬於專案（不該走到這裡：工具清單已經擋掉）一律拒絕。
        if (context.Scope.Kind != McpScopeKind.OrganizationOnly)
        {
            throw new McpArgumentException("propose_project 只能在不選專案的組織對話裡用。");
        }

        if (context.ConversationId is not { } conversationId)
        {
            throw new McpArgumentException("找不到這件工作的對話。");
        }

        var draft = new ProjectProposalDraft(
            arguments.RequiredPlain("key"),
            arguments.RequiredPlain("name"),
            arguments.RequiredPlain("template"),
            arguments.OptionalBool("includeExamples") ?? false,
            arguments.OptionalString("description"))
        {
            ConversationId = conversationId,
        };
        var created = await context.Services.ProjectProposals.CreateAsync(context.Actor, draft, cancellationToken);
        if (created.Failure is { } failure)
        {
            return McpToolResult.FromFailure(failure);
        }

        var proposal = created.Value;
        return McpToolResult.Ok(new
        {
            proposalId = proposal.Id,
            key = proposal.Key,
            name = proposal.Name,
            template = proposal.Template,
            templateName = proposal.TemplateName,
            includeExamples = proposal.IncludeExamples,
            note = CreatedNote,
            notice = BlueprintReadTools.ContentNotice,
        });
    }
}
