using PmTool.Abstractions;
using PmTool.Ai;

namespace PmTool.Api.Tests.Support;

/// <summary>記下對話讀過的專案（組織對話裡用專案工具時）：記了哪些人、哪則對話、哪些專案。</summary>
internal sealed class FakeConversationReads : IAssistantConversationReads
{
    private readonly List<(Actor Actor, Guid? ConversationId, IReadOnlyList<string> ProjectKeys)> _calls = [];

    public IReadOnlyList<(Actor Actor, Guid? ConversationId, IReadOnlyList<string> ProjectKeys)> Calls => _calls;

    public Task RememberAsync(Actor actor, Guid? conversationId, IReadOnlyCollection<string> projectKeys, CancellationToken cancellationToken)
    {
        _calls.Add((actor, conversationId, [.. projectKeys]));
        return Task.CompletedTask;
    }
}
