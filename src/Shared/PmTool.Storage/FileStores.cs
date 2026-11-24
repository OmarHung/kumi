using Google.Cloud.Storage.V1;

namespace PmTool.Storage;

/// <summary>依設定建出儲存空間。設定缺了或不認得時丟例外，訊息說得出缺的是哪個設定。</summary>
public static class FileStores
{
    public static IFileStore Create(FileStoreSettings settings)
    {
        switch (settings.Provider?.Trim().ToLowerInvariant())
        {
            case FileStoreSettings.LocalProvider:
                if (string.IsNullOrWhiteSpace(settings.Directory))
                {
                    throw new InvalidOperationException(
                        $"{FileStoreSettings.ProviderSetting} 是 local，但沒有設定 {FileStoreSettings.DirectorySetting}（存附件的資料夾）。");
                }

                return new LocalFileStore(System.IO.Path.GetFullPath(settings.Directory.Trim()));
            case FileStoreSettings.GcsProvider:
                if (string.IsNullOrWhiteSpace(settings.Bucket))
                {
                    throw new InvalidOperationException(
                        $"{FileStoreSettings.ProviderSetting} 是 gcs，但沒有設定 {FileStoreSettings.BucketSetting}（bucket 名稱）。");
                }

                return new GcsFileStore(StorageClient.Create(), settings.Bucket.Trim());
            case null or "":
                throw new InvalidOperationException(
                    $"沒有設定附件的儲存方式（{FileStoreSettings.ProviderSetting}）：local（主機的資料夾）或 gcs（Google Cloud Storage）。");
            default:
                throw new InvalidOperationException(
                    $"{FileStoreSettings.ProviderSetting} 只能是 local 或 gcs，現在是「{settings.Provider}」。");
        }
    }
}
