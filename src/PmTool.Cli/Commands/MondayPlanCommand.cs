using System.Text.Json.Nodes;
using PmTool.Cli.Api;
using PmTool.Cli.Import.Monday;
using PmTool.Cli.Output;

namespace PmTool.Cli.Commands;

/// <summary>kumi import monday plan：從 dump 產生對應檔的草稿。人員要用這個伺服器的人員清單對 Email，所以需要登入。</summary>
internal static class MondayPlanCommand
{
    public static async Task<int> RunAsync(CommandCall call)
    {
        var output = ImportCommands.Resolve(call, call.Arguments.Required("out"));
        if (File.Exists(output) && !call.Arguments.Has("force"))
        {
            throw CliException.Usage($"{output} 已經存在，不覆蓋。要蓋掉請加 --force。");
        }

        var dump = MondayDumpReader.Load(ImportCommands.Resolve(call, call.Arguments.Required("dump")));
        var people = await call.Api.GetAsync("people", null, call.CancellationToken);
        var emails = people.Items().Select(person => person.Text("email")).OfType<string>().ToArray();
        var draft = MondayPlanner.Draft(dump, emails, RelativePath(Path.GetDirectoryName(output)!, dump.Directory));
        MondayMappingFile.Save(output, draft.Mapping, draft.Annotations);
        await ReportAsync(call, output, draft);
        return ExitCodes.Success;
    }

    /// <summary>對應檔裡寫相對路徑：整個資料夾搬到別的電腦也還找得到。</summary>
    private static string RelativePath(string fromDirectory, string target)
    {
        var relative = Path.GetRelativePath(fromDirectory, target).Replace('\\', '/');
        return Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal) ? relative : "./" + relative;
    }

    private static async Task ReportAsync(CommandCall call, string output, MondayMappingDraft draft)
    {
        var matched = draft.Mapping.People.Values.Count(email => email is not null);
        var groups = draft.Mapping.Boards.Values.Sum(board => board.Groups.Count);
        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, new JsonObject
            {
                ["out"] = output,
                ["people"] = new JsonObject { ["total"] = draft.Mapping.People.Count, ["matched"] = matched },
                ["boards"] = draft.Mapping.Boards.Count,
                ["groups"] = groups,
            });
            return;
        }

        await call.Out.WriteLineAsync($"已寫入 {output}");
        await call.Out.WriteLineAsync($"人員：{draft.Mapping.People.Count} 位，依 Email 對上 {matched} 位（對不上的是 null）");
        await call.Out.WriteLineAsync($"看板：{draft.Mapping.Boards.Count} 個，群組：{groups} 個（project 與 type、狀態對應要自己填）");
        await call.Out.WriteLineAsync("改好之後執行 kumi import monday fetch --mapping 只抓要匯的群組並下載檔案。");
    }
}
