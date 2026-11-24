using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Cli.Api;
using PmTool.Cli.Output;

namespace PmTool.Cli.Commands;

/// <summary>專案說明（網址、部署環境、主機）：看與改。</summary>
internal static class ProjectDescriptionCommands
{
    private const string FileOption = "file";
    private const string ClearOption = "clear";

    public static readonly IReadOnlyList<CommandSpec> Specs =
    [
        new("project", "info", "看專案說明（網址、部署環境、主機）", "kumi project info 代號 [--json]", [], 1, 1, InfoAsync),
        new("project", "describe", "改專案說明（Markdown）；不要寫密碼與金鑰",
            "kumi project describe 代號 (--file 檔案 | --clear) [--json]",
            [new(FileOption, "檔案", "從檔案讀說明（- 是標準輸入）"), new(ClearOption, "", "清掉說明")], 1, 1, DescribeAsync),
        new("project", "search", "用代號、名稱或專案說明找專案（例如主機名稱、網址）", "kumi project search 文字 [--limit 數量] [--json]",
            [new("limit", "數量", "最多列幾個（1 到 20，預設 8）")], 1, 1, SearchAsync),
        new("project", "formats", "列出專案說明的格式（開始寫說明前先看有哪些）", "kumi project formats [--json]", [], 0, 0, FormatsAsync),
    ];

    private static async Task<int> SearchAsync(CommandCall call)
    {
        var query = new List<KeyValuePair<string, string>> { new("q", call.Arguments.Positionals[0]) };
        if (call.Arguments.Value("limit") is { } limit)
        {
            query.Add(new("limit", limit));
        }

        var hits = await call.Api.GetAsync("search/projects", query, call.CancellationToken);
        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, hits);
            return ExitCodes.Success;
        }

        if (hits.Items().Count == 0)
        {
            await call.Out.WriteLineAsync("找不到符合的專案。");
            return ExitCodes.Success;
        }

        // 摘錄是 null 表示代號或名稱就比對到了，不必再看說明：欄位留空。
        var rows = hits.Items()
            .Select(hit => (IReadOnlyList<string>)
                [hit.Get("project")?.Display("key") ?? "-", ProjectNameOf(hit), hit.Text("snippet") ?? string.Empty])
            .ToArray();
        await TextTable.WriteAsync(call.Out, ["代號", "名稱", "說明裡的"], rows);
        return ExitCodes.Success;
    }

    private static string ProjectNameOf(JsonElement hit) =>
        hit.Get("project") is { } project ? ProjectCommands.NameWithArchiveMark(project) : "-";

    private static async Task<int> FormatsAsync(CommandCall call)
    {
        var formats = await call.Api.GetAsync("description-templates", null, call.CancellationToken);
        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, formats);
            return ExitCodes.Success;
        }

        // 內容很長，表格不印；要內容用 --json。
        var rows = formats.Items()
            .Select(format => (IReadOnlyList<string>)[format.Display("id"), format.Display("name"), Marks(format)])
            .ToArray();
        await TextTable.WriteAsync(call.Out, ["代號", "名稱", "標記"], rows);
        return ExitCodes.Success;
    }

    private static string Marks(JsonElement format)
    {
        var marks = new List<string>();
        if (format.Flag("isDefault"))
        {
            marks.Add("預設");
        }

        if (format.Flag("isBuiltIn"))
        {
            marks.Add("內建");
        }

        return string.Join("、", marks);
    }

    private static async Task<int> InfoAsync(CommandCall call)
    {
        var description = await call.Api.GetAsync(PathOf(call), null, call.CancellationToken);
        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, description);
            return ExitCodes.Success;
        }

        if (description.Text("text") is not { } text)
        {
            await call.Out.WriteLineAsync("這個專案還沒有說明。");
            return ExitCodes.Success;
        }

        // 原文照印（結尾的換行不重複加），空一行再寫最後更新，不會被當成說明的一部分。
        await call.Out.WriteAsync(text.EndsWith('\n') ? text : text + "\n");
        await call.Out.WriteLineAsync();
        var by = description.Get("updatedBy")?.Display("displayName") ?? "-";
        await call.Out.WriteLineAsync($"最後由 {by} 於 {JsonAccess.Moment(description.Text("updatedAt"))} 更新");
        return ExitCodes.Success;
    }

    private static async Task<int> DescribeAsync(CommandCall call)
    {
        var hasFile = call.Arguments.Has(FileOption);
        var clear = call.Arguments.Has(ClearOption);
        if (hasFile == clear)
        {
            throw CliException.Usage("--file 與 --clear 要二選一。用法：kumi project describe 代號 (--file 檔案 | --clear)");
        }

        var text = clear ? null : await ReadTextAsync(call, call.Arguments.Required(FileOption));
        var key = call.Arguments.Positionals[0];
        var result = await call.Api.PutAsync(PathOf(call), new JsonObject { ["text"] = text }, call.CancellationToken);
        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, result);
        }
        else
        {
            await call.Out.WriteLineAsync(clear ? $"已清除 {key} 的專案說明。" : $"已更新 {key} 的專案說明。");
        }

        return ExitCodes.Success;
    }

    /// <summary>空白的內容不送：標準輸入忘了接內容時，不該悄悄把原本的說明清掉，要清請用 --clear。</summary>
    private static async Task<string> ReadTextAsync(CommandCall call, string file)
    {
        var text = file == "-"
            ? await call.Context.In.ReadToEndAsync(call.CancellationToken)
            : await ReadFileAsync(call, file);
        return string.IsNullOrWhiteSpace(text)
            ? throw CliException.Usage("要寫的說明是空白的。要清掉說明請用 --clear。")
            : text;
    }

    private static async Task<string> ReadFileAsync(CommandCall call, string file)
    {
        var fullPath = Path.GetFullPath(file, call.Context.WorkingDirectory);
        try
        {
            return await File.ReadAllTextAsync(fullPath, call.CancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CliException(ExitCodes.Error, $"讀不到檔案 {file}：{exception.Message}");
        }
    }

    private static string PathOf(CommandCall call) => $"projects/{Uri.EscapeDataString(call.Arguments.Positionals[0])}/description";
}
