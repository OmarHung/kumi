namespace PmTool.Cli.Agent;

/// <summary>找引擎的執行檔：環境變數指定的路徑優先（給測試與裝在特別位置的人），沒有才依 PATH 找。</summary>
internal static class ExecutableLocator
{
    public const string ClaudeVariable = "KUMI_AGENT_CLAUDE_PATH";
    public const string CodexVariable = "KUMI_AGENT_CODEX_PATH";

    public static string VariableFor(string kind) => kind == EngineKinds.Codex ? CodexVariable : ClaudeVariable;

    public static string CommandFor(string kind) => kind == EngineKinds.Codex ? "codex" : "claude";

    public static string? Find(string kind, Func<string, string?> environment)
    {
        if (environment(VariableFor(kind)) is { Length: > 0 } configured)
        {
            return File.Exists(configured) ? Path.GetFullPath(configured) : null;
        }

        return FindOnPath(CommandFor(kind), environment("PATH"));
    }

    internal static string? FindOnPath(string command, string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        // .exe 優先；找到 .cmd／.bat 也回傳，由呼叫端說明為什麼不能用（見 IsBatchScript），不要默默說「找不到」。
        var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", ".bat" } : [string.Empty];
        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            // 相對的項目（例如 . 或空的）會讓目前資料夾裡的同名檔案搶先被執行。
            if (!Path.IsPathFullyQualified(entry))
            {
                continue;
            }

            var directory = Path.GetFullPath(entry);
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, command + extension);
                if (File.Exists(candidate) && IsExecutable(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// .cmd 與 .bat 要經過 cmd.exe，命令列的跳脫規則和 .NET 的不同：系統提示裡的 <c>"</c> 加 <c>&amp; | &lt; &gt; ^ %</c> 會變成命令注入。
    /// 所以這樣的引擎不用。
    /// </summary>
    public static bool IsBatchScript(string path) =>
        Path.GetExtension(path).Equals(".cmd", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".bat", StringComparison.OrdinalIgnoreCase);

    private static bool IsExecutable(string file) =>
        OperatingSystem.IsWindows()
        || (File.GetUnixFileMode(file) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
}
