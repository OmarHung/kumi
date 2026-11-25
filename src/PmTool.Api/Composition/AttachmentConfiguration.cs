using PmTool.Storage;
using PmTool.Work;

namespace PmTool.Api.Composition;

internal static class AttachmentConfiguration
{
    /// <summary>
    /// 讀 Attachments 區段。這裡只讀、不驗證：缺了或不能用要等啟動檢查（<see cref="WorkModule.VerifyAttachmentStorageAsync"/>）才說，
    /// 這樣 migrate 命令（不碰附件）不必設定儲存空間。相對路徑從網站的根目錄（content root）算起，不看程序從哪裡啟動。
    /// </summary>
    public static FileStoreSettings Read(IConfiguration configuration)
    {
        var directory = SettingValues.ReadText(configuration, FileStoreSettings.DirectorySetting);
        if (directory is not null && !Path.IsPathRooted(directory))
        {
            var root = configuration[Microsoft.Extensions.Hosting.HostDefaults.ContentRootKey];
            directory = Path.GetFullPath(directory, string.IsNullOrWhiteSpace(root) ? Directory.GetCurrentDirectory() : root);
        }

        return new FileStoreSettings(
            SettingValues.ReadText(configuration, FileStoreSettings.ProviderSetting),
            directory,
            SettingValues.ReadText(configuration, FileStoreSettings.BucketSetting));
    }

    /// <summary>
    /// 讀儲存空間的上限（單位 GiB）：<c>Attachments:Quota:OrganizationGiB</c>（預設 20）與 <c>Attachments:Quota:PersonDailyGiB</c>（預設 2）。
    /// 整數、0 以上，0 是不限制。設錯就讓啟動失敗：悄悄當成不限制，會讓設定打錯字的部署沒有上限。
    /// </summary>
    /// <exception cref="InvalidOperationException">不是整數、是負數，或換算成位元組會溢位。</exception>
    public static StorageQuotaSettings ReadQuota(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new StorageQuotaSettings(
            ReadGiB(configuration, StorageQuotaSettings.OrganizationGiBSetting, 20),
            ReadGiB(configuration, StorageQuotaSettings.PersonDailyGiBSetting, 2));
    }

    private static long ReadGiB(IConfiguration configuration, string key, long defaultGiB)
    {
        var raw = SettingValues.ReadText(configuration, key);
        if (raw is null)
        {
            return defaultGiB * StorageQuotaSettings.GiB;
        }

        // 最大值留在 long 放得下的範圍裡：8 EiB 以內，換算不會溢位。
        const long MaxGiB = long.MaxValue / StorageQuotaSettings.GiB;
        if (!long.TryParse(raw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var gib) || gib > MaxGiB)
        {
            throw new InvalidOperationException($"{key} 要是 0 以上的整數（單位 GiB，0 是不限制），現在是「{raw}」。");
        }

        return gib * StorageQuotaSettings.GiB;
    }
}
