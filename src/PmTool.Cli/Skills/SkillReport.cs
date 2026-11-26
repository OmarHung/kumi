using System.Text.Json.Nodes;
using PmTool.Cli.Agent;
using PmTool.Cli.Output;

namespace PmTool.Cli.Skills;

/// <summary>把 <see cref="SkillOutcome"/> 印成文字或 JSON。--json 的欄位與字串是對外合約（docs/CLI.md）。</summary>
internal static class SkillReport
{
    public static string AgentOption(string kind) => kind == EngineKinds.Codex ? "codex" : "claude-code";

    public static string ActionCode(SkillOutcome outcome) => outcome.Action switch
    {
        SkillAction.Installed => "installed",
        SkillAction.Updated => "updated",
        SkillAction.UpToDate => "up_to_date",
        SkillAction.SkippedForeign => "skipped_foreign",
        SkillAction.SkippedModified or SkillAction.KeptModified => "skipped_modified",
        SkillAction.SkippedNewer => "skipped_newer",
        SkillAction.Removed => "removed",
        SkillAction.NotOurs => "not_ours",
        SkillAction.NotInstalled => "not_installed",
        SkillAction.LegacyRemoved => "legacy_removed",
        SkillAction.LegacyKept => "legacy_kept",
        _ => "failed",
    };

    public static async Task WriteJsonAsync(TextWriter output, IReadOnlyList<SkillOutcome> outcomes, string version, Func<SkillOutcome, string> code)
    {
        var results = new JsonArray();
        foreach (var outcome in outcomes)
        {
            results.Add(new JsonObject
            {
                ["agent"] = AgentOption(outcome.Location.Engine),
                ["directory"] = outcome.Location.Directory,
                ["skill"] = outcome.Skill,
                ["path"] = outcome.Path,
                ["result"] = code(outcome),
                ["version"] = outcome.Action is SkillAction.UpToDate or SkillAction.Updated or SkillAction.Installed or SkillAction.SkippedNewer ? outcome.Detail : null,
                ["error"] = outcome.Action == SkillAction.Failed ? outcome.Detail : null,
            });
        }

        await JsonOutput.WriteAsync(output, new JsonObject { ["version"] = version, ["results"] = results });
    }

    public static async Task WriteTextAsync(TextWriter output, TextWriter error, IReadOnlyList<SkillOutcome> outcomes)
    {
        foreach (var outcome in outcomes)
        {
            var (mark, text) = Describe(outcome);
            var writer = outcome.Action is SkillAction.LegacyKept or SkillAction.SkippedForeign or SkillAction.SkippedModified or SkillAction.KeptModified or SkillAction.SkippedNewer or SkillAction.Failed ? error : output;
            await writer.WriteLineAsync($"{mark} {outcome.Location.DisplayName}：{outcome.Skill}　{text}");
        }
    }

    private static (string Mark, string Text) Describe(SkillOutcome outcome) => outcome.Action switch
    {
        SkillAction.Installed => ("✓", $"已安裝到 {outcome.Path}"),
        SkillAction.Updated => ("✓", $"已更新到 {outcome.Detail}（{outcome.Path}）"),
        SkillAction.UpToDate => ("✓", $"已經是最新版（{outcome.Path}）"),
        SkillAction.SkippedForeign => ("–", $"{outcome.Path} 已經有同名的資料夾、不是 kumi 裝的（沒有 {SkillMarker.FileName}），沒有覆蓋。要換成 kumi 的版本請加 --force"),
        SkillAction.SkippedModified => ("–", $"{outcome.Path} 裡的檔案被改過（和安裝時不一樣），沒有覆蓋。要換成 kumi 的版本請加 --force（你改的內容會消失）"),
        SkillAction.KeptModified => ("–", $"{outcome.Path} 檔案改過，要移除請加 --force（你改的內容會一起刪掉）"),
        SkillAction.SkippedNewer => ("–", $"{outcome.Path} 已安裝的版本（{outcome.Detail}）比這份 kumi 內含的新，不降級。要降級請加 --force"),
        SkillAction.Removed => ("✓", $"已移除 {outcome.Path}"),
        SkillAction.NotOurs => ("–", $"{outcome.Path} 不是 kumi 裝的，沒有動"),
        SkillAction.NotInstalled => ("–", "沒有安裝"),
        SkillAction.LegacyRemoved => ("✓", $"已移除舊名稱的 skill（{outcome.Path}）"),
        SkillAction.LegacyKept => ("!", $"舊名稱的 skill 還在 {outcome.Path}；同一份 skill 出現兩次會讓代理混淆。執行 kumi skill install --yes 移除，或自己刪掉那個資料夾"),
        _ => ("✗", $"失敗：{outcome.Detail}"),
    };
}
