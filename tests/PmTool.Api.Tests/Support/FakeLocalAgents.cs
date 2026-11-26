using System.Collections.Concurrent;
using PmTool.Abstractions;
using PmTool.Ai;

namespace PmTool.Api.Tests.Support;

/// <summary>測試用的本機代理那一頭：記住每次呼叫的參數，回應可以換掉。預設是登記成功、沒有工作。</summary>
internal sealed class FakeLocalAgentService : ILocalAgentService
{
    public static readonly Guid JobId = Guid.Parse("44444444-4444-4444-4444-444444444441");

    private readonly ConcurrentQueue<(Actor Actor, LocalAgentCheckIn CheckIn, int Capacity)> _checkInCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, Guid JobId, IReadOnlyList<LocalAgentEvent> Events)> _reportCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, Guid JobId, LocalAgentFinish Finish)> _finishCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, Guid JobId, Guid AttachmentId)> _openAttachmentCalls = new();

    public Func<Actor, LocalAgentCheckIn, int, CancellationToken, Task<Result<LocalAgentJob?>>> OnCheckIn { get; set; } =
        (_, _, _, _) => Task.FromResult<Result<LocalAgentJob?>>((LocalAgentJob?)null);

    public Func<Actor, Guid, IReadOnlyList<LocalAgentEvent>, Result<LocalAgentReportAck>> OnReport { get; set; } =
        (_, _, _) => new LocalAgentReportAck(false);

    public Func<Actor, Guid, LocalAgentFinish, Result<Unit>> OnFinish { get; set; } = (_, _, _) => Unit.Value;

    public Func<Actor, Guid, Guid, Result<AssistantAttachmentContent>> OnOpenAttachment { get; set; } =
        (_, _, _) => new Failure(FailureKind.NotFound, AiErrorCodes.AttachmentNotFound, "找不到這個附件。");

    public IReadOnlyList<(Actor Actor, Guid JobId, Guid AttachmentId)> OpenAttachmentCalls => _openAttachmentCalls.ToArray();

    public IReadOnlyList<(Actor Actor, LocalAgentCheckIn CheckIn, int Capacity)> CheckInCalls => _checkInCalls.ToArray();

    public IReadOnlyList<(Actor Actor, Guid JobId, IReadOnlyList<LocalAgentEvent> Events)> ReportCalls => _reportCalls.ToArray();

    public IReadOnlyList<(Actor Actor, Guid JobId, LocalAgentFinish Finish)> FinishCalls => _finishCalls.ToArray();

    public static LocalAgentJob Job() => new(
        JobId,
        AiProviderKind.ClaudeCode,
        "sonnet",
        AiEffort.High,
        "WEB",
        "系統提示",
        "使用者：嗨",
        ["get_blueprint", "propose_changeset"],
        "https://kumi.example.com/mcp",
        13);

    public Task<Result<LocalAgentJob?>> CheckInAsync(
        Actor actor, LocalAgentCheckIn checkIn, int capacity, CancellationToken cancellationToken)
    {
        _checkInCalls.Enqueue((actor, checkIn, capacity));
        return OnCheckIn(actor, checkIn, capacity, cancellationToken);
    }

    public Task<Result<LocalAgentReportAck>> ReportAsync(
        Actor actor, Guid jobId, IReadOnlyList<LocalAgentEvent> events, CancellationToken cancellationToken)
    {
        _reportCalls.Enqueue((actor, jobId, events));
        return Task.FromResult(OnReport(actor, jobId, events));
    }

    public Task<Result<Unit>> FinishAsync(Actor actor, Guid jobId, LocalAgentFinish finish, CancellationToken cancellationToken)
    {
        _finishCalls.Enqueue((actor, jobId, finish));
        return Task.FromResult(OnFinish(actor, jobId, finish));
    }

    public Task<Result<AssistantAttachmentContent>> OpenJobAttachmentAsync(
        Actor actor, Guid jobId, Guid attachmentId, CancellationToken cancellationToken)
    {
        _openAttachmentCalls.Enqueue((actor, jobId, attachmentId));
        return Task.FromResult(OnOpenAttachment(actor, jobId, attachmentId));
    }
}

/// <summary>測試用的「我的帳號」電腦清單。預設有一台在線的電腦。</summary>
internal sealed class FakeLocalAgentDirectory : ILocalAgentDirectory
{
    public static readonly Guid MachineId = Guid.Parse("55555555-5555-5555-5555-555555555551");

    public static readonly DateTimeOffset SeenAt = new(2026, 11, 23, 3, 0, 0, TimeSpan.Zero);

    private readonly ConcurrentQueue<Actor> _listCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, Guid Id)> _removeCalls = new();

    public Func<Actor, IReadOnlyList<LocalAgentMachine>> OnList { get; set; } = _ =>
        [
            new LocalAgentMachine(
                MachineId,
                "辦公室的 Mac",
                "0.9.0",
                [new LocalAgentEngineSummary(AiProviderKind.ClaudeCode, "2.1.330"), new LocalAgentEngineSummary(AiProviderKind.Codex, "0.163.0")],
                true,
                SeenAt),
        ];

    public Func<Actor, Guid, Result<Unit>> OnRemove { get; set; } = (_, _) => Unit.Value;

    public IReadOnlyList<Actor> ListCalls => _listCalls.ToArray();

    public IReadOnlyList<(Actor Actor, Guid Id)> RemoveCalls => _removeCalls.ToArray();

    public Task<IReadOnlyList<LocalAgentMachine>> ListAsync(Actor actor, CancellationToken cancellationToken)
    {
        _listCalls.Enqueue(actor);
        return Task.FromResult(OnList(actor));
    }

    public Task<Result<Unit>> RemoveAsync(Actor actor, Guid machineId, CancellationToken cancellationToken)
    {
        _removeCalls.Enqueue((actor, machineId));
        return Task.FromResult(OnRemove(actor, machineId));
    }
}
