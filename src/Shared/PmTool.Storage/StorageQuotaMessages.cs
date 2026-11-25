using System.Globalization;

namespace PmTool.Storage;

/// <summary>超過上限時給使用者看的句子。數字一律用 GiB、小數一位，兩個模組說的話一樣。</summary>
public static class StorageQuotaMessages
{
    public static string OrganizationFull(StorageQuotaCheck check) =>
        $"組織的附件已經用了 {Used(check.OrganizationUsedBytes)} GiB，上限是 {Limit(check.OrganizationLimitBytes)} GiB。"
        + "請刪掉不用的附件，或請管理員調高上限。";

    public static string PersonDailyFull(StorageQuotaCheck check) =>
        $"你在過去 24 小時已經上傳 {Used(check.PersonUsedBytes)} GiB，上限是 {Limit(check.PersonLimitBytes)} GiB，請稍後再試。";

    private static string Used(long bytes) => Format(bytes, "F1");

    /// <summary>上限通常是整數 GiB，寫成「20」而不是「20.0」。</summary>
    private static string Limit(long bytes) => Format(bytes, bytes % StorageQuotaSettings.GiB == 0 ? "F0" : "F1");

    private static string Format(long bytes, string format) =>
        ((double)bytes / StorageQuotaSettings.GiB).ToString(format, CultureInfo.InvariantCulture);
}
