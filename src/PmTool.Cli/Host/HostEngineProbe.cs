using System.Text.Json.Nodes;
using PmTool.Cli.Agent;
using PmTool.Cli.Processes;

namespace PmTool.Cli.Host;

/// <summary>一個引擎這一次探測的結果。<see cref="Spec"/> 與 <see cref="Executable"/> 只有 <see cref="Installed"/> 且 <see cref="Supported"/> 時有。</summary>
internal sealed record ProbedEngine(
    string Kind,
    bool Installed,
    string? Version,
    bool Supported,
    bool LoggedIn,
    string Billing,
    string? Fingerprint,
    IReadOnlyList<EngineModel> Models,
    string? Problem,
    string? Executable = null,
    IEngineSpec? Spec = null)
{
    public bool IsRunnable => Installed && Supported && Executable is not null && Spec is not null;

    /// <summary>登記時的內容（<c>HostEngineReport</c>）；<paramref name="loginId"/> 是 Kumi 的登入編號（不是這份登入的帳號資料）。</summary>
    public JsonObject ToReport(Guid? loginId) => new()
    {
        ["kind"] = Kind,
        ["installed"] = Installed,
        ["version"] = Version,
        ["supported"] = Supported,
        ["loggedIn"] = LoggedIn,
        ["billing"] = Billing,
        ["loginId"] = loginId,
        ["models"] = new JsonArray([.. Models.Select(model => (JsonNode?)model.ToJson())]),
        ["problem"] = Problem,
    };

    public static ProbedEngine NotInstalled(string kind, string? problem = null) => new(
        kind, false, null, false, false, Host.Billing.Unknown, null, [],
        problem ?? $"映像檔裡沒有 {EngineKinds.DisplayName(kind)}。請使用 Kumi 提供的 ai-worker 映像檔。");
}

/// <summary>
/// 探測 Claude Code 與 Codex：裝了沒有、版本與安全用的旗標、登入了沒有、計費方式。只跑不送提示的指令，
/// <b>不送模型請求</b>（不會用掉任何額度）；輸出裡的 email、組織名稱、金鑰的片段不留下。
/// </summary>
internal sealed class HostEngineProbe(Func<string, string?> environment, AgentOptions options, Action<string>? log = null)
{
    private readonly EngineProber _prober = new(options);

    public async Task<ProbedEngine> ProbeAsync(string kind, CancellationToken cancellation) =>
        kind == EngineKinds.Codex ? await ProbeCodexAsync(cancellation) : await ProbeClaudeAsync(cancellation);

    /// <summary>
    /// 每件工作開始前的輕量確認：只跑登入狀態的指令（不送模型請求），回這一刻的登入、計費與指紋；沒裝是 null。
    /// 兩次完整探測之間（最多 5 分鐘）有人在 Kumi 以外換了登入，靠它在跑之前發現。
    /// </summary>
    public async Task<LoginStatus?> CheckLoginAsync(string kind, CancellationToken cancellation)
    {
        if (ExecutableLocator.Find(kind, environment) is not { } executable)
        {
            return null;
        }

        return kind == EngineKinds.Codex ? await CodexLoginAsync(executable, cancellation) : await ClaudeLoginAsync(executable, cancellation);
    }

    private async Task<LoginStatus> ClaudeLoginAsync(string executable, CancellationToken cancellation) =>
        await _prober.ProbeAsync(executable, ["auth", "status"], cancellation) is { } probe
            ? BillingClassifier.Claude(probe.ExitCode, probe.StandardOutput)
            : LoginStatus.LoggedOut;

    private async Task<LoginStatus> CodexLoginAsync(string executable, CancellationToken cancellation)
    {
        var status = await _prober.ProbeAsync(executable, ["login", "status"], cancellation) is { } probe
            ? BillingClassifier.Codex(probe.ExitCode, probe.Output)
            : LoginStatus.LoggedOut;
        return status.LoggedIn ? status with { Fingerprint = CodexFingerprint() } : status;
    }

    /// <summary>執行 CLI 自己的登出；成功（結束代碼 0）回 true。沒裝或起不來回 false。</summary>
    public async Task<bool> LogoutAsync(string kind, CancellationToken cancellation)
    {
        if (ExecutableLocator.Find(kind, environment) is not { } executable)
        {
            return false;
        }

        string[] arguments = kind == EngineKinds.Codex ? ["logout"] : ["auth", "logout"];
        return await _prober.ProbeAsync(executable, arguments, cancellation) is { ExitCode: 0 };
    }

    private async Task<ProbedEngine> ProbeClaudeAsync(CancellationToken cancellation)
    {
        const string kind = EngineKinds.ClaudeCode;
        var name = EngineKinds.DisplayName(kind);
        if (ExecutableLocator.Find(kind, environment) is not { } executable)
        {
            return ProbedEngine.NotInstalled(kind);
        }

        if (ExecutableLocator.IsBatchScript(executable)
            || await _prober.ProbeAsync(executable, ["--version"], cancellation) is not { ExitCode: 0 } version)
        {
            return Unsupported(kind, null, $"{name} 跑不起來（--version 失敗）。請重建映像檔。");
        }

        var versionText = EngineProber.FirstToken(version.StandardOutput);
        if (await _prober.ProbeAsync(executable, ["--help"], cancellation) is not { } help
            || EngineProber.ClaudeRequiredFlags.Any(flag => !HelpFlags.Contains(help.Output, flag)))
        {
            return Unsupported(kind, versionText, $"這個版本的 {name} 太舊或缺少安全用的選項，為了安全不接工作。請更新映像檔裡的 {name}。");
        }

        var status = await ClaudeLoginAsync(executable, cancellation);
        // 模型清單固定：別名（opus、sonnet、haiku）由 Claude Code 自己對到最新的版本；不為了探別名送任何請求。
        return Finish(kind, versionText, status, ClaudeCodeSpec.Models, executable, new ClaudeCodeSpec());
    }

    private async Task<ProbedEngine> ProbeCodexAsync(CancellationToken cancellation)
    {
        const string kind = EngineKinds.Codex;
        var name = EngineKinds.DisplayName(kind);
        if (ExecutableLocator.Find(kind, environment) is not { } executable)
        {
            return ProbedEngine.NotInstalled(kind);
        }

        if (ExecutableLocator.IsBatchScript(executable)
            || await _prober.ProbeAsync(executable, ["--version"], cancellation) is not { ExitCode: 0 } version)
        {
            return Unsupported(kind, null, $"{name} 跑不起來（--version 失敗）。請重建映像檔。");
        }

        var versionText = EngineProber.LastToken(version.StandardOutput);
        if (await _prober.ProbeAsync(executable, ["exec", "--help"], cancellation) is not { } help
            || EngineProber.CodexRequiredFlags.Any(flag => !HelpFlags.Contains(help.Output, flag)))
        {
            return Unsupported(kind, versionText, $"這個版本的 {name} 太舊，缺少安全用的選項，為了安全不接工作。請更新映像檔裡的 {name}。");
        }

        var features = await _prober.ProbeAsync(executable, ["features", "list"], cancellation);
        var listed = features is { ExitCode: 0 } ? EngineProber.ParseFeatureNames(features.Output) : [];
        // 關不掉執行指令的工具，模型就能碰到主機：寧可不接工作。
        if (!listed.Contains("shell_tool"))
        {
            return Unsupported(kind, versionText, $"這個版本的 {name} 沒辦法關掉執行指令的工具，為了安全不接工作。請更新映像檔裡的 {name}。");
        }

        var status = await CodexLoginAsync(executable, cancellation);
        var disabled = CodexSpec.DisableCandidates.Where(listed.Contains).ToArray();
        return Finish(kind, versionText, status, CodexModels.Read(environment, log), executable, new CodexSpec(disabled));
    }

    /// <summary>
    /// Codex 的登入存在 <c>auth.json</c>。指紋只看代表「哪個帳號」的欄位（登入方式、API 金鑰、ChatGPT 的 account_id），
    /// 不雜湊整個檔案：Codex 刷新權杖時會改寫它，整檔雜湊會讓同一份登入被誤判成換了人。只算雜湊、不留下內容。
    /// 檔案沒有或讀不懂時回 null：沒有指紋就不比對。
    /// </summary>
    private string? CodexFingerprint()
    {
        var home = environment("CODEX_HOME") is { Length: > 0 } configured
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        // 家目錄可能被別的程式寫入：只讀一般檔案、不跟隨連結、最多 1 MiB；讀不到當作沒有（沒有指紋就不比對）。
        if (BoundedFile.TryReadBytes(Path.Combine(home, "auth.json"), log) is not { } bytes)
        {
            return null;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(bytes);
            return BillingClassifier.CodexFingerprint(document.RootElement);
        }
        catch (System.Text.Json.JsonException)
        {
            log?.Invoke("auth.json 讀不懂，當作沒有指紋。");
            return null;
        }
    }

    private static ProbedEngine Unsupported(string kind, string? version, string problem) =>
        new(kind, true, version, false, false, Billing.Unknown, null, [], problem);

    private static ProbedEngine Finish(
        string kind, string version, LoginStatus status, IReadOnlyList<EngineModel> models, string executable, IEngineSpec spec) =>
        new(kind, true, version, true, status.LoggedIn, status.Billing, status.Fingerprint, models, ProblemOf(kind, status), executable, spec);

    /// <summary>給擁有者看的說明：只在「能做什麼來解決」時才有。</summary>
    internal static string? ProblemOf(string kind, LoginStatus status)
    {
        var name = EngineKinds.DisplayName(kind);
        var command = $"docker compose exec -it ai-worker kumi host login {(kind == EngineKinds.Codex ? "codex" : "claude-code")}";
        var wayToLogIn = kind == EngineKinds.Codex ? "OpenAI API 金鑰" : "Anthropic Console（API 用量計費）";
        if (!status.LoggedIn)
        {
            return $"{name} 還沒登入：先在網頁按「開始登入」取得登入碼，再在主機上執行 {command}。";
        }

        return status.Billing switch
        {
            Billing.Subscription => $"{name} 登入的是訂閱帳號，依供應商條款不能給全組織共用。請登出，改用 {wayToLogIn} 登入。",
            Billing.Unknown => $"判斷不出 {name} 的計費方式，當作不能共用。請登出，改用 {wayToLogIn} 登入。",
            _ => null,
        };
    }
}
