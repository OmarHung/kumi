using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using PmTool.Ai.Providers;

namespace PmTool.Ai.Tests.Settings;

/// <summary>
/// 設定的測試用的假供應商：讀模型清單時照腳本回清單或丟失敗，記住每次拿到的連線。
/// （助理的測試另外有自己的假 gateway，腳本是模型的回覆。）
/// </summary>
internal sealed class StubAiProviderGateway : IAiProviderGateway
{
    private readonly ConcurrentQueue<AiEndpoint> _discoverCalls = new();

    public Func<AiEndpoint, IReadOnlyList<AiDiscoveredModel>> OnDiscover { get; set; } = _ => Models("model-a", "model-b");

    public IReadOnlyList<AiEndpoint> DiscoverCalls => _discoverCalls.ToArray();

    public static IReadOnlyList<AiDiscoveredModel> Models(params string[] ids) =>
        [.. ids.Select(id => new AiDiscoveredModel(id, $"Display {id}", [AiEffort.Low, AiEffort.High]))];

    public Task<IReadOnlyList<AiDiscoveredModel>> DiscoverAsync(AiEndpoint endpoint, CancellationToken cancellationToken)
    {
        _discoverCalls.Enqueue(endpoint);
        return Task.FromResult(OnDiscover(endpoint));
    }

    public IChatClient CreateChatClient(AiEndpoint endpoint, string modelId) =>
        throw new NotSupportedException("設定的測試不會真的呼叫模型。");
}
