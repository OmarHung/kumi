using PmTool.Abstractions;

namespace PmTool.Ai.Tests.Assistant.Fakes;

/// <summary>查項目的服務假物件：記下每次的呼叫（發問的人、對話編號、條件），結果由測試設定。</summary>
internal sealed class FakeItemFinder : IAssistantItemFinder
{
    private readonly List<(Actor Actor, Guid? ConversationId, ItemFindQuery Query)> _calls = [];

    public Func<ItemFindQuery, Result<ItemFindResult>> OnFind { get; set; } = _ => new ItemFindResult([], false);

    public IReadOnlyList<(Actor Actor, Guid? ConversationId, ItemFindQuery Query)> Calls => _calls;

    public Task<Result<ItemFindResult>> FindAsync(Actor actor, Guid? conversationId, ItemFindQuery query, CancellationToken cancellationToken)
    {
        _calls.Add((actor, conversationId, query));
        return Task.FromResult(OnFind(query));
    }
}
