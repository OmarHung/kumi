using Google.Cloud.Storage.V1;

namespace PmTool.Work.Attachments;

/// <summary>依設定建出儲存空間。設定缺了或不認得時丟例外，訊息說得出缺的是哪個設定。</summary>
internal static class AttachmentStores
{
    public static IAttachmentStore Create(AttachmentSettings settings)
    {
        switch (settings.Provider?.Trim().ToLowerInvariant())
        {
            case AttachmentSettings.LocalProvider:
                if (string.IsNullOrWhiteSpace(settings.Directory))
                {
                    throw new InvalidOperationException(
                        $"{AttachmentSettings.ProviderSetting} 是 local，但沒有設定 {AttachmentSettings.DirectorySetting}（存附件的資料夾）。");
                }

                return new LocalAttachmentStore(System.IO.Path.GetFullPath(settings.Directory.Trim()));
            case AttachmentSettings.GcsProvider:
                if (string.IsNullOrWhiteSpace(settings.Bucket))
                {
                    throw new InvalidOperationException(
                        $"{AttachmentSettings.ProviderSetting} 是 gcs，但沒有設定 {AttachmentSettings.BucketSetting}（bucket 名稱）。");
                }

                return new GcsAttachmentStore(StorageClient.Create(), settings.Bucket.Trim());
            case null or "":
                throw new InvalidOperationException(
                    $"沒有設定附件的儲存方式（{AttachmentSettings.ProviderSetting}）：local（主機的資料夾）或 gcs（Google Cloud Storage）。");
            default:
                throw new InvalidOperationException(
                    $"{AttachmentSettings.ProviderSetting} 只能是 local 或 gcs，現在是「{settings.Provider}」。");
        }
    }
}
