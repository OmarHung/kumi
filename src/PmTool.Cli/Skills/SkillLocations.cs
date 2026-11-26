using PmTool.Cli.Agent;

namespace PmTool.Cli.Skills;

/// <param name="Engine"><see cref="EngineKinds"/> 的種類。</param>
/// <param name="Directory">skill 資料夾的上一層（例如 ~/.claude/skills）。</param>
/// <param name="Label">給人看的位置說明。</param>
internal sealed record SkillLocation(string Engine, string Directory, string Label)
{
    public string DisplayName => EngineKinds.DisplayName(Engine);
}

/// <summary>skill 要裝去哪裡：使用者層（各引擎自己的設定資料夾）或某個專案資料夾。</summary>
internal static class SkillLocations
{
    private static readonly string[] Kinds = [EngineKinds.ClaudeCode, EngineKinds.Codex];

    /// <summary>--agent 的值換成引擎種類；all 是兩個都要；不認得的是用法錯誤。</summary>
    public static IReadOnlyList<string>? ParseAgent(string? option)
    {
        if (option is null)
        {
            return null;
        }

        if (option.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return Kinds;
        }

        return EngineKinds.FromOption(option) is { } kind
            ? [kind]
            : throw CliException.Usage($"--agent 只能是 claude-code、codex 或 all，不是 {option}。");
    }

    /// <summary>引擎的設定資料夾：Claude Code 看 CLAUDE_CONFIG_DIR、Codex 看 CODEX_HOME，沒設就是家目錄底下的 .claude、.codex。</summary>
    public static string ConfigDirectory(CliContext context, string kind)
    {
        var variable = kind == EngineKinds.Codex ? "CODEX_HOME" : "CLAUDE_CONFIG_DIR";
        if (context.GetEnvironmentVariable(variable) is { Length: > 0 } configured)
        {
            return Path.GetFullPath(configured, context.WorkingDirectory);
        }

        return Path.Combine(Home(context), kind == EngineKinds.Codex ? ".codex" : ".claude");
    }

    public static SkillLocation Global(CliContext context, string kind) =>
        new(kind, Path.Combine(ConfigDirectory(context, kind), "skills"), "使用者層");

    /// <summary>專案資料夾：Claude Code 讀 .claude/skills，Codex 在 repo 裡讀 .agents/skills。</summary>
    public static SkillLocation Project(string folder, string kind) =>
        new(kind, Path.Combine(folder, kind == EngineKinds.Codex ? ".agents" : ".claude", "skills"), $"專案 {folder}");

    /// <summary>所有預設的使用者層位置（update、status、uninstall 找已經裝過的東西用）。</summary>
    public static IReadOnlyList<SkillLocation> AllGlobal(CliContext context) => [.. Kinds.Select(kind => Global(context, kind))];

    /// <summary>找得到執行檔（KUMI_AGENT_*_PATH 或 PATH）或設定資料夾已經存在的引擎。</summary>
    public static IReadOnlyList<string> Detected(CliContext context) =>
        [.. Kinds.Where(kind => ExecutableLocator.Find(kind, context.GetEnvironmentVariable) is not null
            || Directory.Exists(ConfigDirectory(context, kind)))];

    public static string ResolveProject(CliContext context, string folder)
    {
        var full = Path.GetFullPath(folder, context.WorkingDirectory);
        return Directory.Exists(full) ? full : throw CliException.Usage($"--project 的資料夾 {full} 不存在。");
    }

    /// <summary>Windows 先看 USERPROFILE（HOME 在 Git Bash 之類的環境裡可能是別的意思），其他系統先看 HOME。</summary>
    private static string Home(CliContext context)
    {
        string[] names = OperatingSystem.IsWindows() ? ["USERPROFILE", "HOME"] : ["HOME", "USERPROFILE"];
        foreach (var name in names)
        {
            if (context.GetEnvironmentVariable(name) is { Length: > 0 } value)
            {
                return value;
            }
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}
