using System.Text.Json;
using PmTool.Cli.Api;

namespace PmTool.Cli.Agent;

/// <summary>
/// 把引擎輸出的一行事件轉成伺服器的進度。每件工作一個新的實例（裡面記著工具名稱、文字寫到哪裡）。
/// </summary>
internal interface IEngineTranslator
{
    TranslatorStep Feed(string line);

    /// <summary>引擎結束了，串流裡卻沒有給出結果（當掉、沒登入、被結束）時，由結束代碼與標準錯誤決定結果。</summary>
    JobOutcome Conclude(int exitCode, string standardError);
}

/// <param name="Events">這一行轉出來的進度。</param>
/// <param name="Outcome">不是 null 就是這件工作的結果，後面的行不用再看。</param>
/// <param name="Abort">要馬上結束引擎（它做了不該做的事），不等它自己結束。</param>
internal sealed record TranslatorStep(IReadOnlyList<AgentEvent> Events, JobOutcome? Outcome = null, bool Abort = false)
{
    public static readonly TranslatorStep None = new([]);

    public static TranslatorStep Emit(params AgentEvent[] events) => events.Length == 0 ? None : new TranslatorStep(events);

    public static TranslatorStep Stop(string error) => new([], JobOutcome.Failed(error), Abort: true);
}

/// <summary>兩個引擎共用的小工具。</summary>
internal static class TranslatorSupport
{
    public const string ProposeChangeSet = "propose_changeset";
    public const string ProposeItemChanges = "propose_item_changes";
    public const string ProposeNewItems = "propose_new_items";
    public const string ProposeMemberChanges = "propose_member_changes";
    public const string ProposeItemDeletions = "propose_item_deletions";
    public const string ProposeBulkItemChanges = "propose_bulk_item_changes";
    public const string ProposeProject = "propose_project";

    public static JsonDocument? TryParse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            // 不是 JSON 的行（引擎的雜訊）略過，也不記錄：內容可能是對話。
            return null;
        }
    }

    /// <summary>工具結果的文字：字串，或一串 <c>{type:"text", text}</c>。</summary>
    public static string TextOf(JsonElement? content)
    {
        if (content is not { } value)
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Concat(value.EnumerateArray().Select(part => part.Text("text") ?? string.Empty)),
            _ => string.Empty,
        };
    }

    /// <summary>propose_changeset 成功時，結果是一段 JSON，變更集的編號在 number。</summary>
    public static long? ChangeSetNumber(string text)
    {
        using var document = TryParse(text);
        if (document is null)
        {
            return null;
        }

        var root = document.RootElement;
        return root.Number("number") ?? root.Number("changeSetNumber") ?? root.Get("changeSet")?.Number("number");
    }

    /// <summary>propose_item_changes 成功時，結果的 JSON 帶 proposalId；不是 GUID 就不採用，免得把引擎亂寫的字串送給伺服器。</summary>
    public static string? ItemProposalId(string text) => ProposalIdOf(text);

    /// <summary>propose_project 成功時同樣帶 proposalId，同樣只認 GUID。</summary>
    public static string? ProjectProposalId(string text) => ProposalIdOf(text);

    /// <summary>propose_new_items 成功時同樣帶 proposalId，同樣只認 GUID。</summary>
    public static string? NewItemProposalId(string text) => ProposalIdOf(text);

    /// <summary>propose_member_changes 成功時同樣帶 proposalId，同樣只認 GUID。</summary>
    public static string? MemberProposalId(string text) => ProposalIdOf(text);

    /// <summary>propose_item_deletions 成功時同樣帶 proposalId，同樣只認 GUID。</summary>
    public static string? ItemDeletionProposalId(string text) => ProposalIdOf(text);

    /// <summary>propose_bulk_item_changes 成功時同樣帶 proposalId，同樣只認 GUID。</summary>
    public static string? ItemBatchProposalId(string text) => ProposalIdOf(text);

    private static string? ProposalIdOf(string text)
    {
        using var document = TryParse(text);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return Guid.TryParse(document.RootElement.Text("proposalId"), out var id) ? id.ToString("D") : null;
    }

    /// <summary>
    /// 工具失敗的原因：工具回的文字（沒有的話給一句通用的），拿掉權杖與路徑。
    /// Kumi 的 MCP 工具失敗時回 <c>{"error": "…"}</c>，只取裡面的訊息：整段 JSON 照原樣顯示在網頁上看不懂。
    /// </summary>
    public static string ToolError(string text) =>
        ErrorText.Sanitize(ErrorText.FirstParagraph(ErrorMessageOf(text))) is { Length: > 0 } clean ? clean : "工具回了錯誤。";

    private static string ErrorMessageOf(string text)
    {
        using var document = TryParse(text);
        return document is { RootElement.ValueKind: JsonValueKind.Object } && document.RootElement.Text("error") is { Length: > 0 } message
            ? message
            : text;
    }
}
