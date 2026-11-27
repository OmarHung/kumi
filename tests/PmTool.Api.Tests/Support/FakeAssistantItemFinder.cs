using System.Collections.Concurrent;
using PmTool.Abstractions;
using PmTool.Ai;

namespace PmTool.Api.Tests.Support;

/// <summary>測試用的跨專案查項目（find_items）：記住每次呼叫（含對話編號），回應可以換掉。</summary>
internal sealed class FakeAssistantItemFinder : IAssistantItemFinder
{
    public static readonly Guid FoundPersonId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private readonly ConcurrentQueue<(Actor Actor, Guid? ConversationId, ItemFindQuery Query)> _calls = new();

    public Func<ItemFindQuery, Result<ItemFindResult>> OnFind { get; set; } = _ => Found();

    public IReadOnlyList<(Actor Actor, Guid? ConversationId, ItemFindQuery Query)> Calls => _calls.ToArray();

    public static Result<ItemFindResult> Found() =>
        new ItemFindResult(
            [
                new FoundItem(
                    "WEB-1", "WEB", "網站", "改首頁", "task", "進行中", "in_progress", new DateOnly(2026, 12, 1), "high",
                    new FoundPerson(FoundPersonId, "小明"), new FoundPerson(TestUsers.Member.PersonId, TestUsers.Member.DisplayName)),
                new FoundItem(
                    "API-2", "API", "介面", "沒人負責的", "bug", "待辦", "not_started", null, "none",
                    null, new FoundPerson(TestUsers.Owner.PersonId, TestUsers.Owner.DisplayName)),
            ],
            HasMore: true);

    public Task<Result<ItemFindResult>> FindAsync(Actor actor, Guid? conversationId, ItemFindQuery query, CancellationToken cancellationToken)
    {
        _calls.Enqueue((actor, conversationId, query));
        return Task.FromResult(OnFind(query));
    }
}
