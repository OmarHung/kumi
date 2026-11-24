using System.Text.Json.Nodes;
using PmTool.Cli.Agent;
using PmTool.Cli.Api;
using PmTool.Cli.Host;
using PmTool.Cli.Settings;

namespace PmTool.Cli.Commands;

/// <summary>
/// <c>kumi host …</c>：常駐在 Kumi 主機（容器）的 worker。<b>只給主機的服務與擁有者在主機上用，AI 代理不要自己執行。</b>
/// 配對碼與登入碼從標準輸入讀（不放在命令列，才不會留在歷史與程序清單）；機器憑證存在狀態檔（權限 600）、不印出來。
/// 規格見 internal/HANDOFF-host-agent.md 第 8 節。
/// </summary>
internal static class HostCommands
{
    private const string EngineArgument = "claude-code|codex";

    public static readonly IReadOnlyList<CommandSpec> Specs =
    [
        new("host", "pair", "用網頁給的配對碼，把這台主機（worker）配給 Kumi",
            "kumi host pair [--server 網址] [--name 名稱]（配對碼從標準輸入讀）",
            [new OptionSpec("name", "名稱", "這台主機的名稱（預設是主機名稱）")],
            0, 0, PairAsync),
        new("host", "run", "服務本身：登記、領網頁送來的回覆、用主機上的 Claude Code／Codex 跑",
            $"kumi host run [--engine {EngineArgument}]…",
            [new OptionSpec("engine", "引擎", "只開放這個引擎：claude-code 或 codex；可以重複（預設兩個都開放）", Repeatable: true)],
            0, 0, RunAsync),
        new("host", "login", "完成擁有者在網頁開始的引擎登入（登入碼從標準輸入讀）",
            $"kumi host login <{EngineArgument}>",
            [],
            1, 1, LoginAsync),
        new("host", "status", "狀態檔（不印憑證）、伺服器、代理編號與兩個引擎的探測結果",
            "kumi host status [--json]",
            [],
            0, 0, StatusAsync),
        new("host", "logout", "在主機上登出一個引擎並清掉 Kumi 的授權（平常用網頁的「登出」）",
            $"kumi host logout <{EngineArgument}>",
            [],
            1, 1, LogoutAsync),
    ];

    private static async Task<int> PairAsync(CommandCall call)
    {
        var context = call.Context;
        var store = HostStateStore.FromEnvironment(context.GetEnvironmentVariable);
        var current = store.Load();
        var server = Credentials.Normalize(
            call.Arguments.Value("server") ?? Credentials.ServerFromEnvironment(context)?.Value ?? current.Server
            ?? throw CliException.Usage($"還沒設定伺服器。加 --server，或設定環境變數 {Credentials.ServerVariable}。"));
        Credentials.EnsureTransportAllowed(context, call.Arguments, server);

        var code = ReadCode(call, "請輸入配對碼：");
        var machineKey = store.EnsureMachineKey();
        var body = new JsonObject
        {
            ["code"] = code,
            ["machineKey"] = machineKey,
            ["machineName"] = MachineIdentity.ResolveName(call.Arguments.Value("name")),
            ["agentVersion"] = HelpText.VersionNumber,
        };
        var result = await new ApiClient(context.Http, server, string.Empty).PostAsync("host-agent/pair", body, call.CancellationToken);
        if (!Guid.TryParse(result.Text("agentId"), out var agentId) || result.Text("credential") is not { Length: > 0 } credential)
        {
            throw new CliException(ExitCodes.Error, "伺服器的回應不正確（少了代理編號或憑證）。請確認伺服器的版本。");
        }

        var agentName = result.Text("agentName") ?? string.Empty;
        store.Update(state => state with
        {
            Server = server,
            AgentId = agentId,
            AgentName = agentName,
            Credential = credential,
            // 換成另一台代理時，舊的登入編號對不上，不留著。
            Logins = state.AgentId == agentId ? state.Logins : new Dictionary<string, HostLoginRecord>(),
        });
        await WriteAsync(call, new JsonObject { ["agentId"] = agentId, ["agentName"] = agentName, ["server"] = server },
            $"已配對：「{agentName}」（{server}）。服務會在 30 秒內開始登記。");
        return ExitCodes.Success;
    }

    private static async Task<int> RunAsync(CommandCall call)
    {
        var context = call.Context;
        var kinds = ResolveKinds(call.Arguments.Values("engine"));
        var store = HostStateStore.FromEnvironment(context.GetEnvironmentVariable);
        var console = new AgentConsole(call.Out, call.Error, json: false);
        var service = new HostService(
            store, context.Http, new HostEngineProbe(context.GetEnvironmentVariable, context.AgentOptions), kinds, context.HostOptions,
            context.AgentOptions, console, HelpText.VersionNumber, () => DateTimeOffset.Now,
            server => Credentials.EnsureTransportAllowed(context, call.Arguments, server));

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(call.CancellationToken);
        using var registration = context.RegisterInterrupt(() => stop.Cancel());
        console.Notice($"kumi host run {HelpText.VersionNumber}：狀態檔 {store.Path}，開放 {string.Join("、", kinds.Select(EngineKinds.DisplayName))}。");
        await service.RunAsync(stop.Token);
        return ExitCodes.Success;
    }

    private static async Task<int> LoginAsync(CommandCall call)
    {
        var context = call.Context;
        var kind = ResolveKind(call.Arguments.Positionals[0]);
        var store = HostStateStore.FromEnvironment(context.GetEnvironmentVariable);
        var state = RequirePaired(store.Load());
        Credentials.EnsureTransportAllowed(context, call.Arguments, state.Server!);

        var code = ReadCode(call, "請輸入登入碼：");
        var api = new ApiClient(context.Http, state.Server!, state.Credential!);
        var login = new HostLogin(store, api, new HostEngineProbe(context.GetEnvironmentVariable, context.AgentOptions), context.GetEnvironmentVariable, call.Error);
        await login.RunAsync(kind, code, call.CancellationToken);
        await call.Out.WriteLineAsync($"{EngineKinds.DisplayName(kind)} 登入完成，已經授權給 Kumi。服務會在一分鐘內回報。");
        return ExitCodes.Success;
    }

    private static async Task<int> StatusAsync(CommandCall call)
    {
        var context = call.Context;
        var state = HostStateStore.FromEnvironment(context.GetEnvironmentVariable).Load();
        var probe = new HostEngineProbe(context.GetEnvironmentVariable, context.AgentOptions);
        var engines = new JsonArray();
        foreach (var kind in new[] { EngineKinds.ClaudeCode, EngineKinds.Codex })
        {
            var probed = await probe.ProbeAsync(kind, call.CancellationToken);
            var authorized = probed.LoggedIn && state.Logins.TryGetValue(kind, out var login)
                && (login.Fingerprint is null || probed.Fingerprint is null || login.Fingerprint == probed.Fingerprint);
            engines.Add(new JsonObject
            {
                ["kind"] = kind,
                ["installed"] = probed.Installed,
                ["version"] = probed.Version,
                ["supported"] = probed.Supported,
                ["loggedIn"] = probed.LoggedIn,
                ["billing"] = probed.Billing,
                ["authorized"] = authorized,
                ["problem"] = probed.Problem,
            });
        }

        // 不印憑證、不印機器編號以外的機密；狀態檔的路徑不是機密。
        var data = new JsonObject
        {
            ["paired"] = state.IsPaired,
            ["server"] = state.Server,
            ["agentId"] = state.AgentId,
            ["agentName"] = state.AgentName,
            ["engines"] = engines,
        };
        if (call.Json)
        {
            await call.Out.WriteLineAsync(data.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return ExitCodes.Success;
        }

        await call.Out.WriteLineAsync(state.IsPaired
            ? $"已配對：「{state.AgentName}」（{state.AgentId}），伺服器 {state.Server}"
            : "還沒配對：請在網頁新增主機代理，再執行 kumi host pair。");
        foreach (var engine in engines.OfType<JsonObject>())
        {
            await call.Out.WriteLineAsync(Describe(engine));
        }

        return ExitCodes.Success;
    }

    private static string Describe(JsonObject engine)
    {
        var name = EngineKinds.DisplayName(engine["kind"]!.GetValue<string>());
        if (!engine["installed"]!.GetValue<bool>())
        {
            return $"  {name}：沒裝";
        }

        var version = engine["version"]?.GetValue<string>() ?? "?";
        if (!engine["supported"]!.GetValue<bool>())
        {
            return $"  {name} {version}：版本不支援";
        }

        var login = engine["loggedIn"]!.GetValue<bool>()
            ? $"已登入（{BillingLabel(engine["billing"]!.GetValue<string>())}）{(engine["authorized"]!.GetValue<bool>() ? "，Kumi 已授權" : "，Kumi 還沒授權")}"
            : "沒登入";
        return $"  {name} {version}：{login}";
    }

    private static string BillingLabel(string billing) => billing switch
    {
        Billing.Api => "API 用量計費",
        Billing.Subscription => "訂閱",
        _ => "計費方式不明",
    };

    private static async Task<int> LogoutAsync(CommandCall call)
    {
        var context = call.Context;
        var kind = ResolveKind(call.Arguments.Positionals[0]);
        var store = HostStateStore.FromEnvironment(context.GetEnvironmentVariable);
        // 先清 Kumi 的授權：不管 CLI 登出成不成功，Kumi 都不該再當它有效。
        store.Update(state => state.WithLogin(kind, null));
        var done = await new HostEngineProbe(context.GetEnvironmentVariable, context.AgentOptions).LogoutAsync(kind, call.CancellationToken);
        if (!done)
        {
            throw new CliException(ExitCodes.Error, $"{EngineKinds.DisplayName(kind)} 的登出沒有成功（沒裝，或本來就沒登入）。Kumi 的授權已經清掉。");
        }

        await call.Out.WriteLineAsync($"已登出 {EngineKinds.DisplayName(kind)}，Kumi 的授權也清掉了。");
        return ExitCodes.Success;
    }

    /// <summary>配對碼、登入碼：終端機上提示並不回顯；不是終端機就讀標準輸入的第一行。不接受命令列參數。</summary>
    private static string ReadCode(CommandCall call, string prompt)
    {
        var context = call.Context;
        string? text;
        if (context.IsInteractive && context.ReadSecret is { } readSecret)
        {
            context.Error.Write(prompt);
            text = readSecret();
        }
        else
        {
            text = context.RawInput is { } raw ? ReadLineFromStream(raw) : context.In.ReadLine();
        }

        return string.IsNullOrWhiteSpace(text)
            ? throw CliException.Usage("沒有收到碼。在終端機執行（docker compose exec -it …），或用標準輸入給（例如 echo 碼 | kumi host pair）。")
            : text.Trim();
    }

    /// <summary>
    /// 一個位元組一個位元組讀到換行為止：不能用有緩衝的讀取器，不然後面的內容（<c>kumi host login codex</c> 要從同一條管線讀 API 金鑰）
    /// 會被一起吞進緩衝，子程序收不到。
    /// </summary>
    internal static string? ReadLineFromStream(Stream stream)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (stream.Read(one, 0, 1) == 1)
        {
            if (one[0] == (byte)'\n')
            {
                break;
            }

            bytes.Add(one[0]);
        }

        return bytes.Count == 0 && stream.CanSeek && stream.Position == stream.Length && one[0] != (byte)'\n'
            ? null
            : System.Text.Encoding.UTF8.GetString([.. bytes]).TrimEnd('\r');
    }

    private static HostState RequirePaired(HostState state) => state.IsPaired
        ? state
        : throw new CliException(ExitCodes.Error, "這台主機還沒配對。請先在網頁新增主機代理，再執行 kumi host pair。");

    private static async Task WriteAsync(CommandCall call, JsonObject data, string text) =>
        await call.Out.WriteLineAsync(call.Json ? data.ToJsonString() : text);

    private static string ResolveKind(string option) => EngineKinds.FromOption(option)
        ?? throw CliException.Usage($"不認得的引擎 {option}。可以用 claude-code 或 codex。");

    private static IReadOnlyList<string> ResolveKinds(IReadOnlyList<string> requested) =>
        requested.Count == 0
            ? [EngineKinds.ClaudeCode, EngineKinds.Codex]
            : [.. requested.Select(ResolveKind).Distinct()];
}
