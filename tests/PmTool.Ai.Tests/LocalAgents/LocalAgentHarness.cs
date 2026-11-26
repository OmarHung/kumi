using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Persistence;
using PmTool.Ai.Tests.Assistant.Fakes;

namespace PmTool.Ai.Tests.LocalAgents;

/// <summary>
/// 本機代理測試的環境：在助理的環境上，組織允許成員用自己的 AI 帳號，並提供「本機代理」的動作（登記、領工作、回報、結束）。
/// 本機的引擎由測試自己扮演：不跑真的 <c>claude</c>。
/// </summary>
internal sealed class LocalAgentHarness : IAsyncDisposable
{
    public const string McpUrl = "https://kumi.example.com/mcp";

    public const string MachineKey = "machine-0001";

    private LocalAgentHarness(AssistantHarness assistant)
    {
        Assistant = assistant;
    }

    public AssistantHarness Assistant { get; }

    /// <summary>每次登記報的 kumi 版本；0.10.0 以上才收圖片與 PDF。測試要改就在登記之前設定。</summary>
    public string AgentVersion { get; set; } = "0.9.0";

    public AiTestHost Host => Assistant.Host;

    public Actor Me => Assistant.Actor;

    public ILocalAgentService Agents => Host.Get<ILocalAgentService>();

    public ILocalAgentDirectory Directory => Host.Get<ILocalAgentDirectory>();

    public static LocalAgentTimings FastTimings { get; } = new(
        TimeSpan.FromMilliseconds(600), TimeSpan.FromMilliseconds(900), TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(600));

    /// <summary>逾時很長的：測試自己慢慢走（領工作、回報）時，不會被伺服器當成沒回應。停止之後等本機代理的時間還是短的。</summary>
    public static LocalAgentTimings PatientTimings { get; } = new(
        TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(600));

    public static LocalEngineInfo ClaudeCode(params AiModelOption[] models) => new(
        AiProviderKind.ClaudeCode,
        "2.1.330",
        models.Length > 0
            ? models
            : [new AiModelOption("default", "預設", []), new AiModelOption("sonnet", "Sonnet", [AiEffort.Low, AiEffort.Medium, AiEffort.High])]);

    public static LocalEngineInfo Codex() => new(AiProviderKind.Codex, "0.163.0", [new AiModelOption("default", "預設", [])]);

    public static async Task<LocalAgentHarness> StartAsync(
        LocalAgentTimings? timings = null, bool allowPersonal = true, string? mcpUrl = McpUrl, AssistantTimings? assistantTimings = null)
    {
        var assistant = await AssistantHarness.StartAsync(
            timings: assistantTimings,
            configure: services =>
            {
                services.AddSingleton(timings ?? FastTimings);
                services.AddScoped<IMcpUrlProvider>(_ => new FixedMcpUrl(mcpUrl));
            });
        var harness = new LocalAgentHarness(assistant);
        var settings = assistant.Host.Get<IAiSettingsService>();
        var saved = await settings.SaveAsync(
            assistant.Admin,
            new AiSettingsInput(true, new AiSelection(assistant.Connection.Id, ScriptedGateway.ModelId, null)) { AllowPersonalConnections = allowPersonal },
            CancellationToken.None);
        Assert.That(saved.Succeeded, Is.True, () => saved.Failure?.Message ?? string.Empty);
        return harness;
    }

    public Task<Result<LocalAgentJob?>> CheckInAsync(
        Actor? actor = null,
        string machineKey = MachineKey,
        string name = "我的電腦",
        IReadOnlyList<LocalEngineInfo>? engines = null,
        int capacity = 0,
        CancellationToken cancellationToken = default) =>
        Agents.CheckInAsync(
            actor ?? Me, new LocalAgentCheckIn(machineKey, name, AgentVersion, engines ?? [ClaudeCode()]), capacity, cancellationToken);

    /// <summary>登記一台電腦與它的 Claude Code，回傳能選清單裡的引擎（連線編號就是引擎的編號）。</summary>
    public async Task<AiConnectionOption> RegisterAsync(
        Actor? actor = null, string machineKey = MachineKey, string name = "我的電腦", IReadOnlyList<LocalEngineInfo>? engines = null)
    {
        var registered = await CheckInAsync(actor, machineKey, name, engines);
        Assert.That(registered.Succeeded, Is.True, () => registered.Failure?.Message ?? string.Empty);
        var options = await Host.Get<IAiOptionsService>().GetAsync(actor ?? Me, CancellationToken.None);
        return options.Connections.Single(c => c.Kind == (engines ?? [ClaudeCode()])[0].Kind && c.Name.Contains(name, StringComparison.Ordinal));
    }

    public static AiSelection Select(AiConnectionOption engine, string model = "default", AiEffort? effort = null) =>
        new(engine.Id, model, effort);

    /// <summary>扮演本機代理領一件工作：一直登記到領到為止，最多等 <paramref name="wait"/>。</summary>
    public async Task<LocalAgentJob> ClaimAsync(
        Actor? actor = null, string machineKey = MachineKey, TimeSpan? wait = null, IReadOnlyList<LocalEngineInfo>? engines = null)
    {
        using var timeout = new CancellationTokenSource(wait ?? TimeSpan.FromSeconds(10));
        while (!timeout.IsCancellationRequested)
        {
            var polled = await CheckInAsync(actor, machineKey, engines: engines, capacity: 1, cancellationToken: timeout.Token);
            if (polled.Succeeded && polled.Value is { } job)
            {
                return job;
            }
        }

        throw new TimeoutException("等不到本機代理的工作。");
    }

    public async Task<LocalAgentReportAck> ReportAsync(Guid jobId, params LocalAgentEvent[] events)
    {
        var ack = await Agents.ReportAsync(Me, jobId, events, CancellationToken.None);
        Assert.That(ack.Succeeded, Is.True, () => ack.Failure?.Message ?? string.Empty);
        return ack.Value;
    }

    public async Task FinishAsync(
        Guid jobId, AssistantMessageStatus status = AssistantMessageStatus.Complete, string? error = null, AssistantUsage? usage = null)
    {
        var finished = await Agents.FinishAsync(Me, jobId, new LocalAgentFinish(status, error, usage), CancellationToken.None);
        Assert.That(finished.Succeeded, Is.True, () => finished.Failure?.Message ?? string.Empty);
    }

    public static LocalAgentText Text(string delta) => new(delta);

    public static LocalAgentTool Tool(
        string callId, string name, AssistantToolStatus status, long? changeSetNumber = null, string? error = null, Guid? itemProposalId = null,
        Guid? newItemProposalId = null) =>
        new(new AssistantToolPart(callId, name, status, changeSetNumber, error) { ItemProposalId = itemProposalId, NewItemProposalId = newItemProposalId });

    public async Task<LocalJobRow> JobRowAsync(Guid jobId)
    {
        await using var db = await Host.CreateDbContextAsync();
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.LocalJobs, j => j.Id == jobId);
    }

    /// <summary>回覆在背景建立工作列；要動時鐘之前先等它建好，工作的建立時間才是動之前的。</summary>
    public async Task WaitForJobRowsAsync(int count)
    {
        for (var i = 0; i < 200; i++)
        {
            await using var db = await Host.CreateDbContextAsync();
            if (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.LocalJobs) >= count)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("工作列沒有建起來。");
    }

    public async Task<int> EventCountAsync(Guid jobId)
    {
        await using var db = await Host.CreateDbContextAsync();
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.LocalEvents, e => e.JobId == jobId);
    }

    /// <summary>
    /// 網頁送出一則給本機引擎的訊息（回覆在背景進行，工作等著被領）。回傳的 <see cref="LocalRun"/> 沒讀完的，測試結束時自動停掉，
    /// 不留背景的回覆去讀已經關掉的資料庫。
    /// </summary>
    public async Task<LocalRun> SendAsync(AiConnectionOption engine, string text = "列出目前的狀態", string model = "default", AiEffort? effort = null, Guid? conversationId = null, Actor? actor = null, string? itemKey = null)
    {
        var id = conversationId ?? (await Assistant.NewConversationAsync(actor)).Id;
        var run = await Assistant.StartAsync(id, text, actor, Select(engine, model, effort), itemKey);
        var local = new LocalRun(id, run);
        _runs.Add(local);
        return local;
    }

    public async Task<ReplyResult> SendAndReadAsync(AiConnectionOption engine, string text = "列出目前的狀態", Guid? conversationId = null)
    {
        var run = await SendAsync(engine, text, conversationId: conversationId);
        return new ReplyResult(run.Run, await run.ReadAllAsync());
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var run in _runs)
        {
            await run.StopAsync();
        }

        await Assistant.DisposeAsync();
    }

    private readonly List<LocalRun> _runs = [];

    private sealed class FixedMcpUrl(string? url) : IMcpUrlProvider
    {
        public string? GetMcpUrl() => url;
    }
}

/// <summary>一則在背景進行的本機回覆。事件只能讀一次，所以讀過的存起來。</summary>
internal sealed class LocalRun(Guid conversationId, AssistantRun run)
{
    private IReadOnlyList<AssistantRunEvent>? _events;
    private bool _opened;

    public Guid ConversationId => conversationId;

    public AssistantRun Run => run;

    public async Task<IReadOnlyList<AssistantRunEvent>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        _opened = true;
        return _events ??= await AssistantHarness.ReadAllAsync(run, cancellationToken);
    }

    /// <summary>
    /// 讀到 <paramref name="stopWhen"/> 的事件就像使用者按「停止」一樣取消讀的 token（關頁面也是這樣），讀完最後的結束事件才回。
    /// </summary>
    public async Task<IReadOnlyList<AssistantRunEvent>> ReadUntilStopAsync(Func<AssistantRunEvent, bool> stopWhen)
    {
        _opened = true;
        using var stop = new CancellationTokenSource();
        var events = new List<AssistantRunEvent>();
        await foreach (var runEvent in run.Events.WithCancellation(stop.Token))
        {
            events.Add(runEvent);
            if (stopWhen(runEvent))
            {
                await stop.CancelAsync();
            }
        }

        return _events = events;
    }

    /// <summary>使用者離開頁面（不讀了）：回覆存成「已停止」。</summary>
    public async Task LeaveAsync()
    {
        _opened = true;
        await run.Events.GetAsyncEnumerator().DisposeAsync();
    }

    /// <summary>沒讀過的：開了又立刻放掉，等於讀的人離開（停止），回覆存成「已停止」。</summary>
    public async Task StopAsync()
    {
        if (_opened)
        {
            return;
        }

        _opened = true;
        await run.Events.GetAsyncEnumerator().DisposeAsync();
    }
}
