using System.Net;
using System.Text.Json.Nodes;
using PmTool.Cli.Agent;
using PmTool.Cli.Host;
using PmTool.Cli.Tests.Agent;

namespace PmTool.Cli.Tests.Host;

/// <summary>主機 worker 的測試用具：假的 claude／codex 指令稿、狀態檔資料夾、環境變數、會記下等待的 Delay。</summary>
internal sealed class HostTestKit : IDisposable
{
    public const string Credential = "kha_example_machine_credential_value";
    public const string ToolToken = "kjt_example_job_tool_token_value";
    public const string ApiAuth = """{"loggedIn": true, "authMethod": "api_key", "apiKeySource": "/login managed key", "email": "someone@example.com", "orgName": "Example Org"}""";
    public const string SubscriptionAuth = """{"loggedIn": true, "authMethod": "claude.ai", "email": "someone@example.com", "subscriptionType": "max"}""";
    public const string LoggedOutAuth = """{"loggedIn": false}""";

    private static readonly TimeSpan RealDrain = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RealExitGrace = TimeSpan.FromSeconds(6);

    private readonly List<TimeSpan> _waits = [];

    public HostTestKit(CliHarness cli)
    {
        Cli = cli;
        Scripts = new FakeEngineScripts();
        StateDirectory = cli.PathOf("host-state");
        Store = new HostStateStore(StateDirectory);
        cli.Environment[HostStateStore.DirectoryVariable] = StateDirectory;
        cli.Environment[ExecutableLocator.CodexVariable] = Path.Combine(Scripts.Directory, "no-codex");
        cli.Environment[ExecutableLocator.ClaudeVariable] = Path.Combine(Scripts.Directory, "no-claude");
        cli.HostOptions = new HostOptions();
        cli.AgentOptions = new AgentOptions
        {
            MinPollGap = TimeSpan.FromMilliseconds(30),
            FlushInterval = TimeSpan.FromMilliseconds(20),
            HeartbeatInterval = TimeSpan.FromHours(1),
            FinishTimeout = TimeSpan.FromSeconds(10),
            ProbeTimeout = TimeSpan.FromSeconds(10),
            // 子程序的收尾要真的等（別的等待都縮短）：用不會和退避、輪詢撞在一起的數字認出它們。
            DrainTimeout = RealDrain,
            ExitGrace = RealExitGrace,
            // 退避與長等待只記下要等多久，不真的等；短的照常。
            Delay = async (duration, cancellation) =>
            {
                lock (_waits)
                {
                    _waits.Add(duration);
                }

                var hook = OnWait;
                hook?.Invoke(duration);
                var isProcessWait = duration == RealDrain || duration == RealExitGrace;
                await Task.Delay(duration >= TimeSpan.FromSeconds(1) && !isProcessWait ? TimeSpan.FromMilliseconds(5) : duration, cancellation);
            },
        };
    }

    public CliHarness Cli { get; }

    public FakeEngineScripts Scripts { get; }

    public string StateDirectory { get; }

    public HostStateStore Store { get; }

    /// <summary>每次等待時呼叫（在等之前）。</summary>
    public Action<TimeSpan>? OnWait { get; set; }

    public IReadOnlyList<TimeSpan> Waits
    {
        get
        {
            lock (_waits)
            {
                return [.. _waits];
            }
        }
    }

    public string AuthFile => Path.Combine(Scripts.Out, "auth.json");

    public void SetAuth(string json) => File.WriteAllText(AuthFile, json);

    /// <summary>假的 claude：版本、help、登入狀態（讀 auth.json）、登入（把 auth-after-login.json 變成登入狀態）、登出；其他都當作跑工作。</summary>
    public string InstallFakeClaude(string authJson = ApiAuth, string? afterLogin = null, string fixture = "claude-success.jsonl", string beforeRun = "")
    {
        SetAuth(authJson);
        File.WriteAllText(Path.Combine(Scripts.Out, "auth-after-login.json"), afterLogin ?? authJson);
        var path = Scripts.Script("claude", $$"""
            OUT="{{Scripts.Out}}"
            case "$1 $2" in
              "--version ") echo "2.1.330 (Claude Code)"; exit 0 ;;
              "--help ") echo "{{EngineDiscoveryTests.ClaudeHelp}}"; exit 0 ;;
              "auth status") cat "$OUT/auth.json"; if grep -q '"loggedIn": true' "$OUT/auth.json"; then exit 0; else exit 1; fi ;;
              "auth logout") echo '{"loggedIn": false}' > "$OUT/auth.json"; echo called >> "$OUT/logout-called"; exit 0 ;;
              "auth login") printf '%s\n' "$@" > "$OUT/login-args"; cp "$OUT/auth-after-login.json" "$OUT/auth.json"; exit 0 ;;
            esac
            cat > "$OUT/stdin.txt"
            printf '%s\n' "$@" > "$OUT/args.txt"
            env | sort > "$OUT/env.txt"
            cp mcp.json "$OUT/mcp.json" 2>/dev/null
            {{beforeRun}}
            cat "{{FakeEngineScripts.FixturePath(fixture)}}"
            """);
        Cli.Environment[ExecutableLocator.ClaudeVariable] = path;
        return path;
    }

    public string InstallFakeCodex(string statusText = "Logged in using an API key - sk-***", int statusExit = 0)
    {
        var path = Scripts.Script("codex", $$"""
            OUT="{{Scripts.Out}}"
            case "$1 $2" in
              "--version ") echo "codex-cli 0.163.0"; exit 0 ;;
              "login status") echo "{{statusText}}" >&2; exit {{statusExit}} ;;
              "exec --help") echo "--strict-config --ignore-user-config --ignore-rules --ephemeral --json"; exit 0 ;;
              "features list") echo 'shell_tool stable true'; exit 0 ;;
              "login --with-api-key") cat > /dev/null; echo called > "$OUT/codex-login-called"; exit 0 ;;
              "logout ") echo called >> "$OUT/codex-logout-called"; exit 0 ;;
            esac
            exit 9
            """);
        Cli.Environment[ExecutableLocator.CodexVariable] = path;
        return path;
    }

    public HostState Pair(string server = CliHarness.ServerUrl, Guid? loginId = null, string? fingerprint = null)
    {
        var agentId = Guid.Parse("0190a000-0000-7000-8000-0000000000aa");
        return Store.Update(state =>
        {
            var paired = state with
            {
                Server = server, AgentId = agentId, AgentName = "主機一號", MachineKey = "machine-key-0001", Credential = Credential,
            };
            return loginId is { } id ? paired.WithLogin(EngineKinds.ClaudeCode, new HostLoginRecord(id, fingerprint)) : paired;
        });
    }

    public static JsonObject HostJobWire(Guid? jobId = null, int lease = 7, string engine = EngineKinds.ClaudeCode)
    {
        var wire = TestJobs.ToWire(TestJobs.Make(engine, id: jobId));
        wire["lease"] = lease;
        wire["toolToken"] = ToolToken;
        wire["toolTokenExpiresAt"] = DateTimeOffset.UtcNow.AddMinutes(6);
        return wire;
    }

    public static HttpResponseMessage CheckInResponse(JsonObject? job = null, string[]? logout = null, bool disabled = false) =>
        FakeServer.Envelope(HttpStatusCode.OK, new { job = (object?)job, logout = logout ?? [], disabled });

    public static HttpResponseMessage Failure(HttpStatusCode status, string code, string message) => FakeAgentServer.Failure(status, code, message);

    /// <summary>每個登記的內容；路徑是 /v1/host-agent/check-in。</summary>
    public IReadOnlyList<RecordedRequest> CheckIns => Cli.Server.To("POST", "/v1/host-agent/check-in");

    public void Dispose()
    {
        Scripts.Dispose();
    }
}
