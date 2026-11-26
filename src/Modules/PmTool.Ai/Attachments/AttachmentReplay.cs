using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using PmTool.Storage;

namespace PmTool.Ai.Attachments;

/// <summary>附件要送給哪一種來源：API 的模型讀得到圖片與 PDF，本機與主機上的引擎只讀得到文字。</summary>
internal enum AttachmentTarget
{
    ApiModel,
    TextOnly,
}

/// <summary>重送的選項：哪些附件之前被供應商拒絕過（直接用說明）、這一次要不要把歷史的圖片與 PDF 全部拿掉（重試用）。</summary>
internal sealed record ReplayOptions
{
    public static ReplayOptions Default { get; } = new();

    /// <summary><c>ai.attachments.replay_skipped_at</c> 有值的附件編號。</summary>
    public IReadOnlySet<Guid> SkippedIds { get; init; } = new HashSet<Guid>();

    public bool OmitHistoryBinary { get; init; }
}

/// <param name="HistoryBinaryIds">這次真的以圖片或 PDF 送出的歷史附件（不含這一則）；請求被拒、重試成功時，這些就是要記下不再重送的。</param>
internal sealed record ReplayResult(IReadOnlyList<ChatMessage> Messages, IReadOnlyList<Guid> HistoryBinaryIds);

/// <summary>
/// 交易之後、呼叫模型之前，把訊息裡的附件參照（<see cref="AttachmentReference"/>）換成真正的內容
/// （internal/HANDOFF-ai-attachments.md 第 4 節）。從最新的訊息往回處理，圖片與 PDF 合計
/// <see cref="AssistantAttachmentRules.MaxReplayBinaryBytes"/>、文字檔合計 <see cref="AssistantAttachmentRules.MaxReplayTextCharacters"/> 個字元；
/// 一種超過之後，更舊的同一種一律換成一行說明（不跳過去撿更舊的小檔案：留下的永遠是最近的）。
/// 對話越長請求越大，不限制的話最後會超過供應商的請求上限。
/// </summary>
internal sealed partial class AttachmentReplay(IFileStore store, ILogger<AttachmentReplay> logger)
{
    private const string Fence = "`";

    private const int MinFenceLength = 4;

    /// <summary>
    /// 把 <paramref name="messages"/> 裡的附件參照換掉。最後一則是這一次要回答的訊息（說明的寫法和之前附的不同）。
    /// 沒有附件的訊息原樣留著；讀不到檔案（儲存空間裡沒有）換成說明，儲存空間本身壞了則讓例外往上丟，這則回覆就失敗。
    /// </summary>
    public async Task<IReadOnlyList<ChatMessage>> ResolveAsync(
        IReadOnlyList<ChatMessage> messages, AttachmentTarget target, CancellationToken cancellationToken) =>
        (await ResolveAsync(messages, target, ReplayOptions.Default, cancellationToken)).Messages;

    public async Task<ReplayResult> ResolveAsync(
        IReadOnlyList<ChatMessage> messages, AttachmentTarget target, ReplayOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(options);
        if (!messages.Any(HasReferences))
        {
            return new ReplayResult(messages, []);
        }

        var run = new Run(target, options);
        var resolved = new ChatMessage[messages.Count];
        for (var index = messages.Count - 1; index >= 0; index--)
        {
            resolved[index] = HasReferences(messages[index])
                ? await ResolveMessageAsync(messages[index], run, isCurrent: index == messages.Count - 1, cancellationToken)
                : messages[index];
        }

        return new ReplayResult(resolved, run.HistoryBinaryIds);
    }

    /// <summary>附件的文字檔接在訊息後面的格式：標題一行，內容用四個以上的反引號圍起來（內容裡有更長的連續反引號就再多一個）。</summary>
    public static string FormatText(string fileName, string content)
    {
        var fence = new string('`', Math.Max(MinFenceLength, LongestRun(content, '`') + 1));
        var body = content.EndsWith('\n') ? content : content + "\n";
        return $"〔附件：{fileName}〕\n{fence}\n{body}{fence}";
    }

    private async Task<ChatMessage> ResolveMessageAsync(
        ChatMessage message, Run run, bool isCurrent, CancellationToken cancellationToken)
    {
        var contents = new List<AIContent>(message.Contents.Count);
        foreach (var content in message.Contents)
        {
            contents.Add(content is AttachmentReference reference
                ? await ResolveAsync(reference, run, isCurrent, cancellationToken)
                : content);
        }

        return new ChatMessage(message.Role, contents);
    }

    private async Task<AIContent> ResolveAsync(
        AttachmentReference reference, Run run, bool isCurrent, CancellationToken cancellationToken)
    {
        if (reference.Kind != AssistantAttachmentKind.Text)
        {
            return run.Target == AttachmentTarget.TextOnly
                ? Note(reference, "這個來源讀不到圖片與 PDF")
                : await ResolveBinaryAsync(reference, run, isCurrent, cancellationToken);
        }

        return await ResolveTextAsync(reference, run.Budget, isCurrent, cancellationToken);
    }

    private async Task<AIContent> ResolveBinaryAsync(
        AttachmentReference reference, Run run, bool isCurrent, CancellationToken cancellationToken)
    {
        // 歷史裡供應商拒絕過的（或這次重試要全部拿掉的）直接用說明；這一則自己的不跳過，被拒就是要讓使用者看到原因。
        if (!isCurrent && (run.Options.OmitHistoryBinary || run.Options.SkippedIds.Contains(reference.Id)))
        {
            return Note(reference, "之前附的，模型讀不了，這次沒有再送");
        }

        var budget = run.Budget;
        if (budget.BinaryExhausted || reference.SizeBytes > budget.BinaryBytes)
        {
            budget.BinaryExhausted = true;
            return Note(reference, OmittedReason(isCurrent));
        }

        var bytes = await ReadAsync(reference, cancellationToken);
        if (bytes is null)
        {
            return Note(reference, "檔案已經不在了");
        }

        budget.BinaryBytes -= bytes.Length;
        if (!isCurrent)
        {
            run.HistoryBinaryIds.Add(reference.Id);
        }

        // PDF 要設檔名：OpenAI 的 Chat Completions（連同 OpenRouter 與相容端點）的 file 區塊要 filename，Gemini 拿它當 displayName。
        return new DataContent(bytes, reference.ContentType) { Name = reference.FileName };
    }

    private async Task<AIContent> ResolveTextAsync(
        AttachmentReference reference, Budget budget, bool isCurrent, CancellationToken cancellationToken)
    {
        // 額度已經用完（或前面有一份放不下）就不必再讀檔；檔案再怎麼省，字元數至少是位元組數的三分之一（UTF-8 一個字最多三個位元組，
        // 四個位元組的字是兩個字元），這樣都放不下的也不必讀出來再丟掉。
        if (budget.TextExhausted || budget.TextCharacters <= 0 || AttachmentText.MinCharacters(reference.SizeBytes) > budget.TextCharacters)
        {
            budget.TextExhausted = true;
            return Note(reference, OmittedReason(isCurrent));
        }

        var bytes = await ReadAsync(reference, cancellationToken);
        if (bytes is null)
        {
            return Note(reference, "檔案已經不在了");
        }

        var text = AttachmentText.Decode(bytes);
        if (text.Length > budget.TextCharacters)
        {
            budget.TextExhausted = true;
            return Note(reference, OmittedReason(isCurrent));
        }

        budget.TextCharacters -= text.Length;
        return new TextContent(FormatText(reference.FileName, text));
    }

    private async Task<byte[]?> ReadAsync(AttachmentReference reference, CancellationToken cancellationToken)
    {
        await using var content = await store.OpenReadAsync(AttachmentMapper.KeyOf(reference.Id), cancellationToken);
        if (content is null)
        {
            LogMissingFile(logger, reference.Id);
            return null;
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private static string OmittedReason(bool isCurrent) => isCurrent ? "這次放不下，沒有送" : "之前附的，這次沒有再送";

    private static TextContent Note(AttachmentReference reference, string reason) => new($"〔附件：{reference.FileName}（{reason}）〕");

    private static bool HasReferences(ChatMessage message) => message.Contents.Any(c => c is AttachmentReference);

    private static int LongestRun(string text, char c)
    {
        int longest = 0, current = 0;
        foreach (var ch in text)
        {
            current = ch == c ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }

        return longest;
    }

    /// <summary>一次重送的狀態：目標、選項、預算，與已經以圖片或 PDF 送出的歷史附件。</summary>
    private sealed class Run(AttachmentTarget target, ReplayOptions options)
    {
        public AttachmentTarget Target { get; } = target;

        public ReplayOptions Options { get; } = options;

        public Budget Budget { get; } = new(target);

        public List<Guid> HistoryBinaryIds { get; } = [];
    }

    private sealed class Budget(AttachmentTarget target)
    {
        public long BinaryBytes { get; set; } = AssistantAttachmentRules.MaxReplayBinaryBytes;

        // 本機與主機的提示整個只有 MaxLocalPromptLength 個字元，更舊的文字檔讀出來也會被丟掉，預算跟著縮小。
        public int TextCharacters { get; set; } = target == AttachmentTarget.TextOnly
            ? AiRules.MaxLocalPromptLength
            : AssistantAttachmentRules.MaxReplayTextCharacters;

        public bool BinaryExhausted { get; set; }

        public bool TextExhausted { get; set; }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "助理附件 {AttachmentId} 在對話裡有參照，儲存空間裡卻找不到檔案；請從備份還原。")]
    private static partial void LogMissingFile(ILogger logger, Guid attachmentId);
}
