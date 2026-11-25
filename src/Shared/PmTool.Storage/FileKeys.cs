using System.Globalization;

namespace PmTool.Storage;

/// <summary>
/// 附件的鍵一定是 Guid 的格式：本機的檔案路徑與 GCS 的物件名稱都完全由它決定，不給路徑符號與別的名稱進來的機會。
/// 兩種儲存共用同一份檢查，換儲存空間時規則不會變。
/// </summary>
internal static class FileKeys
{
    public static string Canonical(string key)
    {
        if (!Guid.TryParseExact(key, "D", out var id))
        {
            throw new ArgumentException("附件的鍵必須是編號。", nameof(key));
        }

        return id.ToString("D", CultureInfo.InvariantCulture);
    }
}
