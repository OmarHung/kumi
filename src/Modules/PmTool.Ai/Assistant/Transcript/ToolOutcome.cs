using System.Text.Json;
using Microsoft.Extensions.AI;

namespace PmTool.Ai.Assistant.Transcript;

/// <summary>工具結果裡回覆的迴圈要看的部分：成功或預期中的失敗、給人看的原因、提出的變更集編號。</summary>
internal sealed record ToolOutcome(string RawResult, bool IsOk, string? Error, long? ChangeSetNumber)
{
    /// <summary>propose_item_changes 成功時結果裡的 proposalId。</summary>
    public Guid? ItemProposalId { get; init; }

    /// <summary>propose_project 成功時結果裡的 proposalId（和修改建議同一個欄位名，依工具的名稱分開用）。</summary>
    public Guid? ProjectProposalId { get; init; }

    /// <summary>propose_new_items 成功時結果裡的 proposalId。</summary>
    public Guid? NewItemProposalId { get; init; }

    private const string UnreadableError = "工具沒有回傳可以讀的結果。";

    /// <summary>
    /// 迴圈套件在工具丟例外或找不到工具時，回的是一段純文字而不是 JSON；一律當成失敗。
    /// 不信任結果裡的其他欄位：只讀 ok、error 與 changeSet.number。
    /// </summary>
    public static ToolOutcome From(FunctionResultContent content)
    {
        var raw = TextOf(content.Result);
        if (content.Exception is not null)
        {
            return new ToolOutcome(raw, false, "工具執行失敗。", null);
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("ok", out var ok) || ok.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return new ToolOutcome(raw, false, UnreadableError, null);
            }

            if (!ok.GetBoolean())
            {
                var error = root.TryGetProperty("error", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null;
                return new ToolOutcome(raw, false, string.IsNullOrWhiteSpace(error) ? "工具回報失敗。" : error, null);
            }

            return new ToolOutcome(raw, true, null, ChangeSetNumberOf(root)) { ItemProposalId = ProposalIdOf(root), ProjectProposalId = ProposalIdOf(root), NewItemProposalId = ProposalIdOf(root) };
        }
        catch (JsonException)
        {
            return new ToolOutcome(raw, false, "工具執行失敗。", null);
        }
    }

    /// <summary>沒有真的執行的工具呼叫（中斷、超過次數）補的結果。</summary>
    public static ToolOutcome FromError(string error) =>
        new(AssistantJson.Serialize(new { ok = false, error }), false, error, null);

    private static Guid? ProposalIdOf(JsonElement root) =>
        root.TryGetProperty("proposalId", out var id) && id.ValueKind == JsonValueKind.String && Guid.TryParse(id.GetString(), out var value)
            ? value
            : null;

    private static long? ChangeSetNumberOf(JsonElement root) =>
        root.TryGetProperty("changeSet", out var changeSet)
        && changeSet.ValueKind == JsonValueKind.Object
        && changeSet.TryGetProperty("number", out var number)
        && number.ValueKind == JsonValueKind.Number
        && number.TryGetInt64(out var value)
            ? value
            : null;

    private static string TextOf(object? result) => result switch
    {
        null => string.Empty,
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? string.Empty,
        JsonElement element => element.GetRawText(),
        _ => JsonSerializer.Serialize(result, AssistantJson.Options),
    };
}
