using System.Net;
using System.Text.Json.Nodes;
using PmTool.Cli.Login;

namespace PmTool.Cli.Tests;

/// <summary>
/// 假的瀏覽器：收到網址之後直接打 127.0.0.1 的迴路，不真的開瀏覽器。
/// <see cref="Behavior"/> 決定這一次是按「允許」、按「拒絕」、先打一個舊的 state、還是什麼都不做。
/// </summary>
internal sealed class FakeBrowser
{
    private static readonly HttpClient Loopback = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly List<string> _urls = [];

    public const string Code = "example-code-1";

    public Func<Uri, Task> Behavior { get; set; } = Approve;

    /// <summary>每個瀏覽器收到的網址（登入幾次就有幾個）。</summary>
    public IReadOnlyList<string> Urls
    {
        get
        {
            lock (_urls)
            {
                return [.. _urls];
            }
        }
    }

    /// <summary>--no-browser 時網址是測試自己從輸出讀出來的，不是瀏覽器收到的：記在這裡，假的伺服器才對得上。</summary>
    public string? ManualUrl { get; set; }

    /// <summary>最後一個授權網址的查詢參數；沒有就是 null。</summary>
    public string? Query(string name)
    {
        var last = Urls.LastOrDefault() ?? ManualUrl;
        return last is null ? null : System.Web.HttpUtility.ParseQueryString(new Uri(last).Query)[name];
    }

    public bool Open(string url)
    {
        lock (_urls)
        {
            _urls.Add(url);
        }

        var uri = new Uri(url);
        // 回呼要等 kumi 開始接受連線之後才打得進去；放到別的執行緒，OpenBrowser 才能馬上回傳。
        _ = Task.Run(() => Behavior(uri));
        return true;
    }

    public static Task Approve(Uri authorize) => Hit(authorize, $"code={FakeBrowser.Code}&state={State(authorize)}");

    public static Task Deny(Uri authorize) => Hit(authorize, $"error=access_denied&state={State(authorize)}");

    public static async Task StaleThenApprove(Uri authorize)
    {
        await Hit(authorize, $"code=old&state=stale-state-from-an-older-login");
        await Approve(authorize);
    }

    public static Task DoNothing(Uri authorize) => Task.CompletedTask;

    public static string State(Uri authorize) => System.Web.HttpUtility.ParseQueryString(authorize.Query)["state"]!;

    public static int Port(Uri authorize) => int.Parse(System.Web.HttpUtility.ParseQueryString(authorize.Query)["port"]!, System.Globalization.CultureInfo.InvariantCulture);

    public static async Task<string> Hit(Uri authorize, string query)
    {
        using var response = await Loopback.GetAsync($"http://127.0.0.1:{Port(authorize)}{LoopbackReceiverPath}?{query}");
        return await response.Content.ReadAsStringAsync();
    }

    private const string LoopbackReceiverPath = "/kumi-login";
}

/// <summary>
/// 登入與精靈的測試環境：在 <see cref="CliHarness"/> 之上換上假的瀏覽器、短的逾時、記錄下來的子指令，
/// 並登記一個假的 <c>POST /v1/cli-login/token</c>，檢查 PKCE 的 verifier 對得上瀏覽器收到的 challenge。
/// </summary>
internal sealed class LoginKit : IDisposable
{
    public const string CliSecret = "pmt_example_cli_secret";
    public const string AgentSecret = "pmt_example_agent_secret";

    private readonly List<JsonNode> _redeems = [];

    public LoginKit() => Browser = new FakeBrowser();

    public CliHarness Cli { get; } = new();

    public FakeBrowser Browser { get; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(20);

    public string MachineName { get; set; } = "MacBook-Pro";

    public bool AutostartSupported { get; set; } = true;

    /// <summary>精靈呼叫的子指令（kumi agent enable／start）收到的參數；它們的結束代碼由 <see cref="SubCommandExit"/> 決定。</summary>
    public List<string[]> SubCommands { get; } = [];

    public Func<string[], int> SubCommandExit { get; set; } = _ => 0;

    /// <summary>兌換回應：預設兩把都有。</summary>
    public bool ReturnCli { get; set; } = true;

    public bool ReturnAgent { get; set; } = true;

    public bool AgentSkipped { get; set; }

    /// <summary>伺服器發的本機代理權杖的範圍；預設是固定的那一組。</summary>
    public string[] AgentScopes { get; set; } = ["agent:run", "blueprint:read", "blueprint:write", "items:read"];

    public IReadOnlyList<JsonNode> Redeems
    {
        get
        {
            lock (_redeems)
            {
                return [.. _redeems];
            }
        }
    }

    public StringWriter ErrorWriter { get; } = new();

    public StringWriter OutWriter { get; } = new();

    public LoginKit WithRedeemEndpoint()
    {
        Cli.Server.On("POST", "/v1/cli-login/token", request =>
        {
            var body = request.Json!;
            lock (_redeems)
            {
                _redeems.Add(body);
            }

            var expected = Pkce.Challenge(body["codeVerifier"]!.GetValue<string>());
            if (body["code"]!.GetValue<string>() != FakeBrowser.Code || expected != Browser.Query("challenge"))
            {
                return Failure(HttpStatusCode.UnprocessableEntity, "cli_login.code_invalid", "登入已經過期，請重新執行登入。");
            }

            // 和真的伺服器一樣：只回請求裡要的那幾把。
            var requested = (Browser.Query("tokens") ?? string.Empty).Split(',');
            var wantsCli = requested.Contains("cli") && ReturnCli;
            var wantsAgent = requested.Contains("agent");
            return FakeServer.Envelope(HttpStatusCode.OK, new
            {
                account = new { displayName = "王小明", email = "ming@example.com", organizationName = "範例公司" },
                cli = wantsCli ? new { token = new { id = "t1" }, secret = CliSecret } : null,
                agent = wantsAgent && ReturnAgent && !AgentSkipped ? new { token = new { id = "t2", scopes = AgentScopes }, secret = AgentSecret } : null,
                agentSkipped = wantsAgent && AgentSkipped,
            });
        });
        return this;
    }

    public static HttpResponseMessage Failure(HttpStatusCode status, string code, string message) =>
        new(status)
        {
            Content = new StringContent(
                new JsonObject { ["success"] = false, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } }.ToJsonString(),
                System.Text.Encoding.UTF8, "application/json"),
        };

    public async Task<CliResult> RunAsync(params string[] args)
    {
        using var http = new HttpClient(Cli.Server, disposeHandler: false);
        var context = new CliContext
        {
            Http = http,
            Out = OutWriter,
            Error = ErrorWriter,
            In = new StringReader(Cli.StandardInput),
            ConfigPath = Cli.ConfigPath,
            WorkingDirectory = Cli.WorkingDirectory,
            GetEnvironmentVariable = name => Cli.Environment.GetValueOrDefault(name),
            IsInteractive = Cli.IsInteractive,
            ReadSecret = () => Cli.Secret,
            AgentOptions = Cli.AgentOptions,
            HostOptions = Cli.HostOptions,
            Update = Cli.Update,
            Login = new LoginEnvironment
            {
                OpenBrowser = Browser.Open,
                Timeout = Timeout,
                MachineName = () => MachineName,
                AutostartSupported = AutostartSupported,
                RunCommand = (arguments, _, _) =>
                {
                    var copy = arguments.ToArray();
                    lock (SubCommands)
                    {
                        SubCommands.Add(copy);
                    }

                    return Task.FromResult(SubCommandExit(copy));
                },
            },
        };
        var exitCode = await CliRunner.RunAsync(args, context, CancellationToken.None);
        return new CliResult(exitCode, OutWriter.ToString(), ErrorWriter.ToString());
    }

    public void Dispose() => Cli.Dispose();
}
