using System.Collections.Concurrent;
using PmTool.Abstractions;
using PmTool.Ai;

namespace PmTool.Api.Tests.Support;

/// <summary>
/// 測試用的 AI 設定：記住每次呼叫的參數，回應可以換掉。預設是已設定好的一個 Anthropic 連線
/// （金鑰只說有沒有與最後四個字，和真的模組一樣不會讀出來）。
/// </summary>
internal sealed class FakeAiSettingsService : IAiSettingsService
{
    public static readonly Guid ConnectionId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly ConcurrentQueue<Actor> _getCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, AiSettingsInput Input)> _saveCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, AiConnectionInput Input)> _createCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, Guid Id, AiConnectionInput Input)> _updateCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, Guid Id)> _refreshCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, Guid Id)> _deleteCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, AiDiscoveryInput Input)> _discoverCalls = new();

    public Func<Actor, Result<AiSettings>> OnGet { get; set; } = _ => StoredSettings();

    public Func<Actor, AiSettingsInput, Result<AiSettings>> OnSave { get; set; } = (_, input) => StoredSettings(input);

    public Func<Actor, AiConnectionInput, Result<AiConnection>> OnCreate { get; set; } = (_, input) => Stored(input);

    public Func<Actor, Guid, AiConnectionInput, Result<AiConnection>> OnUpdate { get; set; } = (_, id, input) => Stored(input, id);

    public Func<Actor, Guid, Result<AiConnection>> OnRefresh { get; set; } = (_, id) => Stored(Input(), id);

    public Func<Actor, Guid, Result<Unit>> OnDelete { get; set; } = (_, _) => Unit.Value;

    public Func<Actor, AiDiscoveryInput, Result<IReadOnlyList<AiDiscoveredModel>>> OnDiscover { get; set; } =
        (_, _) => new List<AiDiscoveredModel> { new("claude-opus-5-5", "Claude Opus 5.5", [AiEffort.Low, AiEffort.High, AiEffort.Max]) };

    public IReadOnlyList<Actor> GetCalls => _getCalls.ToArray();

    public IReadOnlyList<(Actor Actor, AiSettingsInput Input)> SaveCalls => _saveCalls.ToArray();

    public IReadOnlyList<(Actor Actor, AiConnectionInput Input)> CreateCalls => _createCalls.ToArray();

    public IReadOnlyList<(Actor Actor, Guid Id, AiConnectionInput Input)> UpdateCalls => _updateCalls.ToArray();

    public IReadOnlyList<(Actor Actor, Guid Id)> RefreshCalls => _refreshCalls.ToArray();

    public IReadOnlyList<(Actor Actor, Guid Id)> DeleteCalls => _deleteCalls.ToArray();

    public IReadOnlyList<(Actor Actor, AiDiscoveryInput Input)> DiscoverCalls => _discoverCalls.ToArray();

    public static AiConnectionInput Input() =>
        new("Anthropic", AiProviderKind.Anthropic, null, "sk-ant-key-a1b2", ["claude-opus-5-5"], []);

    /// <summary>照輸入組出會讀回來的連線：金鑰不見了，只留有沒有與最後四個字。</summary>
    public static AiConnection Stored(AiConnectionInput input, Guid? id = null, Guid? updatedBy = null) => new(
        id ?? ConnectionId,
        input.Name,
        input.Kind,
        input.BaseUrl,
        !string.IsNullOrEmpty(input.ApiKey) || input.ApiKey is null,
        input.ApiKey is { Length: >= 8 } key ? key[^4..] : null,
        [
            new AiModel("claude-opus-5-5", "Claude Opus 5.5", [AiEffort.Low, AiEffort.High, AiEffort.Max], input.EnabledModels.Contains("claude-opus-5-5"), false),
            .. input.CustomModels.Select(m => new AiModel(m, m, [], input.EnabledModels.Contains(m), true)),
        ],
        new DateTimeOffset(2026, 11, 23, 3, 0, 0, TimeSpan.Zero),
        new AiUsage("2026-11", 120000, 8000, 12),
        new DateTimeOffset(2026, 11, 22, 3, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 11, 23, 3, 0, 0, TimeSpan.Zero),
        updatedBy ?? TestUsers.Owner.PersonId);

    public static AiSettings StoredSettings(AiSettingsInput? input = null, Guid? updatedBy = null) => new(
        input?.Enabled ?? true,
        input is null ? new AiSelection(ConnectionId, "claude-opus-5-5", AiEffort.High) : input.Default,
        [Stored(Input(), updatedBy: updatedBy)],
        new DateTimeOffset(2026, 11, 23, 3, 0, 0, TimeSpan.Zero),
        updatedBy ?? TestUsers.Owner.PersonId)
    {
        Limits = input?.Limits ?? AiUsageLimits.Default,
        MonthUsage = new AiMonthUsage("2026-11", 12, 128000),
    };

    public Task<Result<AiSettings>> GetAsync(Actor actor, CancellationToken cancellationToken)
    {
        _getCalls.Enqueue(actor);
        return Task.FromResult(OnGet(actor));
    }

    public Task<Result<AiSettings>> SaveAsync(Actor actor, AiSettingsInput input, CancellationToken cancellationToken)
    {
        _saveCalls.Enqueue((actor, input));
        return Task.FromResult(OnSave(actor, input));
    }

    public Task<Result<AiConnection>> CreateConnectionAsync(Actor actor, AiConnectionInput input, CancellationToken cancellationToken)
    {
        _createCalls.Enqueue((actor, input));
        return Task.FromResult(OnCreate(actor, input));
    }

    public Task<Result<AiConnection>> UpdateConnectionAsync(
        Actor actor, Guid connectionId, AiConnectionInput input, CancellationToken cancellationToken)
    {
        _updateCalls.Enqueue((actor, connectionId, input));
        return Task.FromResult(OnUpdate(actor, connectionId, input));
    }

    public Task<Result<AiConnection>> RefreshModelsAsync(Actor actor, Guid connectionId, CancellationToken cancellationToken)
    {
        _refreshCalls.Enqueue((actor, connectionId));
        return Task.FromResult(OnRefresh(actor, connectionId));
    }

    public Task<Result<Unit>> DeleteConnectionAsync(Actor actor, Guid connectionId, CancellationToken cancellationToken)
    {
        _deleteCalls.Enqueue((actor, connectionId));
        return Task.FromResult(OnDelete(actor, connectionId));
    }

    public Task<Result<IReadOnlyList<AiDiscoveredModel>>> DiscoverModelsAsync(
        Actor actor, AiDiscoveryInput input, CancellationToken cancellationToken)
    {
        _discoverCalls.Enqueue((actor, input));
        return Task.FromResult(OnDiscover(actor, input));
    }
}

/// <summary>測試用的「成員能選的 AI」。預設是沒開。</summary>
internal sealed class FakeAiOptionsService : IAiOptionsService
{
    private readonly ConcurrentQueue<Actor> _getCalls = new();

    public Func<Actor, AiOptions> OnGet { get; set; } = _ => new AiOptions(false, [], null);

    public IReadOnlyList<Actor> GetCalls => _getCalls.ToArray();

    public Task<AiOptions> GetAsync(Actor actor, CancellationToken cancellationToken)
    {
        _getCalls.Enqueue(actor);
        return Task.FromResult(OnGet(actor));
    }
}

/// <summary>測試用的「自己的 AI 連線」：記住每次呼叫的參數，回應可以換掉。預設照輸入存下來。</summary>
internal sealed class FakePersonalAiConnectionService : IPersonalAiConnectionService
{
    public static readonly Guid ConnectionId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly ConcurrentQueue<Actor> _listCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, AiConnectionInput Input)> _createCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, Guid Id, AiConnectionInput Input)> _updateCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, Guid Id)> _refreshCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, Guid Id)> _deleteCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, AiDiscoveryInput Input)> _discoverCalls = new();

    public Func<Actor, IReadOnlyList<AiConnection>> OnList { get; set; } = actor => [Stored(Input(), actor)];

    public Func<Actor, AiConnectionInput, Result<AiConnection>> OnCreate { get; set; } = (actor, input) => Stored(input, actor);

    public Func<Actor, Guid, AiConnectionInput, Result<AiConnection>> OnUpdate { get; set; } = (actor, id, input) => Stored(input, actor, id);

    public Func<Actor, Guid, Result<AiConnection>> OnRefresh { get; set; } = (actor, id) => Stored(Input(), actor, id);

    public Func<Actor, Guid, Result<Unit>> OnDelete { get; set; } = (_, _) => Unit.Value;

    public Func<Actor, AiDiscoveryInput, Result<IReadOnlyList<AiDiscoveredModel>>> OnDiscover { get; set; } =
        (_, _) => new List<AiDiscoveredModel> { new("anthropic/claude-opus-5-5", "Claude Opus 5.5", [AiEffort.Low, AiEffort.High]) };

    public IReadOnlyList<Actor> ListCalls => _listCalls.ToArray();

    public IReadOnlyList<(Actor Actor, AiConnectionInput Input)> CreateCalls => _createCalls.ToArray();

    public IReadOnlyList<(Actor Actor, Guid Id, AiConnectionInput Input)> UpdateCalls => _updateCalls.ToArray();

    public IReadOnlyList<(Actor Actor, Guid Id)> RefreshCalls => _refreshCalls.ToArray();

    public IReadOnlyList<(Actor Actor, Guid Id)> DeleteCalls => _deleteCalls.ToArray();

    public IReadOnlyList<(Actor Actor, AiDiscoveryInput Input)> DiscoverCalls => _discoverCalls.ToArray();

    public static AiConnectionInput Input() =>
        new("我的 OpenRouter", AiProviderKind.Openrouter, null, "sk-or-v1-key-9z8y", ["anthropic/claude-opus-5-5"], []);

    public static AiConnection Stored(AiConnectionInput input, Actor actor, Guid? id = null) =>
        FakeAiSettingsService.Stored(input, id ?? ConnectionId, actor.PersonId);

    public Task<IReadOnlyList<AiConnection>> ListAsync(Actor actor, CancellationToken cancellationToken)
    {
        _listCalls.Enqueue(actor);
        return Task.FromResult(OnList(actor));
    }

    public Task<Result<AiConnection>> CreateAsync(Actor actor, AiConnectionInput input, CancellationToken cancellationToken)
    {
        _createCalls.Enqueue((actor, input));
        return Task.FromResult(OnCreate(actor, input));
    }

    public Task<Result<AiConnection>> UpdateAsync(Actor actor, Guid connectionId, AiConnectionInput input, CancellationToken cancellationToken)
    {
        _updateCalls.Enqueue((actor, connectionId, input));
        return Task.FromResult(OnUpdate(actor, connectionId, input));
    }

    public Task<Result<AiConnection>> RefreshModelsAsync(Actor actor, Guid connectionId, CancellationToken cancellationToken)
    {
        _refreshCalls.Enqueue((actor, connectionId));
        return Task.FromResult(OnRefresh(actor, connectionId));
    }

    public Task<Result<Unit>> DeleteAsync(Actor actor, Guid connectionId, CancellationToken cancellationToken)
    {
        _deleteCalls.Enqueue((actor, connectionId));
        return Task.FromResult(OnDelete(actor, connectionId));
    }

    public Task<Result<IReadOnlyList<AiDiscoveredModel>>> DiscoverModelsAsync(
        Actor actor, AiDiscoveryInput input, CancellationToken cancellationToken)
    {
        _discoverCalls.Enqueue((actor, input));
        return Task.FromResult(OnDiscover(actor, input));
    }
}

/// <summary>測試用的「用 OpenRouter 登入」：記住參數，回應可以換掉。</summary>
internal sealed class FakeOpenRouterLoginService : IOpenRouterLoginService
{
    private readonly ConcurrentQueue<(Actor Actor, AiConnectionScope Scope, string CallbackUrl)> _startCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string Code, string State)> _completeCalls = new();

    public Func<Actor, AiConnectionScope, string, Result<string>> OnStart { get; set; } =
        (_, _, _) => "https://openrouter.ai/auth?callback_url=x&state=s";

    public Func<Actor, string, string, Result<OpenRouterLoginResult>> OnComplete { get; set; } = (actor, _, _) =>
        new OpenRouterLoginResult(AiConnectionScope.Personal, FakePersonalAiConnectionService.Stored(FakePersonalAiConnectionService.Input(), actor));

    public IReadOnlyList<(Actor Actor, AiConnectionScope Scope, string CallbackUrl)> StartCalls => _startCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string Code, string State)> CompleteCalls => _completeCalls.ToArray();

    public Task<Result<string>> StartAsync(Actor actor, AiConnectionScope scope, string callbackUrl, CancellationToken cancellationToken)
    {
        _startCalls.Enqueue((actor, scope, callbackUrl));
        return Task.FromResult(OnStart(actor, scope, callbackUrl));
    }

    public Task<Result<OpenRouterLoginResult>> CompleteAsync(Actor actor, string code, string state, CancellationToken cancellationToken)
    {
        _completeCalls.Enqueue((actor, code, state));
        return Task.FromResult(OnComplete(actor, code, state));
    }
}
