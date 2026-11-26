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
        IReadOnlyList<ChatMessage> messages, AttachmentTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (!messages.Any(HasReferences))
        {
            return messages;
        }

        var budget = new Budget();
        var resolved = new ChatMessage[messages.Count];
        for (var index = messages.Count - 1; index >= 0; index--)
        {
            resolved[index] = HasReferences(messages[index])
                ? await ResolveMessageAsync(messages[index], target, isCurrent: index == messages.Count - 1, budget, cancellationToken)
                : messages[index];
        }

        return resolved;
    }

    /// <summary>附件的文字檔接在訊息後面的格式：標題一行，內容用四個以上的反引號圍起來（內容裡有更長的連續反引號就再多一個）。</summary>
    public static string FormatText(string fileName, string content)
    {
        var fence = new string('`', Math.Max(MinFenceLength, LongestRun(content, '`') + 1));
        var body = content.EndsWith('\n') ? content : content + "\n";
        return $"〔附件：{fileName}〕\n{fence}\n{body}{fence}";
    }

    private async Task<ChatMessage> ResolveMessageAsync(
        ChatMessage message, AttachmentTarget target, bool isCurrent, Budget budget, CancellationToken cancellationToken)
    {
        var contents = new List<AIContent>(message.Contents.Count);
        foreach (var content in message.Contents)
        {
            contents.Add(content is AttachmentReference reference
                ? await ResolveAsync(reference, target, isCurrent, budget, cancellationToken)
                : content);
        }

        return new ChatMessage(message.Role, contents);
    }

    private async Task<AIContent> ResolveAsync(
        AttachmentReference reference, AttachmentTarget target, bool isCurrent, Budget budget, CancellationToken cancellationToken)
    {
        if (reference.Kind != AssistantAttachmentKind.Text)
        {
            return target == AttachmentTarget.TextOnly
                ? Note(reference, "這個來源讀不到圖片與 PDF")
                : await ResolveBinaryAsync(reference, isCurrent, budget, cancellationToken);
        }

        return await ResolveTextAsync(reference, isCurrent, budget, cancellationToken);
    }

    private async Task<AIContent> ResolveBinaryAsync(
        AttachmentReference reference, bool isCurrent, Budget budget, CancellationToken cancellationToken)
    {
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

        // PDF 要設檔名：OpenAI 的 Chat Completions（連同 OpenRouter 與相容端點）的 file 區塊要 filename，Gemini 拿它當 displayName。
        return new DataContent(bytes, reference.ContentType) { Name = reference.FileName };
    }

    private async Task<AIContent> ResolveTextAsync(
        AttachmentReference reference, bool isCurrent, Budget budget, CancellationToken cancellationToken)
    {
        // 額度已經用完（或前面有一份放不下）就不必再讀檔。
        if (budget.TextExhausted || budget.TextCharacters <= 0)
        {
            budget.TextExhausted = true;
            return Note(reference, OmittedReason(isCurrent));
        }

        var bytes = await ReadAsync(reference, cancellationToken);
        if (bytes is null)
        {
            return Note(reference, "檔案已經不在了");
        }

        // 上傳時擋掉了不合法的 UTF-8，這裡不會丟例外；BOM 不算內容。
        var text = new UTF8Encoding(false).GetString(bytes).TrimStart('﻿');
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

    private sealed class Budget
    {
        public long BinaryBytes { get; set; } = AssistantAttachmentRules.MaxReplayBinaryBytes;

        public int TextCharacters { get; set; } = AssistantAttachmentRules.MaxReplayTextCharacters;

        public bool BinaryExhausted { get; set; }

        public bool TextExhausted { get; set; }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "助理附件 {AttachmentId} 在對話裡有參照，儲存空間裡卻找不到檔案；請從備份還原。")]
    private static partial void LogMissingFile(ILogger logger, Guid attachmentId);
}
