using System.Text.Json.Serialization;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.Assistant;

/// <summary>資料列與合約型別之間的轉換，以及 <c>parts</c> 欄位的 JSON（第 5.4 節）。</summary>
internal static class AssistantMessageMapper
{
    public const string UserRoleName = "user";

    public const string AssistantRoleName = "assistant";

    /// <param name="attachments">使用者的訊息帶的附件，依送出的順序；助理的回覆不用給。</param>
    public static AssistantMessage ToMessage(AiMessageRow row, IReadOnlyList<AssistantAttachment>? attachments = null)
    {
        var parts = ReadParts(row.Parts);
        if (row.Role == UserRoleName)
        {
            return new AssistantMessage(row.Id, AssistantRole.User, parts, row.CreatedAt)
            {
                ItemKey = row.ItemKey,
                Attachments = attachments ?? [],
            };
        }

        return new AssistantMessage(row.Id, AssistantRole.Assistant, parts, row.CreatedAt)
        {
            Status = row.Status is null ? null : ParseStatus(row.Status),
            Error = row.Error,
            Selection = row is { ConnectionId: { } connectionId, Model: { } model }
                ? new AiSelection(connectionId, model, AiEnumNames.ParseOptionalEffort(row.Effort))
                : null,
            ConnectionName = row.ConnectionName,
            Usage = row.InputTokens is null && row.OutputTokens is null
                ? null
                : new AssistantUsage(row.InputTokens ?? 0, row.OutputTokens ?? 0),
        };
    }

    public static string StatusName(AssistantMessageStatus status) => status switch
    {
        AssistantMessageStatus.Complete => "complete",
        AssistantMessageStatus.Stopped => "stopped",
        AssistantMessageStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "不認得的回覆狀態。"),
    };

    public static AssistantMessageStatus ParseStatus(string name) => name switch
    {
        "complete" => AssistantMessageStatus.Complete,
        "stopped" => AssistantMessageStatus.Stopped,
        "failed" => AssistantMessageStatus.Failed,
        _ => throw new FormatException($"不認得的回覆狀態：{name}"),
    };

    public static string WriteParts(IReadOnlyList<AssistantPart> parts) =>
        AssistantJson.Serialize(parts.Select(ToDto).ToList());

    public static IReadOnlyList<AssistantPart> ReadParts(string json) =>
        [.. (AssistantJson.Deserialize<List<PartDto>>(json) ?? []).Select(FromDto)];

    private static PartDto ToDto(AssistantPart part) => part switch
    {
        AssistantTextPart text => new TextPartDto(text.Text),
        AssistantToolPart tool => new ToolPartDto(tool.CallId, tool.Name, tool.Status, tool.ChangeSetNumber, tool.Error, tool.ItemProposalId, tool.ProjectProposalId, tool.NewItemProposalId, tool.MemberProposalId, tool.ItemDeletionProposalId, tool.ItemBatchProposalId),
        _ => throw new ArgumentOutOfRangeException(nameof(part), part, "不認得的訊息段落。"),
    };

    private static AssistantPart FromDto(PartDto dto) => dto switch
    {
        TextPartDto text => new AssistantTextPart(text.Text),
        ToolPartDto tool => new AssistantToolPart(tool.CallId, tool.Name, tool.Status, tool.ChangeSetNumber, tool.Error)
        {
            ItemProposalId = tool.ItemProposalId,
            ProjectProposalId = tool.ProjectProposalId,
            NewItemProposalId = tool.NewItemProposalId,
            MemberProposalId = tool.MemberProposalId,
            ItemDeletionProposalId = tool.ItemDeletionProposalId,
            ItemBatchProposalId = tool.ItemBatchProposalId,
        },
        _ => throw new FormatException("不認得的訊息段落。"),
    };

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(TextPartDto), "text")]
    [JsonDerivedType(typeof(ToolPartDto), "tool")]
    private abstract record PartDto;

    private sealed record TextPartDto(string Text) : PartDto;

    private sealed record ToolPartDto(
        string CallId, string Name, AssistantToolStatus Status, long? ChangeSetNumber, string? Error, Guid? ItemProposalId = null, Guid? ProjectProposalId = null, Guid? NewItemProposalId = null, Guid? MemberProposalId = null,
        Guid? ItemDeletionProposalId = null, Guid? ItemBatchProposalId = null) : PartDto;
}
