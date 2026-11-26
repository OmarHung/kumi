using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.HostAgents;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Persistence;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Ai.Tests.LocalAgents;
using PmTool.Identity;

namespace PmTool.Ai.Tests.HostAgents;

/// <summary>一台配對好的 worker：機器憑證解出來的身分，與它每次登記要回報的引擎狀態（測試可以改）。</summary>
internal sealed class HostWorker(HostAgentIdentity identity, string credential, string name)
{
    public HostAgentIdentity Identity => identity;

    public Guid AgentId => identity.AgentId;

    public string Credential => credential;

    public string Name => name;

    /// <summary>配對時報的機器編號；之後每次登記都要帶同一個。</summary>
    public string MachineKey { get; init; } = HostHarness.DefaultMachineKey;

    /// <summary>每次登記報的 worker 版本；0.10.0 以上才收圖片與 PDF。改了要再登記一次才算數。</summary>
    public string AgentVersion { get; set; } = "0.9.0";

    public HostAgentCheckIn CheckIn() => new(AgentVersion, HostHarness.ServerUrl, Reports) { MachineKey = MachineKey, MachineName = "主機" };

    public IReadOnlyList<HostEngineReport> Reports { get; set; } = [];
}

/// <summary>一個接好的組織來源：worker、登入、來源。</summary>
internal sealed record HostSetup(HostWorker Worker, HostSource Source, Guid LoginId);

/// <summary>
/// 主機代理測試的環境：在助理的環境上，加上組織裡各種角色的人（擁有者、管理員、一般成員、訪客、停用的人、別的組織的擁有者），
/// 並提供 worker 的動作（登記、領工作、回報、結束）。身分模組用假的目錄：沒登記的人查不到。AI 開關與個人連線預設都是關的，
/// 證明主機來源和它們無關。引擎由測試自己扮演：不跑真的 <c>claude</c>。
/// </summary>
internal sealed class HostHarness : IAsyncDisposable
{
    public const string ServerUrl = "http://api:8080";

    public const string DefaultMachineKey = "machine-0001";

    private readonly List<LocalRun> _runs = [];

    private HostHarness(AssistantHarness assistant)
    {
        Assistant = assistant;
        var org = assistant.OrganizationId;
        People.DefaultRole = null;
        Owner = Person(org, OrganizationRole.Owner);
        Admin = Person(org, OrganizationRole.Admin);
        Member = Person(org, OrganizationRole.Member);
        Member2 = Person(org, OrganizationRole.Member);
        Guest = Person(org, OrganizationRole.Guest);
        Disabled = Person(org, OrganizationRole.Member, disabled: true);
        OtherOrg = Guid.CreateVersion7();
        OtherOwner = Person(OtherOrg, OrganizationRole.Owner);
    }

    public static HostTimings FastTimings { get; } = new(
        QueueTimeout: TimeSpan.FromSeconds(8),
        LeaseTimeout: TimeSpan.FromSeconds(8),
        PollInterval: TimeSpan.FromMilliseconds(20),
        PositionInterval: TimeSpan.FromMilliseconds(40),
        StopGrace: TimeSpan.FromMilliseconds(600),
        PollWait: TimeSpan.FromMilliseconds(500),
        ClaimPollInterval: TimeSpan.FromMilliseconds(50));

    public AssistantHarness Assistant { get; }

    public AiTestHost Host => Assistant.Host;

    public FakePeopleDirectory People => (FakePeopleDirectory)Host.Get<IPeopleDirectory>();

    public ManualClock Clock => Assistant.Clock;

    public Actor Owner { get; }

    /// <summary>身分模組裡的角色是管理員；<see cref="Actor.IsOrganizationAdmin"/> 也是 true，證明管理端點不看它。</summary>
    public Actor Admin { get; }

    public Actor Member { get; }

    public Actor Member2 { get; }

    public Actor Guest { get; }

    public Actor Disabled { get; }

    public Actor OtherOwner { get; }

    public Guid OtherOrg { get; }

    public IHostAgentAdminService Service => Host.Get<IHostAgentAdminService>();

    public IHostAgentWorkerService Worker => Host.Get<IHostAgentWorkerService>();

    public IHostToolGrants Grants => Host.Get<IHostToolGrants>();

    public static async Task<HostHarness> StartAsync(HostTimings? timings = null, AssistantTimings? assistantTimings = null)
    {
        var assistant = await AssistantHarness.StartAsync(
            enabled: false,
            timings: assistantTimings,
            configure: services =>
            {
                services.AddSingleton(timings ?? FastTimings);
                services.AddSingleton<IPeopleDirectory>(new FakePeopleDirectory());
            });
        return new HostHarness(assistant);
    }

    private Actor Person(Guid org, OrganizationRole role, bool disabled = false)
    {
        var actor = new Actor(org, Guid.CreateVersion7(), role is OrganizationRole.Owner or OrganizationRole.Admin);
        People.Set(org, actor.PersonId, role, disabled);
        return actor;
    }

    /// <summary>之後才加入組織的人（新成員）。</summary>
    public Actor NewMember() => Person(Assistant.OrganizationId, OrganizationRole.Member);

    public static IReadOnlyList<AiModelOption> DefaultModels() =>
        [new AiModelOption("default", "預設", []), new AiModelOption("sonnet", "Sonnet", [AiEffort.Low, AiEffort.Medium, AiEffort.High])];

    public static HostEngineReport Report(
        AiProviderKind kind = AiProviderKind.ClaudeCode,
        bool installed = true,
        bool supported = true,
        bool loggedIn = true,
        HostBilling billing = HostBilling.Api,
        Guid? loginId = null,
        IReadOnlyList<AiModelOption>? models = null) =>
        new(kind, installed, installed ? "2.1.330" : null, supported, loggedIn, billing, loginId, models ?? DefaultModels(), null);

    // ---- 擁有者的流程 ----

    public async Task<HostAgentPairing> CreateAgentAsync(string name = "主機代理", Actor? actor = null)
    {
        var created = await Service.CreateAgentAsync(actor ?? Owner, new HostAgentInput(name), CancellationToken.None);
        Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
        return created.Value;
    }

    public async Task<HostWorker> PairAsync(string name = "主機代理")
    {
        var pairing = await CreateAgentAsync(name);
        var paired = await Worker.PairAsync(new HostPairingRequest(pairing.PairingCode, DefaultMachineKey, "主機", "0.9.0"), CancellationToken.None);
        Assert.That(paired.Succeeded, Is.True, () => paired.Failure?.Message ?? string.Empty);
        var identity = await Worker.AuthenticateAsync(paired.Value.Credential, HostHarness.DefaultMachineKey, CancellationToken.None);
        Assert.That(identity, Is.Not.Null);
        return new HostWorker(identity!, paired.Value.Credential, name);
    }

    /// <summary>擁有者開始登入、worker 認領、CLI 登入完成（API 計費）。回傳登入編號。</summary>
    public async Task<Guid> LoginAsync(
        HostWorker worker, AiProviderKind engine = AiProviderKind.ClaudeCode, HostBilling billing = HostBilling.Api)
    {
        var started = await Service.StartLoginAsync(Owner, worker.AgentId, engine, CancellationToken.None);
        Assert.That(started.Succeeded, Is.True, () => started.Failure?.Message ?? string.Empty);
        var claimed = await Worker.ClaimLoginAsync(worker.Identity, started.Value.Code, CancellationToken.None);
        Assert.That(claimed.Succeeded, Is.True, () => claimed.Failure?.Message ?? string.Empty);
        var completed = await Worker.CompleteLoginAsync(
            worker.Identity, started.Value.Login.Id, new HostLoginReport(true, billing, null), CancellationToken.None);
        Assert.That(completed.Succeeded, Is.True, () => completed.Failure?.Message ?? string.Empty);
        return started.Value.Login.Id;
    }

    public static HostSourceInput SourceInput(
        Guid agentId,
        AiProviderKind engine = AiProviderKind.ClaudeCode,
        string name = "主機上的 Claude Code",
        bool enabled = true,
        HostSourceLimits? limits = null,
        bool acknowledge = true,
        IReadOnlyList<string>? models = null) =>
        new(agentId, engine, name, enabled, models ?? ["default", "sonnet"], null, null, limits ?? new HostSourceLimits(null, null, null), acknowledge);

    /// <summary>配對、登入、登記、開來源，一次到位：成員馬上能選、能送出。</summary>
    public async Task<HostSetup> SetUpAsync(string name = "主機上的 Claude Code", HostSourceLimits? limits = null, string agentName = "主機代理")
    {
        var worker = await PairAsync(agentName);
        var loginId = await LoginAsync(worker);
        worker.Reports = [Report(loginId: loginId)];
        await CheckInAsync(worker);
        var source = await Service.CreateSourceAsync(Owner, SourceInput(worker.AgentId, name: name, limits: limits), CancellationToken.None);
        Assert.That(source.Succeeded, Is.True, () => source.Failure?.Message ?? string.Empty);
        return new HostSetup(worker, source.Value, loginId);
    }

    // ---- worker 的動作 ----

    public async Task<HostCheckInResult> CheckInAsync(HostWorker worker, int capacity = 0, CancellationToken cancellationToken = default)
    {
        var result = await Worker.CheckInAsync(
            worker.Identity, worker.CheckIn(), capacity, cancellationToken);
        Assert.That(result.Succeeded, Is.True, () => result.Failure?.Message ?? string.Empty);
        return result.Value;
    }

    /// <summary>扮演 worker 領一件工作：一直登記到領到為止，最多等 <paramref name="wait"/>。</summary>
    public async Task<HostJob> ClaimAsync(HostWorker worker, TimeSpan? wait = null, int capacity = 2)
    {
        using var timeout = new CancellationTokenSource(wait ?? TimeSpan.FromSeconds(10));
        while (!timeout.IsCancellationRequested)
        {
            var result = await Worker.CheckInAsync(
                worker.Identity, worker.CheckIn(), capacity, timeout.Token);
            if (result.Succeeded && result.Value.Job is { } job)
            {
                return job;
            }
        }

        throw new TimeoutException("等不到主機代理的工作。");
    }

    public async Task<HostReportAck> ReportAsync(HostWorker worker, HostJob job, int firstSeq, params LocalAgentEvent[] events)
    {
        var ack = await Worker.ReportAsync(worker.Identity, job.JobId, job.Lease, firstSeq, events, CancellationToken.None);
        Assert.That(ack.Succeeded, Is.True, () => ack.Failure?.Message ?? string.Empty);
        return ack.Value;
    }

    public async Task FinishAsync(
        HostWorker worker, HostJob job, AssistantMessageStatus status = AssistantMessageStatus.Complete, string? error = null, AssistantUsage? usage = null)
    {
        var finished = await Worker.FinishAsync(worker.Identity, job.JobId, job.Lease, new LocalAgentFinish(status, error, usage), CancellationToken.None);
        Assert.That(finished.Succeeded, Is.True, () => finished.Failure?.Message ?? string.Empty);
    }

    // ---- 成員的動作 ----

    public async Task<IReadOnlyList<AiConnectionOption>> OptionsOfAsync(Actor actor) =>
        (await Host.Get<IAiOptionsService>().GetAsync(actor, CancellationToken.None)).Connections;

    public async Task<Result<AssistantRun>> TrySendAsync(
        Actor actor, HostSource source, string text = "列出目前的狀態", string model = "default", string? projectKey = AssistantHarness.ProjectKey)
    {
        var created = await Assistant.Service.CreateAsync(actor, projectKey, CancellationToken.None);
        if (created.Failure is { } failure)
        {
            return failure;
        }

        return await Assistant.Service.StartAsync(
            actor,
            projectKey,
            created.Value.Id,
            new AssistantMessageInput(text, new AiSelection(source.Id, model, null)),
            CancellationToken.None);
    }

    /// <summary>送出一則給主機來源的訊息（回覆在背景進行）。沒讀完的，測試結束時自動停掉。</summary>
    public async Task<LocalRun> SendAsync(
        Actor actor, HostSource source, string text = "列出目前的狀態", string model = "default", string? projectKey = AssistantHarness.ProjectKey)
    {
        var started = await TrySendAsync(actor, source, text, model, projectKey);
        Assert.That(started.Succeeded, Is.True, () => started.Failure?.Message ?? string.Empty);
        var run = new LocalRun(Guid.Empty, started.Value);
        _runs.Add(run);
        return run;
    }

    public async Task<HostSource> SourceAsync(Guid sourceId)
    {
        var overview = await Service.GetAsync(Owner, CancellationToken.None);
        Assert.That(overview.Succeeded, Is.True, () => overview.Failure?.Message ?? string.Empty);
        return overview.Value.Sources.Single(s => s.Id == sourceId);
    }

    public async Task<HostAgent> AgentAsync(Guid agentId)
    {
        var overview = await Service.GetAsync(Owner, CancellationToken.None);
        return overview.Value.Agents.Single(a => a.Id == agentId);
    }

    public async Task<HostJobRow> JobRowAsync(Guid jobId)
    {
        await using var db = await Host.CreateDbContextAsync();
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.HostJobs, j => j.Id == jobId);
    }

    public async Task<int> JobRowCountAsync()
    {
        await using var db = await Host.CreateDbContextAsync();
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.HostJobs);
    }

    /// <summary>回覆在背景把預留的工作排進隊伍；等到有 <paramref name="count"/> 件在排隊或執行。</summary>
    public async Task WaitForQueuedAsync(int count)
    {
        for (var i = 0; i < 400; i++)
        {
            await using var db = await Host.CreateDbContextAsync();
            var queued = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(
                db.HostJobs, j => j.State == HostJobStates.Queued || j.State == HostJobStates.Running);
            if (queued >= count)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("工作沒有排進隊伍。");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var run in _runs)
        {
            await run.StopAsync();
        }

        await Assistant.DisposeAsync();
    }
}
