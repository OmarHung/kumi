using System.Collections.Concurrent;
using System.Threading.Channels;
using PmTool.Abstractions;
using PmTool.Ai;

namespace PmTool.Api.Tests.Support;

/// <summary>
/// 測試用的 AI 助理：記住每次呼叫的參數，回應可以換掉。預設的回覆是一段文字加結束；
/// 要看逐段送出、中斷與心跳時換成 <see cref="ControlledRun"/>。
/// </summary>
internal sealed class FakeAssistantService : IAssistantService
{
    public static readonly Guid ConversationId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static readonly Guid ConnectionId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static readonly DateTimeOffset At = new(2026, 11, 23, 3, 0, 0, TimeSpan.Zero);

    private readonly ConcurrentQueue<(Actor Actor, string ProjectKey)> _listCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string ProjectKey)> _createCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string ProjectKey, Guid Id)> _getCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string ProjectKey, Guid Id)> _deleteCalls = new();
    private readonly ConcurrentQueue<StartCall> _startCalls = new();

    public Func<Actor, string, Result<IReadOnlyList<AssistantConversationSummary>>> OnList { get; set; } =
        (_, _) => new List<AssistantConversationSummary> { Summary() };

    public Func<Actor, string, Result<AssistantConversationSummary>> OnCreate { get; set; } = (_, _) => Summary();

    public Func<Actor, string, Guid, Result<AssistantConversation>> OnGet { get; set; } =
        (_, _, _) => new AssistantConversation(Summary(messageCount: 2), [UserMessage(), AssistantReply()]);

    public Func<Actor, string, Guid, Result<Unit>> OnDelete { get; set; } = (_, _, _) => Unit.Value;

    public Func<StartCall, Result<AssistantRun>> OnStart { get; set; } = _ => QuickRun();

    public IReadOnlyList<(Actor Actor, string ProjectKey)> ListCalls => _listCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string ProjectKey)> CreateCalls => _createCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string ProjectKey, Guid Id)> GetCalls => _getCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string ProjectKey, Guid Id)> DeleteCalls => _deleteCalls.ToArray();

    public IReadOnlyList<StartCall> StartCalls => _startCalls.ToArray();

    public static AssistantConversationSummary Summary(string? title = "把驗收拆成兩段", int messageCount = 0, bool isRunning = false) =>
        new(ConversationId, title, messageCount, isRunning, At, At.AddMinutes(5));

    public static AssistantMessage UserMessage(string text = "把驗收拆成內部驗收和客戶驗收") =>
        new(Guid.Parse("33333333-3333-3333-3333-333333333331"), AssistantRole.User, [new AssistantTextPart(text)], At);

    public static AssistantMessage AssistantReply(
        AssistantMessageStatus status = AssistantMessageStatus.Complete, string? error = null, Guid? id = null) =>
        new(
            id ?? Guid.Parse("33333333-3333-3333-3333-333333333332"),
            AssistantRole.Assistant,
            [
                new AssistantTextPart("好，我先讀目前的設定。"),
                new AssistantToolPart("c1", "propose_changeset", AssistantToolStatus.Done, 12, null),
            ],
            At.AddMinutes(1))
        {
            Status = status,
            Error = error,
            Selection = new AiSelection(ConnectionId, "claude-opus-5-5", AiEffort.High),
            ConnectionName = "Anthropic",
            Usage = new AssistantUsage(1200, 300),
        };

    /// <summary>一則短的回覆：一段文字、一次工具、結束。</summary>
    public static Result<AssistantRun> QuickRun(Guid? assistantId = null)
    {
        var id = assistantId ?? Guid.Parse("33333333-3333-3333-3333-333333333332");
        var tool = new AssistantToolPart("c1", "propose_changeset", AssistantToolStatus.Running, null, null);
        return new AssistantRun(
            UserMessage(),
            id,
            ToAsync(
                new AssistantTextDelta("好，我先讀目前的設定。"),
                new AssistantToolUpdate(tool),
                new AssistantToolUpdate(tool with { Status = AssistantToolStatus.Done, ChangeSetNumber = 12 }),
                new AssistantRunCompleted(AssistantReply(id: id))));
    }

    public Task<Result<IReadOnlyList<AssistantConversationSummary>>> ListAsync(
        Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        _listCalls.Enqueue((actor, projectKey));
        return Task.FromResult(OnList(actor, projectKey));
    }

    public Task<Result<AssistantConversationSummary>> CreateAsync(Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        _createCalls.Enqueue((actor, projectKey));
        return Task.FromResult(OnCreate(actor, projectKey));
    }

    public Task<Result<AssistantConversation>> GetAsync(
        Actor actor, string projectKey, Guid conversationId, CancellationToken cancellationToken)
    {
        _getCalls.Enqueue((actor, projectKey, conversationId));
        return Task.FromResult(OnGet(actor, projectKey, conversationId));
    }

    public Task<Result<Unit>> DeleteAsync(Actor actor, string projectKey, Guid conversationId, CancellationToken cancellationToken)
    {
        _deleteCalls.Enqueue((actor, projectKey, conversationId));
        return Task.FromResult(OnDelete(actor, projectKey, conversationId));
    }

    public Task<Result<AssistantRun>> StartAsync(
        Actor actor, string projectKey, Guid conversationId, AssistantMessageInput input, CancellationToken cancellationToken)
    {
        var call = new StartCall(actor, projectKey, conversationId, input);
        _startCalls.Enqueue(call);
        return Task.FromResult(OnStart(call));
    }

    private static async IAsyncEnumerable<AssistantRunEvent> ToAsync(params AssistantRunEvent[] events)
    {
        foreach (var runEvent in events)
        {
            yield return runEvent;
            await Task.Yield();
        }
    }
}

internal sealed record StartCall(Actor Actor, string ProjectKey, Guid ConversationId, AssistantMessageInput Input);

/// <summary>
/// 一則由測試控制節奏的回覆：測試決定什麼時候送出哪個事件，讀的人（API）取消或離開時留下記錄。
/// 取消時學真的模組：送出「已停止」的最後一個事件再結束。只能讀一次。
/// </summary>
internal sealed class ControlledRun : IAsyncEnumerable<AssistantRunEvent>
{
    private readonly Channel<AssistantRunEvent> _channel = Channel.CreateUnbounded<AssistantRunEvent>();
    private readonly TaskCompletionSource _cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>讀的人的取消 token 被取消了。</summary>
    public Task Cancelled => _cancelled.Task;

    /// <summary>讀的人離開了（列舉器被 Dispose）。</summary>
    public Task Disposed => _disposed.Task;

    public AssistantRun ToRun(Guid? assistantId = null) =>
        new(FakeAssistantService.UserMessage(), assistantId ?? Guid.Parse("33333333-3333-3333-3333-333333333332"), this);

    public void Send(AssistantRunEvent runEvent) => _channel.Writer.TryWrite(runEvent);

    public void Finish(AssistantMessage? message = null)
    {
        _channel.Writer.TryWrite(new AssistantRunCompleted(message ?? FakeAssistantService.AssistantReply()));
        _channel.Writer.TryComplete();
    }

    public IAsyncEnumerator<AssistantRunEvent> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        var registration = cancellationToken.Register(() =>
        {
            _cancelled.TrySetResult();
            _channel.Writer.TryWrite(new AssistantRunCompleted(
                FakeAssistantService.AssistantReply(AssistantMessageStatus.Stopped)));
            _channel.Writer.TryComplete();
        });
        return new Enumerator(_channel.Reader, registration, cancellationToken, _cancelled, _disposed);
    }

    private sealed class Enumerator(
        ChannelReader<AssistantRunEvent> reader,
        CancellationTokenRegistration registration,
        CancellationToken cancellationToken,
        TaskCompletionSource cancelled,
        TaskCompletionSource disposed)
        : IAsyncEnumerator<AssistantRunEvent>
    {
        public AssistantRunEvent Current { get; private set; } = null!;

        public async ValueTask<bool> MoveNextAsync()
        {
            if (!await reader.WaitToReadAsync(CancellationToken.None) || !reader.TryRead(out var next))
            {
                return false;
            }

            Current = next;
            return true;
        }

        public async ValueTask DisposeAsync()
        {
            // 取消的回呼依登記的反向順序執行：讀的那一邊先收到取消、結束並 Dispose 時，上面登記的回呼可能還沒輪到就被註銷了。
            // 「讀的 token 被取消了」看的是 token 本身，所以 Dispose 時再看一次，不靠回呼的順序。
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled.TrySetResult();
            }

            await registration.DisposeAsync();
            disposed.TrySetResult();
        }
    }
}
