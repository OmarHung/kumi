using System.Text;
using PmTool.Abstractions;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Persistence;
using PmTool.Storage;

namespace PmTool.Ai.Attachments;

/// <summary>文字附件的字元數：上傳時已經確認是合法的 UTF-8，BOM 不算內容。</summary>
internal static class AttachmentText
{
    public static string Decode(ReadOnlySpan<byte> bytes) => new UTF8Encoding(false).GetString(bytes).TrimStart('\uFEFF');

    /// <summary>字元數至少是位元組數的三分之一（UTF-8 一個字最多三個位元組；四個位元組的字是兩個字元）。</summary>
    public static long MinCharacters(long bytes) => (bytes + 2) / 3;
}

/// <summary>
/// 送出時就擋掉放不進去的文字附件，不要等到重送給模型時才被悄悄丟掉（模型沒讀到卻照樣回答）：
/// API 的模型每一輪重送的文字檔有字元預算（<see cref="AssistantAttachmentRules.MaxReplayTextCharacters"/>），
/// 本機與主機上的引擎整個提示有長度上限（<see cref="AiRules.MaxLocalPromptLength"/>，這一則的打字加附件都算）。
/// 字元用實際的數：位元組數加起來就放得下的直接過、少算也放不下的直接擋，只有兩者之間才把檔案讀出來數（中文一個字三個位元組）。
/// </summary>
internal sealed class AttachmentTextLimits(IFileStore store)
{
    /// <summary>每個文字附件在提示裡多出來的標題與圍欄（<see cref="AttachmentReplay.FormatText"/>）。</summary>
    private const int FormatOverhead = 16;

    public async Task<Failure?> CheckAsync(
        IReadOnlyList<AiAttachmentRow> rows, int typedCharacters, bool localEngine, CancellationToken cancellationToken)
    {
        var texts = rows.Where(row => row.Kind == AttachmentKinds.Text).ToList();
        if (texts.Count == 0)
        {
            return null;
        }

        var limit = localEngine
            ? AiRules.MaxLocalPromptLength - LocalPromptBuilder.FixedOverhead - typedCharacters
            : AssistantAttachmentRules.MaxReplayTextCharacters;
        if (await FitsAsync(texts, limit, cancellationToken))
        {
            return null;
        }

        return localEngine ? AttachmentFailures.PromptTooLong() : AttachmentFailures.TextTooLong();
    }

    private async Task<bool> FitsAsync(IReadOnlyList<AiAttachmentRow> texts, long limit, CancellationToken cancellationToken)
    {
        var overhead = texts.Sum(row => (long)row.FileName.Length + FormatOverhead);
        if (limit < overhead)
        {
            return false;
        }

        var upper = texts.Sum(row => row.SizeBytes) + overhead;
        if (upper <= limit)
        {
            return true;
        }

        if (texts.Sum(row => AttachmentText.MinCharacters(row.SizeBytes)) + overhead > limit)
        {
            return false;
        }

        long characters = overhead;
        foreach (var row in texts)
        {
            characters += await CountAsync(row, cancellationToken);
            if (characters > limit)
            {
                return false;
            }
        }

        return true;
    }

    // 檔案不見了（儲存空間壞了、備份沒還原）算 0：送出時不為它擋；真的送給模型時會換成「檔案已經不在了」的說明。
    private async Task<int> CountAsync(AiAttachmentRow row, CancellationToken cancellationToken)
    {
        await using var content = await store.OpenReadAsync(AttachmentMapper.KeyOf(row.Id), cancellationToken);
        if (content is null)
        {
            return 0;
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        return AttachmentText.Decode(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)).Length;
    }
}
