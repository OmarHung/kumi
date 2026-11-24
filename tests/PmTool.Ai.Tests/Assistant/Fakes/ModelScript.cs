using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using PmTool.Ai.Providers;

namespace PmTool.Ai.Tests.Assistant.Fakes;

/// <summary>模型的一次回應（一次呼叫 <c>GetStreamingResponseAsync</c>）：依序送出的步驟。</summary>
internal sealed class Turn
{
    private readonly List<Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>> _steps = [];

    public IReadOnlyList<Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>> Steps => _steps;

    public static Turn Create() => new();

    public Turn Text(string text) => Update(new ChatResponseUpdate(ChatRole.Assistant, text));

    public Turn Call(string callId, string name, object? arguments = null) => Update(new ChatResponseUpdate(
        ChatRole.Assistant,
        [new FunctionCallContent(callId, name, ToDictionary(arguments))])
    {
        FinishReason = ChatFinishReason.ToolCalls,
    });

    public Turn Reasoning(string text) => Update(new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent(text)]));

    public Turn Usage(long input, long output) => Update(new ChatResponseUpdate
    {
        Role = ChatRole.Assistant,
        Contents = [new UsageContent(new UsageDetails { InputTokenCount = input, OutputTokenCount = output })],
    });

    public Turn Finish(ChatFinishReason reason) => Update(new ChatResponseUpdate { Role = ChatRole.Assistant, FinishReason = reason });

    public Turn Update(ChatResponseUpdate update) => Step(_ => Single(update));

    /// <summary>停在這裡，直到讀的人取消（或逾時）。</summary>
    public Turn Hang() => Step(Hanging);

    /// <summary>停在這裡，直到測試放行；取消時丟 OperationCanceledException。</summary>
    public Turn WaitFor(Task gate) => Step(token => Waiting(gate, token));

    /// <summary>跑到這裡時執行一段程式（例如弄壞收尾要用的東西）。</summary>
    public Turn Do(Action action) => Step(_ => Acting(action));

    public Turn Throw(Exception exception) => Step(_ => Throwing(exception));

    private Turn Step(Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> step)
    {
        _steps.Add(step);
        return this;
    }

    private static Dictionary<string, object?>? ToDictionary(object? arguments) => arguments switch
    {
        null => null,
        Dictionary<string, object?> dictionary => dictionary,
        _ => System.Text.Json.JsonSerializer.SerializeToElement(arguments).EnumerateObject()
            .ToDictionary(p => p.Name, p => (object?)p.Value.Clone()),
    };

    private static async IAsyncEnumerable<ChatResponseUpdate> Single(ChatResponseUpdate update)
    {
        yield return update;
        await Task.Yield();
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> Hanging([EnumeratorCancellation] CancellationToken token)
    {
        await Task.Delay(Timeout.Infinite, token);
        yield break;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> Waiting(Task gate, [EnumeratorCancellation] CancellationToken token)
    {
        await gate.WaitAsync(token);
        yield break;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> Acting(Action action)
    {
        action();
        await Task.Yield();
        yield break;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> Throwing(Exception exception)
    {
        await Task.Yield();
        ExceptionDispatchInfo.Capture(exception).Throw();
        yield break;
    }
}

/// <summary>送給模型的一次呼叫：訊息與選項。</summary>
internal sealed record ModelCall(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options);

/// <summary>模型的腳本：每呼叫一次取下一個 <see cref="Turn"/>；腳本用完還被呼叫就丟例外，測試才看得到多呼叫了。</summary>
internal sealed class ModelScript
{
    private readonly ConcurrentQueue<Turn> _turns = new();
    private readonly ConcurrentQueue<ModelCall> _calls = new();

    public IReadOnlyList<ModelCall> Calls => _calls.ToArray();

    public ModelScript Enqueue(Turn turn)
    {
        _turns.Enqueue(turn);
        return this;
    }

    public ModelScript Reply(string text) => Enqueue(Turn.Create().Text(text).Finish(ChatFinishReason.Stop));

    public async IAsyncEnumerable<ChatResponseUpdate> NextAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _calls.Enqueue(new ModelCall([.. messages], options));
        if (!_turns.TryDequeue(out var turn))
        {
            throw new InvalidOperationException("腳本裡沒有下一次的回應了。");
        }

        foreach (var step in turn.Steps)
        {
            await foreach (var update in step(cancellationToken).WithCancellation(cancellationToken))
            {
                yield return update;
            }
        }
    }
}

internal sealed class ScriptedChatClient(ModelScript script) : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("助理一律用串流。");

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        script.NextAsync(messages, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

/// <summary>助理測試用的供應商：讀模型清單回固定的一個模型，聊天用腳本。</summary>
internal sealed class ScriptedGateway(ModelScript script) : IAiProviderGateway
{
    public const string ModelId = "model-a";

    private readonly ConcurrentQueue<AiEndpoint> _endpoints = new();

    public IReadOnlyList<AiEndpoint> ChatEndpoints => _endpoints.ToArray();

    public Task<IReadOnlyList<AiDiscoveredModel>> DiscoverAsync(AiEndpoint endpoint, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AiDiscoveredModel>>([new AiDiscoveredModel(ModelId, "模型 A", [AiEffort.Low, AiEffort.High])]);

    public IChatClient CreateChatClient(AiEndpoint endpoint, string modelId)
    {
        _endpoints.Enqueue(endpoint);
        return new ScriptedChatClient(script);
    }
}
