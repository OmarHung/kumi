using System.Globalization;
using System.Text.Json.Nodes;
using PmTool.Cli.Import.Monday;
using PmTool.Cli.Output;

namespace PmTool.Cli.Commands;

/// <summary>kumi import monday fetch：抓 monday 的資料存成 dump。不碰 kumi 的伺服器，所以不需要登入。</summary>
internal static class MondayFetchCommand
{
    public static async Task<int> RunAsync(CommandCall call)
    {
        var boards = ParseBoards(call.Arguments.Values("board"));
        var output = ImportCommands.Resolve(call, call.Arguments.Required("out"));
        var mapping = call.Arguments.Value("mapping") is { } mappingPath
            ? MondayMappingFile.Load(ImportCommands.Resolve(call, mappingPath))
            : null;
        var client = MondayGraphQlClient.FromEnvironment(call.Context);
        var fetcher = new MondayFetcher(client, call.Context.Http, call.Error);
        var summary = await fetcher.RunAsync(
            new MondayFetchOptions(boards, output, mapping, call.Arguments.Has("files")), call.CancellationToken);
        await ReportAsync(call, summary);
        return ExitCodes.Success;
    }

    private static IReadOnlyList<string> ParseBoards(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            throw CliException.Usage("缺少 --board（monday 看板的編號）。");
        }

        foreach (var value in values.Where(value => value.Length == 0 || !value.All(char.IsAsciiDigit)))
        {
            throw CliException.Usage($"--board 要是數字的編號（看板網址裡的數字），不是「{value}」。");
        }

        return [.. values.Distinct(StringComparer.Ordinal)];
    }

    private static async Task ReportAsync(CommandCall call, MondayFetchSummary summary)
    {
        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, ToJson(summary));
            return;
        }

        await call.Out.WriteLineAsync($"已存到 {summary.Directory}");
        await call.Out.WriteLineAsync($"使用者 {summary.Users} 位");
        foreach (var board in summary.Boards)
        {
            await call.Out.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
                $"看板 {board.Id}（{board.Name}）：{board.Items} 個項目、{board.Subitems} 個子項目，{board.Pages} 頁"));
        }

        if (summary.FilesRequested)
        {
            await call.Out.WriteLineAsync($"檔案：下載 {summary.FilesDownloaded}、沿用上次的 {summary.FilesReused}、沒下載 {summary.FilesSkipped.Count}"
                + (summary.FilesSkipped.Count > 0 ? "（原因在 files/skipped.json）" : string.Empty));
        }
    }

    private static JsonObject ToJson(MondayFetchSummary summary) => new()
    {
        ["directory"] = summary.Directory,
        ["users"] = summary.Users,
        ["boards"] = new JsonArray([.. summary.Boards.Select(board => (JsonNode?)new JsonObject
        {
            ["id"] = board.Id,
            ["name"] = board.Name,
            ["pages"] = board.Pages,
            ["items"] = board.Items,
            ["subitems"] = board.Subitems,
        })]),
        ["files"] = summary.FilesRequested
            ? new JsonObject
            {
                ["downloaded"] = summary.FilesDownloaded,
                ["reused"] = summary.FilesReused,
                ["skipped"] = summary.FilesSkipped.Count,
            }
            : null,
    };
}
