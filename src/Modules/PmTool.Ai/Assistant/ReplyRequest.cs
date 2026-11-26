using Microsoft.Extensions.AI;
using PmTool.Abstractions;
using PmTool.Ai.Attachments;
using PmTool.Ai.Providers;

namespace PmTool.Ai.Assistant;

/// <summary>開始一則回覆需要的一切，都是 <c>StartAsync</c> 在通過檢查、存下使用者的訊息之後備好的。</summary>
/// <param name="Permissions">使用者在這個專案的權限，只用來寫給模型的那一句說明。</param>
/// <param name="History">之前每一則的 transcript 還原出來的訊息。</param>
/// <param name="LeaseUntil">這則回覆拿到的租約。放租約時要對得上：租約過期後被別的回覆接手，不能把人家的租約放掉。</param>
/// <param name="AssistantSequence">這則回覆存下來時在對話裡的序號。</param>
internal sealed record ReplyRequest(
    Actor Actor,
    string ProjectKey,
    IReadOnlyList<string> Permissions,
    Guid ConversationId,
    Guid AssistantMessageId,
    int AssistantSequence,
    DateTimeOffset LeaseUntil,
    ResolvedSelection Selection,
    IReadOnlyList<ChatMessage> History,
    string UserText)
{
    /// <summary>本機的引擎連的 MCP 網址（<see cref="IMcpUrlProvider"/>）；用 API 金鑰的模型不用。</summary>
    public string? McpUrl { get; init; }

    /// <summary>主機來源：送出時預留的工作編號（<c>HostReservations</c>）；其他是 null。</summary>
    public Guid? HostJobId { get; init; }

    /// <summary>用組織的連線：送出時在 <c>connection_usage</c> 預留過，回覆結束要記帳（編號就是 <see cref="AssistantMessageId"/>）。</summary>
    public bool CountsUsage { get; init; }

    /// <summary>這則訊息選的項目編號；沒選是 null。</summary>
    public string? ItemKey { get; init; }

    /// <summary>這則訊息帶的附件（還只是參照，要送給模型前由 <c>AttachmentReplay</c> 讀檔）；沒有是空的。</summary>
    public IReadOnlyList<AttachmentReference> UserAttachments { get; init; } = [];

    /// <summary>
    /// 這則對話裡供應商拒絕過、之後不再重送的附件編號（<c>ai.attachments.replay_skipped_at</c> 有值的）；
    /// 重送歷史時直接用一行說明，不再讀檔、也不再送給供應商。
    /// </summary>
    public IReadOnlySet<Guid> SkippedAttachmentIds { get; init; } = new HashSet<Guid>();

    /// <summary>
    /// 本機與主機的引擎：這件工作要交給引擎下載的圖片與 PDF（<c>ResolveAttachmentsAsync</c> 決定的，依提示裡「〔附件 N：…〕」的編號順序）；
    /// 沒有是空的。
    /// </summary>
    public IReadOnlyList<LocalAgentJobAttachment> JobAttachments { get; init; } = [];

    /// <summary><see cref="JobAttachments"/> 裡屬於歷史（不是這一則）的那幾個；引擎失敗時記下不再重送。</summary>
    public IReadOnlyList<Guid> HistoryJobAttachmentIds { get; init; } = [];
}
