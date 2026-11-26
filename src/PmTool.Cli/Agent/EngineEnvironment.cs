namespace PmTool.Cli.Agent;

/// <summary>
/// 引擎子程序的環境：白名單，只留它跑得起來、登入得了需要的變數。以前是「拿掉清單」，清單外的東西（雲端的金鑰、
/// 資料庫的連線字串、別的服務的權杖）都會原樣交給引擎，也就交給它啟動的任何東西；現在新的變數預設進不去。
/// </summary>
internal static class EngineEnvironment
{
    /// <summary>
    /// 使用者自己加的放行名單（逗號分隔的變數名稱）：公司的代理或憑證用了非標準的變數名稱、或測試用的假引擎需要額外的變數時用。
    /// 是使用者自己環境裡的設定，不是伺服器送來的，所以可以信。
    /// </summary>
    public const string PassthroughVariable = "KUMI_ENGINE_PASSTHROUGH_ENV";

    /// <summary>引擎本身（node 的指令稿）找得到 node、找得到自己的設定與登入、連得上網路需要的。</summary>
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        // 程式與使用者：node 指令稿的 shebang 靠 PATH 找 node；登入與設定放在 HOME 底下。
        "PATH", "HOME", "USER", "LOGNAME", "SHELL",
        // 語系、終端機、時區、暫存資料夾。
        "LANG", "LANGUAGE", "TERM", "COLORTERM", "NO_COLOR", "TZ", "TMPDIR", "TMP", "TEMP",
        // 代理與憑證：公司網路裡連不出去就是少了這些。
        "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY", "SSL_CERT_FILE", "SSL_CERT_DIR", "NODE_EXTRA_CA_CERTS",
        // 設定資料夾：引擎的登入存在這些位置，指到別處的人要能照樣登入。
        "XDG_CONFIG_HOME", "XDG_DATA_HOME", "XDG_CACHE_HOME", "XDG_STATE_HOME", "XDG_RUNTIME_DIR", "CLAUDE_CONFIG_DIR", "CODEX_HOME",
        // 不要讓引擎自己更新（容器與本機都一樣：換版本要經過我們驗過的範圍）。
        "DISABLE_AUTOUPDATER",
        // Windows：沒有 SystemRoot 之類的，連網路與 DLL 都載入不了。
        "USERPROFILE", "USERNAME", "APPDATA", "LOCALAPPDATA", "HOMEDRIVE", "HOMEPATH", "SYSTEMROOT", "SYSTEMDRIVE", "WINDIR",
        "COMSPEC", "PATHEXT", "PROGRAMDATA", "PROGRAMFILES", "PROGRAMFILES(X86)", "PROGRAMW6432", "COMMONPROGRAMFILES", "OS", "PROCESSOR_ARCHITECTURE",
    };

    /// <summary>登入時要開瀏覽器、接終端機：只有繼承終端機的互動式子程序加上這些。</summary>
    private static readonly HashSet<string> InteractiveExtras = new(StringComparer.OrdinalIgnoreCase)
    {
        "DISPLAY", "WAYLAND_DISPLAY", "BROWSER", "DBUS_SESSION_BUS_ADDRESS", "SSH_TTY", "COLUMNS", "LINES",
    };

    private const string LocalePrefix = "LC_";

    /// <summary>
    /// 從 <paramref name="source"/>（通常是目前程序的環境）挑出放行的變數，再加上 <paramref name="additions"/>（例如 KUMI_MCP_TOKEN）。
    /// 變數名稱的比對不分大小寫：Windows 的環境本來就不分，Unix 上多放行一個大小寫不同的同名變數沒有風險。
    /// </summary>
    public static IReadOnlyDictionary<string, string> Build(
        IEnumerable<KeyValuePair<string, string?>> source, IReadOnlyDictionary<string, string> additions, bool interactive = false)
    {
        var pairs = source.Where(pair => pair.Value is not null).ToArray();
        var extra = ExtraNames(pairs);
        // 結果的鍵分大小寫：Unix 上 HTTPS_PROXY 與 https_proxy 是兩個變數，值可以不同，不能併成一個。
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in pairs)
        {
            if (IsAllowed(name, interactive) || extra.Contains(name))
            {
                result[name] = value!;
            }
        }

        foreach (var (name, value) in additions)
        {
            result[name] = value;
        }

        return result;
    }

    /// <summary>把子程序的環境換成白名單的結果（先清空再放進去）。</summary>
    public static void Apply(IDictionary<string, string?> target, IReadOnlyDictionary<string, string> additions, bool interactive = false)
    {
        var built = Build(target, additions, interactive);
        target.Clear();
        foreach (var (name, value) in built)
        {
            target[name] = value;
        }
    }

    internal static bool IsAllowed(string name, bool interactive) =>
        Allowed.Contains(name)
        || name.StartsWith(LocalePrefix, StringComparison.OrdinalIgnoreCase)
        || (interactive && InteractiveExtras.Contains(name));

    private static HashSet<string> ExtraNames(IEnumerable<KeyValuePair<string, string?>> pairs)
    {
        var configured = pairs.FirstOrDefault(pair => string.Equals(pair.Key, PassthroughVariable, StringComparison.OrdinalIgnoreCase)).Value;
        return new HashSet<string>(
            (configured ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(name => !IsReserved(name)),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// kumi 自己的權杖與 MCP 設定：不論放行名單怎麼寫都不從使用者的環境帶進引擎（引擎要的 MCP 變數由 kumi 自己在 additions 設）。
    /// 放行 ANTHROPIC_API_KEY、OPENAI_API_KEY 這類金鑰是使用者的選擇，但引擎會改走 API 計費，見 docs/CLI.md。
    /// </summary>
    internal static bool IsReserved(string name) =>
        name.Equals("KUMI_TOKEN", StringComparison.OrdinalIgnoreCase)
        || name.Equals("PM_TOKEN", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("KUMI_MCP_", StringComparison.OrdinalIgnoreCase);
}
