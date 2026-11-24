using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PmTool.Cli.Import.Monday;

internal sealed record MondayFetchOptions(IReadOnlyList<string> Boards, string OutDirectory, MondayMapping? Mapping, bool DownloadFiles);

internal sealed record MondayBoardSummary(string Id, string Name, int Pages, int Items, int Subitems);

internal sealed record MondayFetchSummary(
    string Directory,
    int Users,
    IReadOnlyList<MondayBoardSummary> Boards,
    bool FilesRequested,
    int FilesDownloaded,
    int FilesReused,
    IReadOnlyList<SkippedFile> FilesSkipped);

/// <summary>
/// 把 monday 的看板抓成 dump（internal/HANDOFF-monday-import.md 第 4 節）。GraphQL 回應的 data 原樣存成檔案
/// （只拿掉 complexity 與簽章網址 public_url）；先寫到暫存資料夾，全部成功才換上。
/// </summary>
internal sealed class MondayFetcher(MondayGraphQlClient client, HttpClient http, TextWriter progress)
{
    private const int MaxUpdatePages = 500;

    public async Task<MondayFetchSummary> RunAsync(MondayFetchOptions options, CancellationToken cancellationToken)
    {
        // 結尾的斜線要去掉：不然暫存資料夾（和目標同一層）會落在目標資料夾裡面，最後換不上去。
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.OutDirectory));
        MondayDumpDirectory.EnsureReplaceable(target);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = MondayDumpDirectory.NewTemporary(target);
        Directory.CreateDirectory(temporary);
        try
        {
            var summary = await FetchIntoAsync(temporary, target, options, cancellationToken);
            MondayDumpDirectory.Replace(temporary, target);
            return summary with { Directory = target };
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }
        }
    }

    private async Task<MondayFetchSummary> FetchIntoAsync(string temporary, string target, MondayFetchOptions options, CancellationToken cancellationToken)
    {
        var downloader = options.DownloadFiles
            ? new MondayFileDownloader(http, temporary, Directory.Exists(target) ? target : null)
            : null;
        var users = await FetchUsersAsync(temporary, cancellationToken);
        var boards = new List<MondayBoardSummary>();
        var groupsByBoard = new JsonObject();
        foreach (var boardId in options.Boards)
        {
            var groups = GroupsToFetch(options.Mapping, boardId);
            groupsByBoard[boardId] = groups is null ? null : new JsonArray([.. groups.Select(group => (JsonNode?)JsonValue.Create(group))]);
            boards.Add(await FetchBoardAsync(temporary, boardId, groups, downloader, cancellationToken));
        }

        downloader?.WriteSkipped();
        MondayDumpLayout.WriteJson(MondayDumpLayout.MetaPath(temporary), new JsonObject
        {
            ["fetchedAt"] = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            ["boards"] = new JsonArray([.. options.Boards.Select(id => (JsonNode?)JsonValue.Create(id))]),
            ["groups"] = groupsByBoard,
        });
        return new MondayFetchSummary(
            target, users, boards, options.DownloadFiles, downloader?.Downloaded ?? 0, downloader?.Reused ?? 0, downloader?.Skipped ?? []);
    }

    /// <summary>
    /// 有對應檔時只抓沒有 skip 的群組，用伺服器端的 query_params 篩（items_page 的 group 規則）：
    /// 巢狀的留言很重，抓完再篩會白白下載、白白耗掉複雜度額度。對應檔沒提到的看板抓整個。
    /// </summary>
    private List<string>? GroupsToFetch(MondayMapping? mapping, string boardId)
    {
        if (mapping is null)
        {
            return null;
        }

        if (!mapping.Boards.TryGetValue(boardId, out var board))
        {
            progress.WriteLine($"對應檔沒有看板 {boardId}，抓整個看板。");
            return null;
        }

        return [.. board.Groups.Where(pair => !pair.Value.Skip).Select(pair => pair.Key)];
    }

    private async Task<int> FetchUsersAsync(string temporary, CancellationToken cancellationToken)
    {
        var users = new JsonArray();
        for (var page = 1; ; page++)
        {
            var data = await client.QueryAsync(MondayQueries.UsersDeclarations, MondayQueries.UsersSelection,
                new JsonObject { ["limit"] = MondayQueries.UsersPageSize, ["page"] = page }, cancellationToken);
            var batch = MondayJson.Items(data["users"]);
            foreach (var user in batch)
            {
                users.Add(user.DeepClone());
            }

            if (batch.Count < MondayQueries.UsersPageSize)
            {
                break;
            }
        }

        MondayDumpLayout.WriteJson(MondayDumpLayout.UsersPath(temporary), new JsonObject { ["users"] = users });
        progress.WriteLine($"使用者：{users.Count} 位");
        return users.Count;
    }

    private async Task<MondayBoardSummary> FetchBoardAsync(
        string temporary, string boardId, IReadOnlyList<string>? groups, MondayFileDownloader? downloader, CancellationToken cancellationToken)
    {
        var data = await client.QueryAsync(MondayQueries.BoardDeclarations, MondayQueries.BoardSelection, IdVariables(boardId), cancellationToken);
        var board = MondayJson.Items(data["boards"]).FirstOrDefault()
            ?? throw new CliException(ExitCodes.Error, $"找不到看板 {boardId}：編號不對，或這把權杖看不到它。");
        MondayDumpLayout.WriteJson(MondayDumpLayout.BoardPath(temporary, boardId), data);
        await FetchSubitemBoardAsync(temporary, boardId, board, cancellationToken);
        var name = MondayJson.Text(board["name"]) ?? boardId;
        if (groups is { Count: 0 })
        {
            progress.WriteLine($"看板 {boardId}（{name}）：所有群組都標了 skip，沒有項目要抓。");
            return new MondayBoardSummary(boardId, name, 0, 0, 0);
        }

        var totals = await FetchItemPagesAsync(temporary, boardId, groups, downloader, cancellationToken);
        return new MondayBoardSummary(boardId, name, totals.Pages, totals.Items, totals.Subitems);
    }

    private async Task FetchSubitemBoardAsync(string temporary, string boardId, JsonNode board, CancellationToken cancellationToken)
    {
        var subitemBoardId = MondayJson.Items(board["columns"])
            .Where(column => MondayJson.Text(column["type"]) == "subtasks" || MondayJson.Text(column["id"]) == "subitems")
            .Select(SubitemBoardIdOf)
            .FirstOrDefault(id => id is not null);
        if (subitemBoardId is null)
        {
            return;
        }

        var data = await client.QueryAsync(MondayQueries.BoardDeclarations, MondayQueries.SubitemBoardSelection, IdVariables(subitemBoardId), cancellationToken);
        MondayDumpLayout.WriteJson(MondayDumpLayout.SubitemBoardPath(temporary, boardId), data);
    }

    /// <summary>subitems 欄的 settings_str 是 {"boardIds":[…]}，取第一個。</summary>
    private static string? SubitemBoardIdOf(JsonNode column)
    {
        try
        {
            var settings = JsonNode.Parse(MondayJson.Text(column["settings_str"]) ?? "{}");
            return MondayJson.Text(MondayJson.Items(settings?["boardIds"]).FirstOrDefault());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<(int Pages, int Items, int Subitems)> FetchItemPagesAsync(
        string temporary, string boardId, IReadOnlyList<string>? groups, MondayFileDownloader? downloader, CancellationToken cancellationToken)
    {
        var (pages, items, subitems) = (0, 0, 0);
        string? cursor = null;
        do
        {
            var data = cursor is null
                ? await client.QueryAsync(MondayQueries.FirstPageDeclarations, MondayQueries.FirstPageSelection, FirstPageVariables(boardId, groups), cancellationToken)
                : await client.QueryAsync(MondayQueries.NextPageDeclarations, MondayQueries.NextPageSelection, NextPageVariables(cursor), cancellationToken);
            var page = (cursor is null ? data["boards"]?[0]?["items_page"] : data["next_items_page"])
                ?? throw new CliException(ExitCodes.Error, $"看板 {boardId} 的項目頁沒有內容（monday 的回應格式和預期的不一樣）。");
            var pageItems = MondayJson.Items(page["items"]);
            pages++;
            items += pageItems.Count;
            subitems += pageItems.Sum(item => MondayJson.Items(item["subitems"]).Count);
            await StoreItemPageAsync(temporary, boardId, pages, data, pageItems, downloader, cancellationToken);
            progress.WriteLine($"看板 {boardId} 第 {pages} 頁：{pageItems.Count} 個項目（累計 {items}）");
            cursor = MondayJson.Text(page["cursor"]);
        }
        while (cursor is not null);
        return (pages, items, subitems);
    }

    private async Task StoreItemPageAsync(
        string temporary, string boardId, int pageNumber, JsonObject data, IReadOnlyList<JsonNode> items,
        MondayFileDownloader? downloader, CancellationToken cancellationToken)
    {
        var assets = MondayAssetCollector.StripPublicUrls(data);
        MondayDumpLayout.WriteJson(MondayDumpLayout.ItemsPagePath(temporary, boardId, pageNumber), data);
        await DownloadAsync(downloader, assets, cancellationToken);
        foreach (var item in items.Concat(items.SelectMany(item => MondayJson.Items(item["subitems"]))))
        {
            if (MondayJson.Items(item["updates"]).Count >= MondayQueries.UpdatesPageSize
                && MondayJson.Text(item["id"]) is { } itemId)
            {
                await FetchMoreUpdatesAsync(temporary, itemId, downloader, cancellationToken);
            }
        }
    }

    /// <summary>留言取滿一頁（100 則）的項目：接著讀第 2 頁以後，存成 updates/&lt;項目&gt;.json（每頁的 data 一個元素）。</summary>
    private async Task FetchMoreUpdatesAsync(string temporary, string itemId, MondayFileDownloader? downloader, CancellationToken cancellationToken)
    {
        var pages = new JsonArray();
        for (var page = 2; page <= MaxUpdatePages; page++)
        {
            var variables = new JsonObject { ["ids"] = new JsonArray(JsonValue.Create(itemId)), ["page"] = page };
            var data = await client.QueryAsync(MondayQueries.MoreUpdatesDeclarations, MondayQueries.MoreUpdatesSelection, variables, cancellationToken);
            var count = MondayJson.Items(MondayJson.Items(data["items"]).FirstOrDefault()?["updates"]).Count;
            if (count == 0)
            {
                break;
            }

            await DownloadAsync(downloader, MondayAssetCollector.StripPublicUrls(data), cancellationToken);
            pages.Add(data);
            if (count < MondayQueries.UpdatesPageSize)
            {
                break;
            }
        }

        if (pages.Count > 0)
        {
            MondayDumpLayout.WriteJson(MondayDumpLayout.UpdatesPath(temporary, itemId), pages);
            progress.WriteLine($"項目 {itemId} 的留言超過 {MondayQueries.UpdatesPageSize} 則，多讀了 {pages.Count} 頁。");
        }
    }

    private static async Task DownloadAsync(MondayFileDownloader? downloader, IReadOnlyList<MondayAssetRef> assets, CancellationToken cancellationToken)
    {
        if (downloader is null)
        {
            return;
        }

        foreach (var asset in assets)
        {
            await downloader.EnsureAsync(asset, cancellationToken);
        }
    }

    private static JsonObject IdVariables(string id) => new() { ["ids"] = new JsonArray(JsonValue.Create(id)) };

    private static JsonObject FirstPageVariables(string boardId, IReadOnlyList<string>? groups)
    {
        var variables = IdVariables(boardId);
        variables["limit"] = MondayQueries.ItemsPageSize;
        if (groups is not null)
        {
            variables["query"] = new JsonObject
            {
                ["rules"] = new JsonArray(new JsonObject
                {
                    ["column_id"] = "group",
                    ["compare_value"] = new JsonArray([.. groups.Select(group => (JsonNode?)JsonValue.Create(group))]),
                    ["operator"] = "any_of",
                }),
            };
        }

        return variables;
    }

    private static JsonObject NextPageVariables(string cursor) => new() { ["cursor"] = cursor, ["limit"] = MondayQueries.ItemsPageSize };
}
