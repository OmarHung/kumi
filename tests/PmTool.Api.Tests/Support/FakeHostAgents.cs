using System.Collections.Concurrent;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Identity;

namespace PmTool.Api.Tests.Support;

/// <summary>
/// 測試用的主機代理管理那一頭：記住每次呼叫（誰、哪個方法、參數），回應可以換掉。
/// 「只有擁有者」是模組的規則（這裡不判斷）：測試要看 API 怎麼轉 403 時，把 <see cref="OnCall"/> 換成回失敗的。
/// </summary>
internal sealed class FakeHostAgentAdminService : IHostAgentAdminService
{
    public static readonly Guid AgentId = Guid.Parse("66666666-6666-6666-6666-666666666661");

    public static readonly Guid SourceId = Guid.Parse("66666666-6666-6666-6666-666666666662");

    public static readonly Guid LoginId = Guid.Parse("66666666-6666-6666-6666-666666666663");

    public static readonly DateTimeOffset Now = new(2026, 11, 24, 3, 0, 0, TimeSpan.Zero);

    private readonly ConcurrentQueue<(Actor Actor, string Method, object? Argument)> _calls = new();

    /// <summary>每個方法進來先問它：回非 null 就是那個失敗（其他方法照預設）。</summary>
    public Func<Actor, string, Failure?> OnCall { get; set; } = (_, _) => null;

    public IReadOnlyList<(Actor Actor, string Method, object? Argument)> Calls => _calls.ToArray();

    public static HostEngineStatus Engine(AiProviderKind kind = AiProviderKind.ClaudeCode) => new(
        kind,
        HostEngineReadiness.Ready,
        "2.1.330",
        true,
        HostBilling.Api,
        [new AiModelOption("default", "預設", [AiEffort.Low, AiEffort.High]) { DefaultEffort = AiEffort.High }],
        null,
        Now,
        TestUsers.Owner.PersonId);

    public static HostLogin Login() =>
        new(LoginId, AiProviderKind.ClaudeCode, HostLoginState.Pending, Now.AddMinutes(15), null, TestUsers.Owner.PersonId, Now);

    public static HostAgent Agent() => new(
        AgentId, "主機一號", HostAgentState.Active, true, Now, "host-01", "0.9.0", "http://api:8080", [Engine()], [Login()], null, Now.AddDays(-1))
    {
        CredentialExpiresAt = Now.AddDays(60),
        Warning = new HostAgentWarning(HostAgentWarningKind.OtherMachine, "別台電腦", Now.AddHours(-1)),
    };

    public static HostSource Source() => new(
        SourceId,
        AgentId,
        AiProviderKind.ClaudeCode,
        "主機上的 Claude Code",
        true,
        HostSourceStatus.Ready,
        null,
        [new AiModel("default", "預設", [AiEffort.Low, AiEffort.High], true, false)],
        "default",
        AiEffort.High,
        new HostSourceLimits(5, 200, null),
        1,
        2,
        new HostUsageTotals(10, 7, 2, 1, 1000, 500),
        Now,
        Now,
        TestUsers.Owner.PersonId);

    public Task<Result<HostAgentOverview>> GetAsync(Actor actor, CancellationToken cancellationToken) =>
        Run(actor, nameof(GetAsync), null, () => new HostAgentOverview([Agent()], [Source()]));

    public Task<Result<HostAgentPairing>> CreateAgentAsync(Actor actor, HostAgentInput input, CancellationToken cancellationToken) =>
        Run(actor, nameof(CreateAgentAsync), input, () => new HostAgentPairing(Agent(), "K7QD-3MZP-W9XA", Now.AddMinutes(15)));

    public Task<Result<HostAgent>> UpdateAgentAsync(Actor actor, Guid agentId, HostAgentInput input, CancellationToken cancellationToken) =>
        Run(actor, nameof(UpdateAgentAsync), (agentId, input), Agent);

    public Task<Result<HostAgentPairing>> ResetPairingAsync(Actor actor, Guid agentId, CancellationToken cancellationToken) =>
        Run(actor, nameof(ResetPairingAsync), agentId, () => new HostAgentPairing(Agent(), "K7QD-3MZP-W9XA", Now.AddMinutes(15)));

    public Task<Result<Unit>> RemoveAgentAsync(Actor actor, Guid agentId, CancellationToken cancellationToken) =>
        Run(actor, nameof(RemoveAgentAsync), agentId, () => Unit.Value);

    public Task<Result<HostLoginStart>> StartLoginAsync(Actor actor, Guid agentId, AiProviderKind engine, CancellationToken cancellationToken) =>
        Run(actor, nameof(StartLoginAsync), (agentId, engine), () => new HostLoginStart(Login(), "H4TT-9KQ2-MM7C"));

    public Task<Result<Unit>> CancelLoginAsync(Actor actor, Guid loginId, CancellationToken cancellationToken) =>
        Run(actor, nameof(CancelLoginAsync), loginId, () => Unit.Value);

    public Task<Result<Unit>> LogoutAsync(Actor actor, Guid agentId, AiProviderKind engine, CancellationToken cancellationToken) =>
        Run(actor, nameof(LogoutAsync), (agentId, engine), () => Unit.Value);

    public Task<Result<HostSource>> CreateSourceAsync(Actor actor, HostSourceInput input, CancellationToken cancellationToken) =>
        Run(actor, nameof(CreateSourceAsync), input, Source);

    public Task<Result<HostSource>> UpdateSourceAsync(Actor actor, Guid sourceId, HostSourceInput input, CancellationToken cancellationToken) =>
        Run(actor, nameof(UpdateSourceAsync), (sourceId, input), Source);

    public Task<Result<Unit>> DeleteSourceAsync(Actor actor, Guid sourceId, CancellationToken cancellationToken) =>
        Run(actor, nameof(DeleteSourceAsync), sourceId, () => Unit.Value);

    public Task<Result<HostSourceUsage>> GetUsageAsync(Actor actor, Guid sourceId, string? month, CancellationToken cancellationToken) =>
        Run(actor, nameof(GetUsageAsync), (sourceId, month), () => new HostSourceUsage(
            month ?? "2026-11",
            new HostUsageTotals(10, 7, 2, 1, 1000, 500),
            [new HostPersonUsage(TestUsers.Member.PersonId, new HostUsageTotals(6, 5, 1, 0, 600, 300))],
            [new HostProjectUsage("WEB", new HostUsageTotals(10, 7, 2, 1, 1000, 500))]));

    private Task<Result<T>> Run<T>(Actor actor, string method, object? argument, Func<T> success)
    {
        _calls.Enqueue((actor, method, argument));
        return Task.FromResult(OnCall(actor, method) is { } failure ? (Result<T>)failure : (Result<T>)success());
    }
}

/// <summary>測試用的 worker 那一頭。<see cref="Credential"/> 是唯一認得的機器憑證。</summary>
internal sealed class FakeHostAgentWorkerService : IHostAgentWorkerService
{
    public const string Credential = "kha_valid-credential-for-tests";

    /// <summary>配對時存下的機器編號：每個 kha_ 請求的 <c>X-Kumi-Machine</c> 要等於它。</summary>
    public const string MachineKey = "machine-0001";

    public static readonly Guid JobId = Guid.Parse("77777777-7777-7777-7777-777777777771");

    public static readonly Guid OtherOrganizationId = Guid.Parse("0190a000-0000-7000-8000-0000000000ff");

    private readonly ConcurrentQueue<(string Method, HostAgentIdentity? Agent, object? Argument)> _calls = new();
    private int _authenticateCalls;

    public static HostAgentIdentity Identity { get; } = new(TestUsers.OrganizationId, FakeHostAgentAdminService.AgentId);

    public Func<HostPairingRequest, Result<HostPairingResult>> OnPair { get; set; } = request => request.Code == "K7QD-3MZP-W9XA"
        ? new HostPairingResult(FakeHostAgentAdminService.AgentId, "主機一號", Credential)
        : new Failure(FailureKind.Invalid, AiErrorCodes.HostPairingInvalid, "配對碼不對或已經失效。");

    public Func<HostAgentIdentity, HostAgentCheckIn, int, Result<HostCheckInResult>> OnCheckIn { get; set; } =
        (_, _, _) => new HostCheckInResult(null, [], false);

    public Func<HostAgentIdentity, Guid, int, int, IReadOnlyList<LocalAgentEvent>, Result<HostReportAck>> OnReport { get; set; } =
        (_, _, _, firstSeq, events) => new HostReportAck(false, firstSeq + events.Count);

    public Func<HostAgentIdentity, Guid, int, LocalAgentFinish, Result<Unit>> OnFinish { get; set; } = (_, _, _, _) => Unit.Value;

    public Func<HostAgentIdentity, Guid, int, Guid, Result<AssistantAttachmentContent>> OnOpenAttachment { get; set; } =
        (_, _, _, _) => new Failure(FailureKind.NotFound, AiErrorCodes.AttachmentNotFound, "找不到這個附件。");

    public Func<HostAgentIdentity, string, Result<HostLoginClaim>> OnClaim { get; set; } = (_, _) =>
        new HostLoginClaim(FakeHostAgentAdminService.LoginId, AiProviderKind.ClaudeCode, FakeHostAgentAdminService.Now.AddMinutes(15));

    public Func<HostAgentIdentity, Guid, HostLoginReport, Result<HostLoginCompleted>> OnComplete { get; set; } = (_, loginId, _) =>
        new HostLoginCompleted(loginId, AiProviderKind.ClaudeCode);

    public IReadOnlyList<(string Method, HostAgentIdentity? Agent, object? Argument)> Calls => _calls.ToArray();

    /// <summary>驗證機器憑證被問了幾次：帶了不像憑證的東西時要在問資料庫之前擋下。</summary>
    public int AuthenticateCalls => Volatile.Read(ref _authenticateCalls);

    public static HostJob Job() => new(
        JobId,
        3,
        AiProviderKind.ClaudeCode,
        "default",
        AiEffort.High,
        "WEB",
        "系統提示",
        "使用者：嗨",
        ["get_blueprint", "propose_item_changes"],
        "http://api:8080/mcp",
        13,
        "kjt_secret-tool-token",
        FakeHostAgentAdminService.Now.AddMinutes(6));

    public Task<HostAgentIdentity?> AuthenticateAsync(string credential, string? machineKey, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _authenticateCalls);
        return Task.FromResult<HostAgentIdentity?>(credential == Credential && machineKey == MachineKey ? Identity : null);
    }

    public Task<Result<HostPairingResult>> PairAsync(HostPairingRequest request, CancellationToken cancellationToken)
    {
        _calls.Enqueue((nameof(PairAsync), null, request));
        return Task.FromResult(OnPair(request));
    }

    public Task<Result<HostCheckInResult>> CheckInAsync(
        HostAgentIdentity agent, HostAgentCheckIn checkIn, int capacity, CancellationToken cancellationToken)
    {
        _calls.Enqueue((nameof(CheckInAsync), agent, (checkIn, capacity)));
        return Task.FromResult(OnCheckIn(agent, checkIn, capacity));
    }

    public Task<Result<HostReportAck>> ReportAsync(
        HostAgentIdentity agent, Guid jobId, int lease, int firstSeq, IReadOnlyList<LocalAgentEvent> events, CancellationToken cancellationToken)
    {
        _calls.Enqueue((nameof(ReportAsync), agent, (jobId, lease, firstSeq, events)));
        return Task.FromResult(OnReport(agent, jobId, lease, firstSeq, events));
    }

    public Task<Result<Unit>> FinishAsync(
        HostAgentIdentity agent, Guid jobId, int lease, LocalAgentFinish finish, CancellationToken cancellationToken)
    {
        _calls.Enqueue((nameof(FinishAsync), agent, (jobId, lease, finish)));
        return Task.FromResult(OnFinish(agent, jobId, lease, finish));
    }

    public Task<Result<AssistantAttachmentContent>> OpenJobAttachmentAsync(
        HostAgentIdentity agent, Guid jobId, int lease, Guid attachmentId, CancellationToken cancellationToken)
    {
        _calls.Enqueue((nameof(OpenJobAttachmentAsync), agent, (jobId, lease, attachmentId)));
        return Task.FromResult(OnOpenAttachment(agent, jobId, lease, attachmentId));
    }

    public Task<Result<HostLoginClaim>> ClaimLoginAsync(HostAgentIdentity agent, string code, CancellationToken cancellationToken)
    {
        _calls.Enqueue((nameof(ClaimLoginAsync), agent, code));
        return Task.FromResult(OnClaim(agent, code));
    }

    public Task<Result<HostLoginCompleted>> CompleteLoginAsync(
        HostAgentIdentity agent, Guid loginId, HostLoginReport report, CancellationToken cancellationToken)
    {
        _calls.Enqueue((nameof(CompleteLoginAsync), agent, (loginId, report)));
        return Task.FromResult(OnComplete(agent, loginId, report));
    }
}

/// <summary>測試用的工具授權。<see cref="Token"/> 是唯一認得的授權；提出類的工具用字典記住結果，模擬「同樣的參數只建立一次」。</summary>
internal sealed class FakeHostToolGrants : IHostToolGrants
{
    public const string Token = "kjt_valid-tool-grant-for-tests";

    private readonly ConcurrentQueue<string> _resolveCalls = new();
    private readonly ConcurrentDictionary<(Guid Job, string Tool, string Hash), string?> _writes = new();
    private readonly ConcurrentQueue<(string Tool, string Hash, string? ResultJson)> _completed = new();
    private readonly ConcurrentQueue<(string Tool, string Hash)> _begun = new();

    /// <summary>目前有效的授權；設成 null 模擬工作結束、被取消。</summary>
    public HostToolGrant? Grant { get; set; } = new(
        TestUsers.OrganizationId,
        TestUsers.Member.PersonId,
        false,
        "WEB",
        ["get_item", "get_blueprint", "propose_item_changes", "propose_changeset"],
        FakeHostAgentWorkerService.JobId);

    /// <summary>不為 null 時 <see cref="BeginWriteAsync"/> 一律回它（模擬另一個請求正在建立同樣的提案）。</summary>
    public HostToolReplay? ForcedReplay { get; set; }

    /// <summary>true 時 <see cref="BeginWriteAsync"/> 丟例外（模擬資料庫斷線）。</summary>
    public bool ThrowOnBegin { get; set; }

    public IReadOnlyList<string> ResolveCalls => _resolveCalls.ToArray();

    public IReadOnlyList<(string Tool, string Hash)> BeginCalls => _begun.ToArray();

    public IReadOnlyList<(string Tool, string Hash, string? ResultJson)> CompleteCalls => _completed.ToArray();

    public Task<HostToolGrant?> ResolveAsync(string token, CancellationToken cancellationToken)
    {
        _resolveCalls.Enqueue(token);
        return Task.FromResult(token == Token ? Grant : null);
    }

    public Task<HostToolReplay> BeginWriteAsync(Guid jobId, string tool, string argumentsHash, CancellationToken cancellationToken)
    {
        _begun.Enqueue((tool, argumentsHash));
        if (ThrowOnBegin)
        {
            throw new InvalidOperationException("資料庫斷線（測試）。");
        }

        if (ForcedReplay is { } forced)
        {
            return Task.FromResult(forced);
        }

        var key = (jobId, tool, argumentsHash);
        if (_writes.TryGetValue(key, out var stored))
        {
            return Task.FromResult(stored is null ? HostToolReplay.InProgress : new HostToolReplay(HostToolReplayKind.Replay, stored));
        }

        _writes[key] = null;
        return Task.FromResult(HostToolReplay.Proceed);
    }

    public Task CompleteWriteAsync(Guid jobId, string tool, string argumentsHash, string? resultJson, CancellationToken cancellationToken)
    {
        _completed.Enqueue((tool, argumentsHash, resultJson));
        if (resultJson is null)
        {
            _writes.TryRemove((jobId, tool, argumentsHash), out _);
        }
        else
        {
            _writes[(jobId, tool, argumentsHash)] = resultJson;
        }

        return Task.CompletedTask;
    }
}
