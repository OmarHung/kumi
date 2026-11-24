using PmTool.Cli.Import.Monday;
using PmTool.Cli.Import.Monday.Run;
using PmTool.Cli.Output;

namespace PmTool.Cli.Commands;

/// <summary>
/// kumi import monday run：照對應檔把 dump 匯進伺服器。先檢查（有問題一次列完、什麼都不寫），再寫入；
/// 可以重跑，已經匯入過的略過。報告印在標準輸出（--json 時是 JSON），進度印在標準錯誤。
/// </summary>
internal static class MondayRunCommand
{
    public static async Task<int> RunAsync(CommandCall call)
    {
        var mapping = MondayMappingFile.Load(ImportCommands.Resolve(call, call.Arguments.Required("mapping")));
        var options = new RunOptions(
            call.Arguments.Has("dry-run"), call.Arguments.Has("add-members"), call.Arguments.Value("member-role") ?? RunOptions.DefaultMemberRole);
        var issues = new Notes();
        var reminders = new Notes();
        var report = new ImportReport(options.DryRun, issues, reminders);
        var api = new HistoryApi(call.Api, mapping.Source, call.CancellationToken);

        MondayDump dump;
        try
        {
            dump = MondayDumpReader.Load(mapping.Dump);
        }
        catch (CliException exception)
        {
            issues.Add(NoteKinds.Mapping, exception.Message);
            return await Invalid(call, report);
        }

        var people = new PersonDirectory(mapping, dump, await api.PeopleAsync());
        var plan = await new ImportPlanner(mapping, dump, people, options, api, issues, reminders).BuildAsync();
        if (issues.Any)
        {
            return await Invalid(call, report);
        }

        try
        {
            await new ImportWriter(api, plan, options, report, message => call.Error.WriteLine(message)).RunAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            report.Status = ReportStatus.Failed;
            report.Failure = exception is CliException cli ? cli.Message : $"發生預期之外的錯誤（{exception.GetType().Name}）";
            await Finish(call, report);
            throw;
        }

        report.Status = issues.Any ? ReportStatus.Invalid : options.DryRun ? ReportStatus.DryRun : ReportStatus.Completed;
        await Finish(call, report);
        return issues.Any ? ExitCodes.Invalid : ExitCodes.Success;
    }

    private static async Task<int> Invalid(CommandCall call, ImportReport report)
    {
        report.Status = ReportStatus.Invalid;
        await Finish(call, report);
        return ExitCodes.Invalid;
    }

    private static async Task Finish(CommandCall call, ImportReport report)
    {
        if (call.Arguments.Value("report") is { } path)
        {
            var full = ImportCommands.Resolve(call, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(full, report.ToJsonText() + "\n", new System.Text.UTF8Encoding(false), call.CancellationToken);
        }

        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, report.ToJson());
        }
        else
        {
            await report.WriteTextAsync(call.Out);
        }
    }
}
