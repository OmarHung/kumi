using System.Globalization;
using PmTool.Abstractions;
using PmTool.Storage;

namespace PmTool.Ai.Attachments;

/// <summary>附件的失敗。代碼是對外合約（<see cref="AiErrorCodes"/>），訊息給人看。</summary>
internal static class AttachmentFailures
{
    public static Failure NotFound() =>
        new(FailureKind.NotFound, AiErrorCodes.AttachmentNotFound, "找不到這個附件。");

    /// <summary>送出時帶的編號不是自己待送的（別人的、已經送過的、過期刪掉的、別的專案的）；上傳之外的路徑是 422，不是 404。</summary>
    public static Failure NotPending() =>
        new(FailureKind.Invalid, AiErrorCodes.AttachmentNotFound, "附件不存在、已經送出過或已經過期，請重新上傳。");

    public static Failure TypeNotAllowed() =>
        new(FailureKind.Invalid, AiErrorCodes.AttachmentTypeNotAllowed,
            "只收圖片（PNG、JPEG、GIF、WebP）、PDF 與 UTF-8 的文字檔；Word、Excel、壓縮檔這類請先轉成 PDF 或文字檔。");

    /// <summary>類型收，但這個檔案供應商一定會拒絕（圖片太大張、PDF 加密）：同一個代碼，訊息說明是哪裡不行。</summary>
    public static Failure Unreadable(string reason) =>
        new(FailureKind.Invalid, AiErrorCodes.AttachmentTypeNotAllowed, reason);

    public static Failure TooLarge(AssistantAttachmentKind kind) =>
        new(FailureKind.Invalid, AiErrorCodes.AttachmentTooLarge, TooLargeMessage(kind));

    public static string TooLargeMessage(AssistantAttachmentKind kind)
    {
        var limit = AttachmentKinds.MaxBytesOf(kind);
        var text = limit >= 1024 * 1024 ? $"{limit / (1024 * 1024)} MB" : $"{limit / 1024} KB";
        return $"{AttachmentKinds.LabelOf(kind)}不能超過 {text}。";
    }

    /// <summary>上傳時內容超過所有種類的上限，連種類都還沒判斷就擋下來。</summary>
    public static Failure TooLargeToRead() =>
        new(FailureKind.Invalid, AiErrorCodes.AttachmentTooLarge,
            $"附件不能超過 {AssistantAttachmentRules.MaxUploadBytes / (1024 * 1024)} MB。");

    public static Failure TooLargeInTotal() =>
        new(FailureKind.Invalid, AiErrorCodes.AttachmentTooLarge,
            $"一則訊息的附件合計不能超過 {AssistantAttachmentRules.MaxBytesPerMessage / (1024 * 1024)} MB。");

    /// <summary>API 的模型：這一則的文字附件合計超過每一輪重送的預算。</summary>
    public static Failure TextTooLong() =>
        new(FailureKind.Invalid, AiErrorCodes.AttachmentTooLarge,
            $"這一則的文字附件合計不能超過 {AssistantAttachmentRules.MaxReplayTextCharacters.ToString("N0", CultureInfo.InvariantCulture)} 字；請少附一些檔案，或把檔案拆小。");

    /// <summary>本機與主機上的引擎：這一則（打的字加文字附件）放不進提示的長度上限。</summary>
    public static Failure PromptTooLong() =>
        new(FailureKind.Invalid, AiErrorCodes.AttachmentTooLarge,
            $"本機與主機上的引擎一次最多讀 {AiRules.MaxLocalPromptLength.ToString("N0", CultureInfo.InvariantCulture)} 字（含附件）；" +
            "這則訊息加上文字附件超過了，請縮短訊息、少附一些檔案，或換成 API 的模型。");

    public static Failure TooManyPending() =>
        new(FailureKind.Conflict, AiErrorCodes.TooManyAttachments,
            $"你有 {AssistantAttachmentRules.MaxPendingPerPerson} 個附件還沒送出，請先送出或移除一些。");

    public static Failure TooManyInMessage() =>
        new(FailureKind.Invalid, AiErrorCodes.TooManyAttachments,
            $"一則訊息最多 {AssistantAttachmentRules.MaxPerMessage} 個附件。");

    /// <summary>kumi 太舊：本機的請使用者更新自己的電腦，主機的請組織擁有者更新 ai-worker（成員不必也沒辦法動主機）。</summary>
    public static Failure AgentTooOld(AgentSource source)
    {
        var version = string.IsNullOrWhiteSpace(source.Version) ? "不明" : source.Version;
        var minimum = AiRules.MinAgentVersionForAttachments;
        var message = source.Hosted
            ? $"「{source.Name}」的 ai-worker（kumi {version}）要 {minimum} 以上才能交圖片與 PDF 給引擎，請組織擁有者更新主機的 ai-worker；" +
                "在那之前請移除圖片與 PDF，或換成 API 的模型。"
            : $"「{source.Name}」那台電腦上的 kumi 是 {version}，要 {minimum} 以上才能交圖片與 PDF 給引擎，請執行 kumi update；" +
                "在那之前請移除圖片與 PDF，或換成 API 的模型。";
        return new(FailureKind.Invalid, AiErrorCodes.AttachmentUnsupported, message);
    }

    public static Failure CodexNoPdf(AgentSource source) =>
        new(FailureKind.Invalid, AiErrorCodes.AttachmentUnsupported,
            $"Codex 讀不到 PDF（圖片可以）：你選的是「{source.Name}」，請移除 PDF，或改選 Claude Code 或 API 的模型。");

    /// <summary>超過的是組織的總量還是這個人的每日量，代碼不同、說的話也不同。</summary>
    public static Failure Quota(StorageQuotaCheck check) =>
        check.Verdict == StorageQuotaVerdict.PersonDailyFull
            ? new(FailureKind.Conflict, AiErrorCodes.AttachmentDailyQuotaExceeded, StorageQuotaMessages.PersonDailyFull(check))
            : new(FailureKind.Conflict, AiErrorCodes.AttachmentQuotaExceeded, StorageQuotaMessages.OrganizationFull(check));
}
