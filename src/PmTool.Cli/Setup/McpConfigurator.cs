using System.Text.Json;
using System.Text.RegularExpressions;
using PmTool.Cli.Agent;
using PmTool.Cli.Host;

namespace PmTool.Cli.Setup;

/// <summary>
/// 替 Claude Code／Codex 加上 kumi 這個 MCP 伺服器並登入（internal/HANDOFF-setup-wizard.md 第 4.2 節）。
/// 子程序的參數只有這裡組出來的固定幾個，網址先檢查過字元：Windows 上 codex 常是 npm 的 .cmd，參數會經過 cmd.exe。
/// </summary>
internal sealed partial class McpConfigurator(CliContext context, TextWriter output)
{
    public const string ServerName = "kumi";

    public string McpUrl(string server) => $"{server}/mcp";

    /// <summary>網址只准這些字元：進得了 cmd.exe 的命令列也不會被當成指令。</summary>
    public static bool IsSafeUrl(string url) => SafeUrl().IsMatch(url);

    /// <summary>
    /// 寫進引擎設定的 MCP 網址只收 https 或本機（127.0.0.1、localhost、::1）的 http。
    /// 引擎之後每一次都會帶著授權去連這個網址，而它是寫進 Claude Code／Codex 的設定檔、不再經過 kumi：
    /// 命令列的 --insecure 只放行這一次指令，不能延伸到會留下來的設定，所以這裡不看 --insecure。
    /// </summary>
    public static bool IsAllowedTransport(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));

    internal const string InsecureMcpMessage = "MCP 網址不是 https（也不是本機）：授權會明文經過網路，不寫進 Claude Code／Codex 的設定。請改用 https 的網址，或手動設定。";

    [GeneratedRegex(@"^[A-Za-z0-9.:/\-\[\]]+\z")]
    private static partial Regex SafeUrl();

    /// <summary>已經有名叫 kumi 的 MCP 伺服器嗎？指到哪裡？查不出來（起不來、逾時）當作沒有，後面的 add 會自己說原因。</summary>
    public async Task<ExistingMcp> InspectAsync(DetectedEngine engine, CancellationToken cancellation)
    {
        var arguments = engine.Kind == EngineKinds.Codex ? new[] { "mcp", "list", "--json" } : ["mcp", "get", ServerName];
        var probe = await new EngineProber(context.AgentOptions).ProbeAsync(engine.Executable, arguments, cancellation);
        if (probe is not { ExitCode: 0 })
        {
            return new ExistingMcp(false, null);
        }

        return engine.Kind == EngineKinds.Codex ? ParseCodexList(probe.StandardOutput) : ParseClaudeGet(probe.StandardOutput);
    }

    internal static ExistingMcp ParseClaudeGet(string output)
    {
        // claude mcp get 的輸出有「  URL: https://…/mcp」這一行（Claude Code 2.1.330）。
        var lines = output.Split('\n').Select(line => line.Trim()).ToList();
        var url = lines.FirstOrDefault(line => line.StartsWith("URL:", StringComparison.Ordinal))?["URL:".Length..].Trim();
        // 「Scope: User config (…)」；讀不到範圍時當作使用者層（舊版的輸出），移除失敗會自己說原因。
        var scope = lines.FirstOrDefault(line => line.StartsWith("Scope:", StringComparison.Ordinal))?["Scope:".Length..].Trim();
        return new ExistingMcp(true, string.IsNullOrEmpty(url) ? null : url)
        {
            IsUserScope = scope is null || scope.StartsWith("User", StringComparison.OrdinalIgnoreCase),
        };
    }

    internal static ExistingMcp ParseCodexList(string output)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (entry.TryGetProperty("name", out var name) && name.GetString() == ServerName)
                {
                    var url = entry.TryGetProperty("transport", out var transport) && transport.TryGetProperty("url", out var value)
                        ? value.GetString()
                        : null;
                    var auth = entry.TryGetProperty("auth_status", out var status) && status.ValueKind == JsonValueKind.String ? status.GetString() : null;
                    return new ExistingMcp(true, url) { IsAuthorized = auth is "o_auth" or "bearer_token" };
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            // 讀不懂就當作沒有：後面的 add 如果因為已經存在而失敗，會把原因印出來。
        }

        return new ExistingMcp(false, null);
    }

    public static bool SameUrl(string? left, string? right) =>
        left is not null && right is not null && string.Equals(left.Trim().TrimEnd('/'), right.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>（必要時先移除再）加上 MCP 伺服器。失敗回原因，成功回 null。</summary>
    public async Task<string?> ApplyAsync(DetectedEngine engine, McpAction action, string mcpUrl, CancellationToken cancellation)
    {
        if (action == McpAction.KeepAndLogin)
        {
            return null;
        }

        if (!IsAllowedTransport(mcpUrl))
        {
            return InsecureMcpMessage;
        }

        if (action == McpAction.Replace && await RunCapturedAsync(engine, RemoveArguments(engine), cancellation) is { } removeFailure)
        {
            return removeFailure;
        }

        return engine.Kind == EngineKinds.Codex
            ? await AddCodexAsync(engine, mcpUrl, cancellation)
            : await RunCapturedAsync(engine, AddArguments(engine, mcpUrl), cancellation);
    }

    /// <summary>
    /// Codex 的 <c>mcp add</c> 遇到支援 OAuth 的伺服器會馬上印出授權網址、開瀏覽器、等人按允許（Codex 0.163.0 實測，沒有關掉的選項）。
    /// 所以要繼承終端機跑：捕捉輸出的話使用者看不到網址，而且等授權的時間會被當成逾時。
    /// </summary>
    private async Task<string?> AddCodexAsync(DetectedEngine engine, string mcpUrl, CancellationToken cancellation)
    {
        var arguments = AddArguments(engine, mcpUrl);
        await output.WriteLineAsync($"  執行：{engine.Command} {string.Join(' ', arguments)}　（Codex 加上之後會馬上請你授權：打開它印出的網址，按「允許」）");
        var exit = await InteractiveProcess.RunAsync(engine.Executable, arguments, cancellation);
        return exit switch
        {
            0 => null,
            null => $"{engine.Command} mcp add 起不來。",
            _ => $"{engine.Command} mcp add 失敗（結束代碼 {exit}）。",
        };
    }

    /// <summary>加完（或原本就有）之後還要不要登入：Codex 看授權狀態（add 可能已經登入了），Claude Code 一律要。</summary>
    public async Task<bool> NeedsLoginAsync(DetectedEngine engine, CancellationToken cancellation) =>
        engine.Kind != EngineKinds.Codex || !(await InspectAsync(engine, cancellation)).IsAuthorized;

    /// <summary>
    /// <c>mcp login kumi</c>：繼承終端機，瀏覽器會開 Kumi 的授權頁。失敗只回提醒（不擋後面的步驟），成功回 null。
    /// <paramref name="noBrowser"/>（<c>kumi setup --no-browser</c>）原樣交給引擎：透過 SSH 跑精靈時，兩家的授權頁一樣開不了瀏覽器，
    /// 只能印網址讓人自己打開（Claude Code 2.1.330、Codex 0.163.0 都有這個選項）。
    /// </summary>
    public async Task<string?> LoginAsync(DetectedEngine engine, bool noBrowser, CancellationToken cancellation)
    {
        string[] arguments = noBrowser ? ["mcp", "login", ServerName, "--no-browser"] : ["mcp", "login", ServerName];
        var hint = noBrowser ? "會印出授權網址，打開它按「允許」" : "瀏覽器會開 Kumi 的授權頁，按「允許」";
        await output.WriteLineAsync($"  執行：{engine.Command} {string.Join(' ', arguments)}　（{hint}）");
        var exit = await InteractiveProcess.RunAsync(engine.Executable, arguments, cancellation);
        return exit == 0
            ? null
            : $"沒有完成授權。之後可以在 {engine.DisplayName} 裡輸入 /mcp 選 kumi 登入。";
    }

    /// <summary>移除 kumi 這個 MCP 伺服器（kumi uninstall 用）。失敗回原因，成功回 null。</summary>
    public Task<string?> RemoveAsync(DetectedEngine engine, CancellationToken cancellation) =>
        RunCapturedAsync(engine, RemoveArguments(engine), cancellation);

    private static string[] AddArguments(DetectedEngine engine, string mcpUrl) => engine.Kind == EngineKinds.Codex
        ? ["mcp", "add", ServerName, "--url", mcpUrl]
        : ["mcp", "add", "--scope", "user", "--transport", "http", ServerName, mcpUrl];

    private static string[] RemoveArguments(DetectedEngine engine) => engine.Kind == EngineKinds.Codex
        ? ["mcp", "remove", ServerName]
        : ["mcp", "remove", ServerName, "--scope", "user"];

    private async Task<string?> RunCapturedAsync(DetectedEngine engine, string[] arguments, CancellationToken cancellation)
    {
        await output.WriteLineAsync($"  執行：{engine.Command} {string.Join(' ', arguments)}");
        var probe = await new EngineProber(context.AgentOptions).ProbeAsync(engine.Executable, arguments, cancellation);
        if (probe is null)
        {
            return $"{engine.Command} {arguments[0]} {arguments[1]} 起不來或逾時沒有結束。";
        }

        return probe.ExitCode == 0 ? null : $"{engine.Command} {arguments[0]} {arguments[1]} 失敗（結束代碼 {probe.ExitCode}）：{Tail(probe.Output)}";
    }

    /// <summary>引擎的輸出只留最後一行當原因，不整段倒出來。</summary>
    private static string Tail(string text)
    {
        var line = text.Split('\n').Select(item => item.Trim()).LastOrDefault(item => item.Length > 0) ?? "沒有訊息";
        return line.Length > 200 ? line[..200] : line;
    }
}
