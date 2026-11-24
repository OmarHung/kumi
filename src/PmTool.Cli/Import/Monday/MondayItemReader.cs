using System.Text.Json.Nodes;

namespace PmTool.Cli.Import.Monday;

/// <summary>dump 裡項目、留言、檔案的讀取（<see cref="MondayDumpReader"/> 的一部分）。</summary>
internal static class MondayItemReader
{
    public static IReadOnlyList<MondayItem> LoadItems(string dump, string boardId)
    {
        var folder = MondayDumpLayout.ItemsFolder(dump, boardId);
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var seen = new HashSet<string>();
        var items = new List<MondayItem>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.Ordinal))
        {
            var data = MondayDumpReader.ReadJson(file);
            var page = data["boards"]?[0]?["items_page"] ?? data["next_items_page"];
            foreach (var node in MondayJson.Items(page?["items"]))
            {
                var item = ReadItem(dump, node, isSubitem: false);
                if (seen.Add(item.Id))
                {
                    items.Add(item);
                }
            }
        }

        return items;
    }

    private static MondayItem ReadItem(string dump, JsonNode node, bool isSubitem)
    {
        var id = MondayJson.Text(node["id"]) ?? string.Empty;
        return new MondayItem(
            id,
            MondayJson.Text(node["name"]) ?? string.Empty,
            isSubitem ? string.Empty : MondayJson.Text(node["group"]?["id"]) ?? string.Empty,
            MondayJson.Text(node["state"]) ?? "active",
            MondayJson.Date(node["created_at"]) ?? throw MondayDumpReader.Broken($"項目 {id}", "沒有 created_at"),
            MondayJson.Text(node["creator_id"]),
            MondayJson.Text(node["url"]) ?? string.Empty,
            [.. MondayJson.Items(node["column_values"]).Select(ReadValue)],
            isSubitem ? [] : [.. MondayJson.Items(node["subitems"]).Select(sub => ReadItem(dump, sub, isSubitem: true))],
            ReadUpdates(dump, id, node["updates"]),
            ReadAssets(dump, node["assets"]));
    }

    private static MondayColumnValue ReadValue(JsonNode node) => new(
        MondayJson.Text(node["id"]) ?? string.Empty,
        MondayJson.Text(node["type"]) ?? string.Empty,
        MondayJson.Text(node["text"]),
        node["value"] switch
        {
            null => null,
            JsonValue value => MondayJson.Text(value),
            { } other => other.ToJsonString(),
        },
        MondayJson.Text(node["label"]),
        [.. MondayJson.Items(node["persons_and_teams"])
            .Where(person => string.Equals(MondayJson.Text(person["kind"]), "person", StringComparison.OrdinalIgnoreCase))
            .Select(person => MondayJson.Text(person["id"]))
            .OfType<string>()],
        [.. MondayJson.Items(node["linked_items"]).Select(linked =>
            new MondayLinkedItem(MondayJson.Text(linked["id"]) ?? string.Empty, MondayJson.Text(linked["name"]) ?? string.Empty))]);

    /// <summary>第一頁在項目裡，超過 100 則的其餘頁在 updates/&lt;項目&gt;.json；兩邊可能重疊，以編號去重。照時間由舊到新。</summary>
    private static IReadOnlyList<MondayUpdate> ReadUpdates(string dump, string itemId, JsonNode? inline)
    {
        var nodes = MondayJson.Items(inline).ToList();
        var extra = MondayDumpLayout.UpdatesPath(dump, itemId);
        if (File.Exists(extra))
        {
            foreach (var page in MondayJson.Items(MondayDumpReader.ReadJson(extra)))
            {
                nodes.AddRange(MondayJson.Items(MondayJson.Items(page["items"]).FirstOrDefault()?["updates"]));
            }
        }

        var seen = new HashSet<string>();
        return [.. nodes
            .Where(node => seen.Add(MondayJson.Text(node["id"]) ?? string.Empty))
            .Select(node => ReadUpdate(dump, node))
            .OrderBy(update => update.CreatedAt)
            .ThenBy(update => update.Id, StringComparer.Ordinal)];
    }

    private static MondayUpdate ReadUpdate(string dump, JsonNode node)
    {
        var id = MondayJson.Text(node["id"]) ?? string.Empty;
        var replies = MondayJson.Items(node["replies"])
            .Select(reply => ReadReply(dump, reply))
            .OrderBy(reply => reply.CreatedAt)
            .ThenBy(reply => reply.Id, StringComparer.Ordinal)
            .ToArray();
        return new MondayUpdate(
            id,
            MondayJson.Text(node["creator_id"]),
            MondayJson.Date(node["created_at"]) ?? throw MondayDumpReader.Broken($"留言 {id}", "沒有 created_at"),
            MondayJson.Text(node["body"]) ?? string.Empty,
            ReadAssets(dump, node["assets"]),
            replies);
    }

    private static MondayReply ReadReply(string dump, JsonNode node) => new(
        MondayJson.Text(node["id"]) ?? string.Empty,
        MondayJson.Text(node["creator_id"]),
        MondayJson.Date(node["created_at"]) ?? throw MondayDumpReader.Broken($"回覆 {MondayJson.Text(node["id"])}", "沒有 created_at"),
        MondayJson.Text(node["body"]) ?? string.Empty,
        ReadAssets(dump, node["assets"]));

    private static IReadOnlyList<MondayAsset> ReadAssets(string dump, JsonNode? assets) =>
        [.. MondayJson.Items(assets).Select(asset =>
        {
            var id = MondayJson.Text(asset["id"]) ?? string.Empty;
            var name = MondayJson.Text(asset["name"]) ?? string.Empty;
            var local = MondayDumpLayout.AssetPath(dump, id, name);
            return new MondayAsset(
                id,
                name,
                MondayJson.Long(asset["file_size"]),
                MondayJson.Text(asset["uploaded_by"]?["id"]),
                MondayJson.Date(asset["created_at"]),
                File.Exists(local) ? local : null);
        })];
}
