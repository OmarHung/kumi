using PmTool.Cli.Tests.Agent;

namespace PmTool.Cli.Tests.Login;

/// <summary>
/// 假的 claude／codex：記下每次被呼叫的參數、模擬 MCP 設定（url 檔就是「kumi 已經指到哪裡」）。
/// 測試絕不呼叫真的引擎，也不碰使用者的 ~/.claude.json、~/.codex。只能在 Unix 上跑。
/// </summary>
internal sealed class FakeEngines : IDisposable
{
    private readonly FakeEngineScripts _scripts = new();

    public FakeEngines()
    {
        ClaudePath = _scripts.Script("claude", Body("claude", "2.1.330 (Claude Code)", codex: false));
        CodexPath = _scripts.Script("codex", Body("codex", "codex-cli 0.163.0", codex: true));
    }

    public string ClaudePath { get; }

    public string CodexPath { get; }

    /// <summary>引擎被呼叫的每一行參數（一次呼叫一行）。</summary>
    public IReadOnlyList<string> Calls(string engine) =>
        _scripts.Exists($"{engine}-calls.txt") ? _scripts.Read($"{engine}-calls.txt").Split('\n', StringSplitOptions.RemoveEmptyEntries) : [];

    /// <summary>設定引擎裡 kumi 已經指到哪個 MCP 網址（沒呼叫過 add 就先有了）。</summary>
    public void SetExisting(string engine, string url) => File.WriteAllText(Path.Combine(_scripts.Out, $"{engine}-url"), url);

    public string? ExistingUrl(string engine) =>
        _scripts.Exists($"{engine}-url") ? _scripts.Read($"{engine}-url") : null;

    /// <summary>
    /// Codex 的 <c>mcp add</c> 遇到支援 OAuth 的伺服器會馬上登入（Codex 0.163.0 實測）：true 時 add 完授權狀態就是 o_auth。
    /// 預設 false（add 只寫設定、授權狀態是 not_logged_in），後面的 <c>mcp login</c> 才登入。
    /// </summary>
    public void SetAddLogsIn(string engine) => File.WriteAllText(Path.Combine(_scripts.Out, $"{engine}-add-logs-in"), "1");

    /// <summary>設定 Codex 裡 kumi 目前的授權狀態（<c>codex mcp list --json</c> 的 auth_status）。</summary>
    public void SetAuthStatus(string engine, string status) => File.WriteAllText(Path.Combine(_scripts.Out, $"{engine}-auth"), status);

    /// <summary><c>mcp login kumi</c> 的結束代碼。</summary>
    public void SetLoginExit(string engine, int exitCode) => File.WriteAllText(Path.Combine(_scripts.Out, $"{engine}-login-exit"), exitCode.ToString());

    public void Dispose() => _scripts.Dispose();

    private string Body(string engine, string version, bool codex)
    {
        var o = _scripts.Out;
        var query = codex
            ? $$"""
                if [ -f "{{o}}/{{engine}}-url" ]; then
                  printf '[{"name":"kumi","transport":{"type":"streamable_http","url":"%s"},"auth_status":"%s"}]\n' "$(cat "{{o}}/{{engine}}-url")" "$(cat "{{o}}/{{engine}}-auth" 2>/dev/null || echo not_logged_in)"
                else echo '[]'; fi
                """
            : $$"""
                if [ -f "{{o}}/{{engine}}-url" ]; then
                  printf 'kumi:\n  Scope: User config (available in all your projects)\n  Type: http\n  URL: %s\n' "$(cat "{{o}}/{{engine}}-url")"
                else echo "No MCP server found with name: kumi" >&2; exit 1; fi
                """;
        return $$"""
            echo "$*" >> "{{o}}/{{engine}}-calls.txt"
            case "$1" in
              --version) echo "{{version}}" ;;
              mcp)
                case "$2" in
                  get|list) {{query}} ;;
                  add) for last; do :; done; printf '%s' "$last" > "{{o}}/{{engine}}-url"
                       if [ -f "{{o}}/{{engine}}-add-logs-in" ]; then echo o_auth > "{{o}}/{{engine}}-auth"; else echo not_logged_in > "{{o}}/{{engine}}-auth"; fi ;;
                  remove) rm -f "{{o}}/{{engine}}-url" "{{o}}/{{engine}}-auth" ;;
                  login) code="$(cat "{{o}}/{{engine}}-login-exit" 2>/dev/null || echo 0)"
                         if [ "$code" = 0 ]; then echo o_auth > "{{o}}/{{engine}}-auth"; fi
                         exit "$code" ;;
                esac ;;
            esac
            """;
    }
}
