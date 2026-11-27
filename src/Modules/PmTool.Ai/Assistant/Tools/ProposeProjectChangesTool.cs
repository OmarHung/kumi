using System.Text.Json;
using Microsoft.Extensions.AI;
using PmTool.Abstractions;

namespace PmTool.Ai.Assistant.Tools;

/// <summary>
/// 建議改專案（名稱、封存或取消封存、專案說明）：只建立建議，使用者在網頁按「套用」才會生效。
/// 檢查在 <see cref="IProjectChangeProposalCreator"/>，MCP 的同名工具用同一份。
/// </summary>
internal sealed class ProposeProjectChangesTool(AssistantToolContext context) : AssistantTool(
    AssistantToolNames.ProposeProjectChanges,
    "提出改專案的建議（不會真的改）：使用者在網頁上看到後按「套用」才會生效。可以改 name（專案名稱，代號不能改）、archived（true 封存、false 取消封存）、"
    + "description（專案說明，也就是資訊頁，Markdown；整段取代，所以要先用 get_project_info 讀現在的內容，在上面改；要清掉說明給空字串，不改就不要給，null 也是不改）。至少給一個，而且要和現在不同。"
    + "專案說明裡不能放密碼、私鑰、權杖這類機密，擋下來時照實告訴使用者。專案說明在你讀過之後被別人改過的話，套用時整份不做。"
    + "需要專案管理的權限，沒有時錯誤會寫出誰能做，照實轉告。提出之後告訴使用者「請按套用」，不要說已經改好了。",
    """
    {"type":"object","properties":{"name":{"type":"string","description":"新的專案名稱"},"archived":{"type":"boolean","description":"true 封存、false 取消封存"},"description":{"type":"string","description":"新的專案說明（Markdown，整段取代）；要清掉說明給空字串；不改就不要給（null 也是不改）"},"reason":{"type":"string","description":"為什麼這樣改（一行，最長 200 字），給使用者在卡片上看"}},"additionalProperties":false}
    """,
    context)
{
    public const string CreatedNote = "已經建立改專案的建議，使用者在網頁上按「套用」才會生效。不要說已經改好了。";

    private static readonly HashSet<string> KnownProperties = new(StringComparer.Ordinal) { "name", "archived", "description", "reason" };

    protected override async Task<string> RunAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        if (arguments.Keys.FirstOrDefault(key => !KnownProperties.Contains(key)) is { } unknown)
        {
            return ToolResults.Error($"不認得的參數：{unknown}（可以給 name、archived、description、reason）。");
        }

        var read = Read(arguments);
        if (read.Error is { } error)
        {
            return ToolResults.Error(error);
        }

        var draft = new ProjectChangeProposalDraft(Context.RequiredProjectKey, read.Name, read.Archived, read.Description)
        {
            ConversationId = Context.ConversationId,
            Reason = read.Reason,
        };
        var created = await Context.ProjectChangeProposals.CreateAsync(Context.Actor, draft, cancellationToken);
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
            name = proposal.Name,
            archived = proposal.Archived,
            changesDescription = proposal.ChangesDescription,
            clearsDescription = proposal.ChangesDescription && proposal.Description is null,
            note = CreatedNote,
        });
    }

    private static (string? Name, bool? Archived, Optional<string?> Description, string? Reason, string? Error) Read(AIFunctionArguments arguments)
    {
        var none = default(Optional<string?>);
        if (arguments.TryGetValue("name", out var rawName) && Kind(rawName) is not (JsonValueKind.String or JsonValueKind.Null))
        {
            return (null, null, none, null, "參數 name 要是字串。");
        }

        if (arguments.TryGetValue("archived", out var rawArchived) && Kind(rawArchived) is not (JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null))
        {
            return (null, null, none, null, "參數 archived 要是 true 或 false。");
        }

        if (arguments.TryGetValue("reason", out var rawReason) && Kind(rawReason) is not (JsonValueKind.String or JsonValueKind.Null))
        {
            return (null, null, none, null, "參數 reason 要是字串。");
        }

        var description = none;
        if (arguments.TryGetValue("description", out var rawDescription))
        {
            if (Kind(rawDescription) is not (JsonValueKind.String or JsonValueKind.Null))
            {
                return (null, null, none, null, "參數 description 要是字串（空字串是清掉說明，不改就不要給）。");
            }

            // 模型常把沒用到的參數寫成 null：當作沒給，不能讓它清掉說明；清掉要明確給空字串。
            if (Kind(rawDescription) == JsonValueKind.String)
            {
                description = new Optional<string?>(ToolArguments.String(arguments, "description"));
            }
        }

        return (ToolArguments.String(arguments, "name"), ToolArguments.Bool(arguments, "archived"), description, ToolArguments.String(arguments, "reason"), null);
    }

    // 參數可能是 JsonElement，也可能已經是 .NET 的值（null、字串、布林）。
    private static JsonValueKind Kind(object? value) => value switch
    {
        null => JsonValueKind.Null,
        string => JsonValueKind.String,
        bool flag => flag ? JsonValueKind.True : JsonValueKind.False,
        JsonElement element => element.ValueKind,
        _ => JsonValueKind.Undefined,
    };
}
