using PmTool.Abstractions;
using PmTool.Storage;

namespace PmTool.Knowledge.Attachments;

/// <summary>附件的失敗。代碼是對外合約（<see cref="KnowledgeErrorCodes"/>）。</summary>
internal static class AttachmentFailures
{
    public static Failure NotFound() =>
        new(FailureKind.NotFound, KnowledgeErrorCodes.AttachmentNotFound, "找不到這個附件。");

    public static Failure TooLarge() =>
        new(FailureKind.Invalid, KnowledgeErrorCodes.AttachmentTooLarge, $"附件不能超過 {KnowledgeRules.MaxAttachmentBytes / (1024 * 1024)} MB。");

    public static Failure LimitReached() =>
        new(FailureKind.Conflict, KnowledgeErrorCodes.AttachmentLimitReached, $"一頁最多 {KnowledgeRules.MaxAttachmentsPerPage} 個附件。");

    /// <summary>超過的是組織的總量還是這個人的每日量，代碼不同、說的話也不同。</summary>
    public static Failure Quota(StorageQuotaCheck check) =>
        check.Verdict == StorageQuotaVerdict.PersonDailyFull
            ? new(FailureKind.Conflict, KnowledgeErrorCodes.AttachmentDailyQuotaExceeded, StorageQuotaMessages.PersonDailyFull(check))
            : new(FailureKind.Conflict, KnowledgeErrorCodes.AttachmentQuotaExceeded, StorageQuotaMessages.OrganizationFull(check));
}
