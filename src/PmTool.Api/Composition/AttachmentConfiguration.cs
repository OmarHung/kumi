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
}
