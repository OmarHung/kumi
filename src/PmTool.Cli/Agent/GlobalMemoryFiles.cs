namespace PmTool.Cli.Agent;

/// <summary>
/// 你自己的全域記憶檔（給引擎的長期指示）會不會被帶進本機代理的對話。2026-11-26 在 Claude Code 2.1.332、Codex 0.163.0 實測：
/// Claude Code 用 <c>--setting-sources project</c> 之後不載入使用者層級的 <c>CLAUDE.md</c>（沒帶這個旗標時會載入，所以旗標有用）；
/// Codex 的 <c>$CODEX_HOME/AGENTS.md</c> 沒有旗標或設定鍵關得掉（<c>project_doc_max_bytes=0</c> 只管專案裡的，<c>--ignore-user-config</c>
/// 也不管它），照樣進到模型。換掉 <c>CODEX_HOME</c> 會讓 Codex 讀不到登入，也不能為此複製憑證，所以只能在啟動時明講。
/// </summary>
internal static class GlobalMemoryFiles
{
    private static readonly string[] CodexFileNames = ["AGENTS.override.md", "AGENTS.md"];

    /// <summary>每個會進到模型的檔案一行說明；沒有就是空的。</summary>
    public static IReadOnlyList<string> Describe(IReadOnlyList<IAgentEngine> engines, Func<string, string?> environment)
    {
        if (!engines.Any(engine => engine.Kind == EngineKinds.Codex))
        {
            return [];
        }

        var home = environment("CODEX_HOME") is { Length: > 0 } configured
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        List<string> notes = [];
        foreach (var name in CodexFileNames)
        {
            var path = Path.Combine(home, name);
            if (SizeOf(path) is { } size and > 0)
            {
                notes.Add($"注意：Codex 會把 {path}（{size} 位元組）帶進每一件工作的提示，目前沒有旗標能關掉它。檔案裡不要放機密，也不要放會改變 Kumi 助理做法的指示。");
            }
        }

        return notes;
    }

    private static long? SizeOf(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
