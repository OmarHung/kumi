using System.Text.Json.Nodes;

namespace PmTool.Cli.Import.Monday;

/// <summary>要下載的檔案。<paramref name="Url"/> 是一小時內有效的簽章網址，只留在記憶體裡，不進 dump。</summary>
internal sealed record MondayAssetRef(string Id, string Name, long? SizeBytes, string? Url);

internal static class MondayAssetCollector
{
    private const string UrlProperty = "public_url";

    /// <summary>
    /// 把整棵 JSON 裡每個資產的 public_url 取出來並從樹上拿掉。簽章網址等於暫時的密碼，
    /// 存進 dump 就是把它留在硬碟（與備份）裡；所以一律在寫檔之前拿掉，下載只用取出來的這份。
    /// </summary>
    public static IReadOnlyList<MondayAssetRef> StripPublicUrls(JsonNode? root)
    {
        var found = new List<MondayAssetRef>();
        Walk(root, found);
        return found;
    }

    private static void Walk(JsonNode? node, List<MondayAssetRef> found)
    {
        switch (node)
        {
            case JsonObject item:
                if (item.ContainsKey(UrlProperty))
                {
                    found.Add(new MondayAssetRef(
                        MondayJson.Text(item["id"]) ?? string.Empty,
                        MondayJson.Text(item["name"]) ?? string.Empty,
                        MondayJson.Long(item["file_size"]),
                        MondayJson.Text(item[UrlProperty])));
                    item.Remove(UrlProperty);
                }

                foreach (var child in item.Select(pair => pair.Value).ToArray())
                {
                    Walk(child, found);
                }

                break;
            case JsonArray list:
                foreach (var child in list)
                {
                    Walk(child, found);
                }

                break;
        }
    }
}
