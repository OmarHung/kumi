using System.Text.Json.Serialization;
using PmTool.Ai;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 要送的訊息：<c>text</c> 與這一則用哪個連線、模型、思考深度。<c>effort</c> 是 null 或省略時用模型自己的預設。
/// 另外定義而不直接收合約的型別，模組加屬性時對外的內文不會跟著變。
/// </summary>
internal sealed record AssistantMessageRequest(
    string Text, AssistantSelectionData Selection, string? ItemKey = null, IReadOnlyList<Guid>? AttachmentIds = null)
{
    /// <summary><c>attachmentIds</c> 省略或 null 等於沒有附件；數量、重複與歸屬由模組判斷。</summary>
    public AssistantMessageInput ToInput() => new(Text, Selection.ToSelection())
    {
        ItemKey = ItemKey,
        AttachmentIds = AttachmentIds ?? [],
    };
}

/// <summary>附件：<c>kind</c> 是 image、pdf 或 text（snake_case）。內容在下載的端點。</summary>
internal sealed record AssistantAttachmentData(
    Guid Id, string FileName, string ContentType, long SizeBytes, AssistantAttachmentKind Kind, DateTimeOffset UploadedAt)
{
    public static AssistantAttachmentData From(AssistantAttachment attachment) => new(
        attachment.Id, attachment.FileName, attachment.ContentType, attachment.SizeBytes, attachment.Kind, attachment.UploadedAt);
}

/// <param name="Effort">snake_case：none、low、medium、high、xhigh、max；null 是不指定。</param>
internal sealed record AssistantSelectionData(Guid ConnectionId, string Model, AiEffort? Effort = null)
{
    public AiSelection ToSelection() => new(ConnectionId, Model, Effort);

    public static AssistantSelectionData From(AiSelection selection) => new(selection.ConnectionId, selection.Model, selection.Effort);
}

internal sealed record AssistantUsageData(long InputTokens, long OutputTokens);

/// <summary>對話清單的一列。</summary>
internal sealed record AssistantConversationData(
    Guid Id, string? Title, int MessageCount, bool IsRunning, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static AssistantConversationData From(AssistantConversationSummary conversation) => new(
        conversation.Id, conversation.Title, conversation.MessageCount, conversation.IsRunning, conversation.CreatedAt, conversation.UpdatedAt);
}

internal sealed record AssistantConversationDetailData(AssistantConversationData Conversation, IReadOnlyList<AssistantMessageData> Messages)
{
    public static AssistantConversationDetailData From(AssistantConversation conversation) => new(
        AssistantConversationData.From(conversation.Conversation), [.. conversation.Messages.Select(AssistantMessageData.From)]);
}

/// <summary>
/// 一則訊息。<c>itemKey</c> 只有使用者的訊息可能有值（送出時選的項目）；<c>attachments</c> 使用者的訊息是送出時的附件，助理的回覆是空陣列（寫出 []，不省略）。使用者的訊息 <c>status</c>、<c>error</c>、<c>selection</c>、<c>connectionName</c>、<c>usage</c> 都是 null（寫出 null，不省略）。
/// </summary>
internal sealed record AssistantMessageData(
    Guid Id,
    AssistantRole Role,
    DateTimeOffset CreatedAt,
    IReadOnlyList<AssistantPartData> Parts,
    AssistantMessageStatus? Status,
    string? Error,
    AssistantSelectionData? Selection,
    string? ConnectionName,
    AssistantUsageData? Usage,
    string? ItemKey,
    IReadOnlyList<AssistantAttachmentData> Attachments)
{
    public static AssistantMessageData From(AssistantMessage message) => new(
        message.Id,
        message.Role,
        message.CreatedAt,
        [.. message.Parts.Select(AssistantPartData.From)],
        message.Status,
        message.Error,
        message.Selection is { } selection ? AssistantSelectionData.From(selection) : null,
        message.ConnectionName,
        message.Usage is { } usage ? new AssistantUsageData(usage.InputTokens, usage.OutputTokens) : null,
        message.ItemKey,
        [.. message.Attachments.Select(AssistantAttachmentData.From)]);
}

/// <summary>
/// 訊息的一段：<c>type</c> 是 text 或 tool。串流的 <c>tool</c> 事件也是這個形狀，所以要用基底型別序列化（才會帶 <c>type</c>）。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(AssistantTextPartData), "text")]
[JsonDerivedType(typeof(AssistantToolPartData), "tool")]
internal abstract record AssistantPartData
{
    // 不認得的種類是合約加了新的段落、這裡沒跟上，屬於伺服器的錯；略過的話畫面上就少了一段。
    public static AssistantPartData From(AssistantPart part) => part switch
    {
        AssistantTextPart text => new AssistantTextPartData(text.Text),
        AssistantToolPart tool => new AssistantToolPartData(tool.CallId, tool.Name, tool.Status, tool.ChangeSetNumber, tool.Error, tool.ItemProposalId, tool.ProjectProposalId, tool.NewItemProposalId),
        _ => throw new InvalidOperationException($"不認得的訊息段落：{part.GetType().Name}。"),
    };
}

internal sealed record AssistantTextPartData(string Text) : AssistantPartData;

internal sealed record AssistantToolPartData(
    string CallId, string Name, AssistantToolStatus Status, long? ChangeSetNumber, string? Error, Guid? ItemProposalId = null, Guid? ProjectProposalId = null, Guid? NewItemProposalId = null)
    : AssistantPartData;

/// <summary>串流的第一個事件：使用者的訊息已經存下，以及回覆存下來時的編號。</summary>
internal sealed record AssistantStartEventData(AssistantMessageData UserMessage, Guid AssistantMessageId);

internal sealed record AssistantTextEventData(string Delta);

/// <summary>串流的最後一個事件：存下來的回覆（完成、中斷或失敗）。</summary>
internal sealed record AssistantDoneEventData(AssistantMessageData Message);

/// <summary>主機來源才有：還在排隊。<c>position</c> 是前面還有幾件再加一（1 是下一個）；位置變了才再送。</summary>
internal sealed record AssistantQueuedEventData(int Position);

/// <summary>主機來源才有：輪到了，開始執行。沒有內容，寫成 <c>{}</c>。</summary>
internal sealed record AssistantStartedEventData;
