namespace PmTool.Storage;

/// <summary>
/// 附件（項目的、知識庫的）共用的儲存設定，區段名稱照舊叫 Attachments：已經部署的環境靠它。<see cref="Provider"/> 是 <see cref="LocalProvider"/> 或 <see cref="GcsProvider"/>；
/// 缺了或不能用時，服務啟動就失敗並說明（由組合根呼叫 <see cref="IFileStore.VerifyAsync"/>），不等到第一次上傳才發現。
/// </summary>
/// <param name="Directory">local 用：存檔的資料夾，要存在而且寫得進去。相對路徑從程序的工作資料夾算起。</param>
/// <param name="Bucket">gcs 用：bucket 名稱。</param>
public sealed record FileStoreSettings(string? Provider, string? Directory = null, string? Bucket = null)
{
    public const string SectionName = "Attachments";
    public const string ProviderSetting = "Attachments:Provider";
    public const string DirectorySetting = "Attachments:Directory";
    public const string BucketSetting = "Attachments:Bucket";

    public const string LocalProvider = "local";
    public const string GcsProvider = "gcs";
}
