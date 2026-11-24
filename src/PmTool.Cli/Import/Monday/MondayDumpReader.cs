using System.Text.Json;
using System.Text.Json.Nodes;

namespace PmTool.Cli.Import.Monday;

/// <summary>把 fetch 存的 dump 讀成 <see cref="MondayDump"/>。run 只看這些 record，不碰 monday 的 JSON。</summary>
internal static class MondayDumpReader
{
    public static MondayDump Load(string directory)
    {
        var dump = Path.GetFullPath(directory);
        if (!File.Exists(MondayDumpLayout.MetaPath(dump)))
        {
            throw new CliException(ExitCodes.Error, $"{dump} 不是 dump 資料夾（找不到 {MondayDumpLayout.MetaFile}）。先執行 kumi import monday fetch。");
        }

        var meta = ReadJson(MondayDumpLayout.MetaPath(dump));
        var fetchedAt = MondayJson.Date(meta["fetchedAt"])
            ?? throw Broken(MondayDumpLayout.MetaPath(dump), "沒有 fetchedAt");
        var users = MondayJson.Items(ReadJson(MondayDumpLayout.UsersPath(dump))["users"]).Select(ReadUser).ToArray();
        var boards = MondayJson.Items(meta["boards"])
            .Select(id => MondayJson.Text(id) ?? throw Broken(MondayDumpLayout.MetaPath(dump), "boards 裡有不是編號的值"))
            .Select(id => LoadBoard(dump, id))
            .ToArray();
        return new MondayDump(dump, fetchedAt, users, boards) { SkippedFiles = ReadSkippedFiles(dump) };
    }

    private static Dictionary<string, string> ReadSkippedFiles(string dump)
    {
        var path = MondayDumpLayout.SkippedPath(dump);
        var skipped = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return skipped;
        }

        foreach (var entry in MondayJson.Items(ReadJson(path)))
        {
            if (MondayJson.Text(entry["assetId"]) is { } id && MondayJson.Text(entry["reason"]) is { } reason)
            {
                skipped[id] = reason;
            }
        }

        return skipped;
    }

    internal static CliException Broken(string file, string reason) =>
        new(ExitCodes.Error, $"dump 的檔案 {file} 讀不了：{reason}。重新執行 kumi import monday fetch。");

    internal static JsonNode ReadJson(string path)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) ?? throw Broken(path, "內容是空的");
        }
        catch (JsonException exception)
        {
            throw Broken(path, $"不是合法的 JSON（{exception.Message}）");
        }
        catch (IOException exception)
        {
            throw Broken(path, exception.Message);
        }
    }

    private static MondayUser ReadUser(JsonNode user) => new(
        MondayJson.Text(user["id"]) ?? string.Empty,
        MondayJson.Text(user["name"]) ?? string.Empty,
        NullIfBlank(MondayJson.Text(user["email"])));

    private static MondayBoard LoadBoard(string dump, string boardId)
    {
        var path = MondayDumpLayout.BoardPath(dump, boardId);
        if (!File.Exists(path))
        {
            throw Broken(path, "找不到看板檔");
        }

        var board = MondayJson.Items(ReadJson(path)["boards"]).FirstOrDefault() ?? throw Broken(path, "沒有看板的資料");
        var subitemPath = MondayDumpLayout.SubitemBoardPath(dump, boardId);
        var subitemColumns = File.Exists(subitemPath)
            ? ReadColumns(MondayJson.Items(ReadJson(subitemPath)["boards"]).FirstOrDefault())
            : [];
        return new MondayBoard(
            MondayJson.Text(board["id"]) ?? boardId,
            MondayJson.Text(board["name"]) ?? boardId,
            [.. MondayJson.Items(board["groups"]).Select(group => new MondayGroup(
                MondayJson.Text(group["id"]) ?? string.Empty, MondayJson.Text(group["title"]) ?? string.Empty))],
            ReadColumns(board),
            subitemColumns,
            MondayItemReader.LoadItems(dump, boardId));
    }

    private static IReadOnlyList<MondayColumn> ReadColumns(JsonNode? board) =>
        [.. MondayJson.Items(board?["columns"]).Select(column =>
        {
            var type = MondayJson.Text(column["type"]) ?? string.Empty;
            return new MondayColumn(
                MondayJson.Text(column["id"]) ?? string.Empty,
                MondayJson.Text(column["title"]) ?? string.Empty,
                type,
                type == "status" ? StatusLabels(MondayJson.Text(column["settings_str"])) : []);
        })];

    /// <summary>settings_str 的 labels 是 {"0":"Working on it","1":"Done"}（也可能是陣列）；照 index 排，空的標籤不列。</summary>
    internal static IReadOnlyList<string> StatusLabels(string? settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
        {
            return [];
        }

        try
        {
            return JsonNode.Parse(settings)?["labels"] switch
            {
                JsonObject labels => [.. labels
                    .Select(pair => (Index: int.TryParse(pair.Key, out var index) ? index : int.MaxValue, Label: MondayJson.Text(pair.Value)))
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Label))
                    .OrderBy(entry => entry.Index)
                    .Select(entry => entry.Label!)],
                JsonArray labels => [.. labels.Select(MondayJson.Text).Where(label => !string.IsNullOrWhiteSpace(label)).Select(label => label!)],
                _ => [],
            };
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
}
