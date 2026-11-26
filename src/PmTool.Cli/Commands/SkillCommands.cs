using PmTool.Cli.Agent;
using PmTool.Cli.Setup;
using PmTool.Cli.Skills;

namespace PmTool.Cli.Commands;

/// <summary>
/// <c>kumi skill</c>：把內嵌在 kumi 裡的 Agent Skill（skills/kumi、skills/kumi-planning）裝進 Claude Code 與 Codex，之後跟著 kumi 更新。
/// 資料夾裡的標記檔（<see cref="SkillMarker.FileName"/>）分辨 kumi 裝的與使用者自己放的：後者不覆蓋、不刪。
/// </summary>
internal static class SkillCommands
{
    private static readonly OptionSpec AgentOption = new("agent", "引擎", "claude-code、codex 或 all（install 預設是偵測得到的；其他指令預設兩個都看）");
    private static readonly OptionSpec ProjectOption = new("project", "資料夾", "改成裝在這個專案資料夾（.claude/skills 與 .agents/skills），不是使用者層");

    public static readonly IReadOnlyList<CommandSpec> Specs =
    [
        new("skill", "install", "把給 AI 代理用的 skill 裝進 Claude Code／Codex",
            "kumi skill install [--agent claude-code|codex|all] [--project 資料夾] [--force] [--yes] [--json]",
            [
                AgentOption,
                ProjectOption,
                new OptionSpec("force", "", "同名資料夾不是 kumi 裝的、改過、或版本比較新時也換掉它"),
                new OptionSpec("yes", "", "舊名稱（pm-tool、pm-tool-planning）的 skill 直接整個資料夾刪除，不問", 'y'),
            ],
            0, 0, InstallAsync),
        new("skill", "update", "把裝過的 skill 換成這份 kumi 內含的版本（只動 kumi 裝的）",
            "kumi skill update [--agent claude-code|codex|all] [--project 資料夾] [--json]", [AgentOption, ProjectOption], 0, 0, UpdateAsync),
        new("skill", "status", "列出每個位置裝了哪些 skill、版本、是不是最新",
            "kumi skill status [--agent claude-code|codex|all] [--project 資料夾] [--json]", [AgentOption, ProjectOption], 0, 0, StatusAsync),
        new("skill", "uninstall", "移除 kumi 裝的 skill（沒有標記或檔案改過的不動）",
            "kumi skill uninstall [--agent claude-code|codex|all] [--project 資料夾] [--force] [--json]",
            [AgentOption, ProjectOption, new OptionSpec("force", "", "檔案改過的資料夾也移除（你改的內容會一起刪掉）")], 0, 0, UninstallAsync),
    ];

    private static async Task<int> InstallAsync(CommandCall call)
    {
        var locations = Resolve(call, detectedOnly: true);
        if (locations.Count == 0)
        {
            await call.Error.WriteLineAsync("找不到 Claude Code 或 Codex，沒有安裝 skill。裝好之後再執行一次，或用 --agent claude-code／codex 指定、--project 裝到專案資料夾。");
            return ExitCodes.Success;
        }

        var interactive = call.Context.IsInteractive && !call.Json;
        var policy = call.Arguments.Has("yes") ? LegacyPolicy.Remove : interactive ? LegacyPolicy.Ask : LegacyPolicy.Keep;
        var prompt = new SetupPrompt(call.Context, call.CancellationToken);
        var outcomes = await SkillService.Current().InstallAsync(
            locations, call.Arguments.Has("force"), policy,
            directory => prompt.AskAsync($"舊名稱的 skill {directory} 還在，同一份 skill 出現兩次會讓代理混淆。要移除嗎？"));
        return await ReportAsync(call, outcomes, SkillReport.ActionCode);
    }

    private static async Task<int> UpdateAsync(CommandCall call)
    {
        var locations = Resolve(call, detectedOnly: false);
        var service = SkillService.Current();
        if (!service.HasInstalled(locations))
        {
            if (call.Json)
            {
                await SkillReport.WriteJsonAsync(call.Out, [], HelpText.VersionNumber, SkillReport.ActionCode);
            }
            else
            {
                await call.Out.WriteLineAsync("沒有找到 kumi 裝的 skill，不需要更新。要安裝請執行 kumi skill install。");
            }

            return ExitCodes.Success;
        }

        return await ReportAsync(call, service.Update(locations), SkillReport.ActionCode);
    }

    private static async Task<int> StatusAsync(CommandCall call)
    {
        var outcomes = SkillService.Current().Status(Resolve(call, detectedOnly: false));
        if (call.Json)
        {
            await SkillReport.WriteJsonAsync(call.Out, outcomes, HelpText.VersionNumber, StatusCode);
            return ExitCodes.Success;
        }

        await call.Out.WriteLineAsync($"這份 kumi 是 {HelpText.VersionNumber}，內含的 skill 也是這一版。");
        foreach (var group in outcomes.GroupBy(outcome => outcome.Location))
        {
            await call.Out.WriteLineAsync();
            await call.Out.WriteLineAsync($"{group.Key.DisplayName}（{group.Key.Label}：{group.Key.Directory}）");
            foreach (var outcome in group)
            {
                await call.Out.WriteLineAsync($"  {outcome.Skill,-16} {StatusText(outcome)}");
            }
        }

        return ExitCodes.Success;
    }

    private static async Task<int> UninstallAsync(CommandCall call)
    {
        var outcomes = SkillService.Current().Uninstall(Resolve(call, detectedOnly: false), call.Arguments.Has("force"));
        if (!outcomes.Any(outcome => outcome.Action != SkillAction.NotInstalled))
        {
            if (call.Json)
            {
                await SkillReport.WriteJsonAsync(call.Out, [], HelpText.VersionNumber, SkillReport.ActionCode);
            }
            else
            {
                await call.Out.WriteLineAsync("沒有找到 kumi 裝的 skill。");
            }

            return ExitCodes.Success;
        }

        return await ReportAsync(call, outcomes.Where(outcome => outcome.Action != SkillAction.NotInstalled).ToList(), SkillReport.ActionCode);
    }

    private static async Task<int> ReportAsync(CommandCall call, IReadOnlyList<SkillOutcome> outcomes, Func<SkillOutcome, string> code)
    {
        if (call.Json)
        {
            await SkillReport.WriteJsonAsync(call.Out, outcomes, HelpText.VersionNumber, code);
        }
        else
        {
            await SkillReport.WriteTextAsync(call.Out, call.Error, outcomes);
        }

        return outcomes.Any(outcome => outcome.Action == SkillAction.Failed) ? ExitCodes.Error : ExitCodes.Success;
    }

    private static string StatusCode(SkillOutcome outcome) => outcome.Action switch
    {
        SkillAction.UpToDate => "current",
        SkillAction.Updated => "outdated",
        SkillAction.NotOurs => "foreign",
        SkillAction.SkippedModified => "modified",
        SkillAction.SkippedNewer => "newer",
        SkillAction.LegacyKept => "legacy",
        _ => "not_installed",
    };

    private static string StatusText(SkillOutcome outcome) => outcome.Action switch
    {
        SkillAction.UpToDate => $"{outcome.Detail}（最新）",
        SkillAction.Updated => $"{outcome.Detail}（不是最新，kumi skill update 更新）",
        SkillAction.NotOurs => "有同名資料夾，但不是 kumi 裝的（沒有標記）",
        SkillAction.SkippedModified => $"{outcome.Detail}，檔案改過（kumi skill install --force 換回 kumi 的版本）",
        SkillAction.SkippedNewer => $"{outcome.Detail}（已安裝的版本比這份 kumi 新）",
        SkillAction.LegacyKept => "舊名稱的殘留，建議移除（kumi skill install --yes）",
        _ => "沒有安裝",
    };

    /// <summary>--project 只看那個專案資料夾；沒有的話看使用者層。install 預設只裝偵測得到的引擎，其他指令兩個都看。</summary>
    private static IReadOnlyList<SkillLocation> Resolve(CommandCall call, bool detectedOnly)
    {
        var context = call.Context;
        var kinds = SkillLocations.ParseAgent(call.Arguments.Value("agent"));
        if (call.Arguments.Value("project") is { } project)
        {
            var folder = SkillLocations.ResolveProject(context, project);
            return [.. (kinds ?? [EngineKinds.ClaudeCode, EngineKinds.Codex]).Select(kind => SkillLocations.Project(folder, kind))];
        }

        kinds ??= detectedOnly ? SkillLocations.Detected(context) : [EngineKinds.ClaudeCode, EngineKinds.Codex];
        return [.. kinds.Select(kind => SkillLocations.Global(context, kind))];
    }
}
