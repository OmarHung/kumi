using System.Collections.Concurrent;
using PmTool.Abstractions;
using PmTool.Identity;

namespace PmTool.Api.Tests.Support;

/// <summary>測試用的稽核紀錄：回測試給的那一頁，記下被問了什麼。權限與分頁的規則在身分模組自己的測試裡。</summary>
internal sealed class FakeAuditLog : IAuditLog
{
    private readonly ConcurrentQueue<(Actor Actor, AuditQuery Query)> _lists = new();
    private readonly ConcurrentQueue<SignInAttempt> _signIns = new();

    /// <summary>每次 <see cref="ListAsync"/> 怎麼回答。預設是空的一頁。</summary>
    public Func<Actor, AuditQuery, Result<AuditPage>> OnList { get; set; } = (_, _) => new AuditPage([], null);

    /// <summary>記下登入結果之後再做的事，例如丟例外，模擬寫稽核失敗。</summary>
    public Action<SignInAttempt>? OnSignIn { get; set; }

    public IReadOnlyList<(Actor Actor, AuditQuery Query)> ListCalls => [.. _lists];

    /// <summary>端點在回應送完之後記下的登入結果。</summary>
    public IReadOnlyList<SignInAttempt> SignIns => [.. _signIns];

    public Task<Result<AuditPage>> ListAsync(Actor actor, AuditQuery query, CancellationToken cancellationToken)
    {
        _lists.Enqueue((actor, query));
        return Task.FromResult(OnList(actor, query));
    }

    public Task RecordSignInAsync(SignInAttempt attempt, CancellationToken cancellationToken)
    {
        _signIns.Enqueue(attempt);
        OnSignIn?.Invoke(attempt);
        return Task.CompletedTask;
    }
}
