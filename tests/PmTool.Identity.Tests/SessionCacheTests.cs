using PmTool.Abstractions;
using PmTool.Identity.Sessions;

namespace PmTool.Identity.Tests;

/// <summary>
/// 工作階段查詢結果的快取。它只在記憶體裡，查資料庫的部分換成測試給的函式，才數得出查了幾次、
/// 也才能讓查詢停在半路，製造「查到一半就被作廢」的時序。這些從公開介面看不出來。
/// </summary>
[TestFixture]
public sealed class SessionCacheTests
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(IdentityRules.SessionCacheSeconds);
    private static readonly TimeSpan Moment = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);
    private static readonly Guid AccountA = Guid.Parse("0190b000-0000-7000-8000-0000000000a1");
    private static readonly Guid AccountB = Guid.Parse("0190b000-0000-7000-8000-0000000000a2");

    private ManualTimeProvider _time = null!;

    [SetUp]
    public void CreateClock() => _time = new ManualTimeProvider();

    [Test]
    public async Task Get_SameKeyWithinTheDuration_LooksUpOnce()
    {
        // Arrange
        var cache = CreateCache();
        var lookups = new Lookups(Valid(AccountA));

        // Act
        await cache.GetAsync("k", lookups.Run, CancellationToken.None);
        _time.Advance(Duration - Moment);
        await cache.GetAsync("k", lookups.Run, CancellationToken.None);
        var withinTheDuration = lookups.Count;
        _time.Advance(Moment);
        await cache.GetAsync("k", lookups.Run, CancellationToken.None);

        // Assert
        Assert.That(withinTheDuration, Is.EqualTo(1));
        Assert.That(lookups.Count, Is.EqualTo(2), "過了快取的時間要再查一次");
    }

    [Test]
    public async Task Get_InvalidResult_IsAlsoKept()
    {
        // Arrange：拿著作廢的舊 Cookie 一直重送，也只查一次。
        var cache = CreateCache();
        var lookups = new Lookups(new SessionLookup(AccountA, null));

        // Act
        for (var i = 0; i < 5; i++)
        {
            await cache.GetAsync("k", lookups.Run, CancellationToken.None);
        }

        // Assert
        Assert.That(lookups.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task Get_ConcurrentRequestsForTheSameKey_ShareOneLookup()
    {
        // Arrange
        var cache = CreateCache();
        var lookups = new Lookups(Valid(AccountA)) { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };

        // Act
        var waiting = Enumerable.Range(0, 10).Select(_ => cache.GetAsync("k", lookups.Run, CancellationToken.None)).ToArray();
        var whileWaiting = lookups.Count;
        lookups.Gate.SetResult();
        var results = await Task.WhenAll(waiting).WaitAsync(Patience);

        // Assert
        Assert.That(whileWaiting, Is.EqualTo(1));
        Assert.That(results.Select(r => r.Session), Is.All.Not.Null);
        Assert.That(lookups.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task Forget_TheAccount_MakesTheNextGetLookUpAgain()
    {
        // Arrange
        var cache = CreateCache();
        var lookups = new Lookups(Valid(AccountA));
        await cache.GetAsync("k", lookups.Run, CancellationToken.None);

        // Act
        cache.Forget(AccountA);
        await cache.GetAsync("k", lookups.Run, CancellationToken.None);

        // Assert
        Assert.That(lookups.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task Forget_AnotherAccount_KeepsTheResult()
    {
        // Arrange
        var cache = CreateCache();
        var lookups = new Lookups(Valid(AccountA));
        await cache.GetAsync("k", lookups.Run, CancellationToken.None);

        // Act
        cache.Forget(AccountB);
        await cache.GetAsync("k", lookups.Run, CancellationToken.None);

        // Assert
        Assert.That(lookups.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task Forget_WhileALookupIsRunning_TheResultOfThatLookupIsNotReused()
    {
        // Arrange：查詢在作廢寫進資料庫之前就讀到「還有效」，作廢之後才回來。它的結果不能留在快取裡。
        var cache = CreateCache();
        var lookups = new Lookups(Valid(AccountA)) { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var first = cache.GetAsync("k", lookups.Run, CancellationToken.None);

        // Act
        cache.Forget(AccountA);
        lookups.Gate.SetResult();
        await first.WaitAsync(Patience);
        await cache.GetAsync("k", lookups.Run, CancellationToken.None);

        // Assert
        Assert.That(lookups.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task Get_AfterAForgetWhileALookupIsRunning_DoesNotJoinThatLookup()
    {
        // Arrange：作廢之後才來的請求不能拿到作廢之前開始的查詢的結果。
        var cache = CreateCache();
        var stale = new Lookups(Valid(AccountA)) { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var fresh = new Lookups(new SessionLookup(AccountA, null));
        var first = cache.GetAsync("k", stale.Run, CancellationToken.None);
        cache.Forget(AccountA);

        // Act
        var afterwards = await cache.GetAsync("k", fresh.Run, CancellationToken.None).WaitAsync(Patience);
        stale.Gate.SetResult();
        await first.WaitAsync(Patience);
        var later = await cache.GetAsync("k", stale.Run, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(afterwards.Session, Is.Null);
            Assert.That(fresh.Count, Is.EqualTo(1));
            Assert.That(later.Session, Is.Null, "晚回來的舊查詢不能蓋掉新的結果");
            Assert.That(stale.Count, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Get_SessionReachesItsAbsoluteLimitWithinTheDuration_LooksUpAgain()
    {
        // Arrange
        var cache = CreateCache();
        var lookups = new Lookups(Valid(AccountA, expiresAt: _time.GetUtcNow() + Moment));
        await cache.GetAsync("k", lookups.Run, CancellationToken.None);

        // Act
        _time.Advance(Moment);
        await cache.GetAsync("k", lookups.Run, CancellationToken.None);

        // Assert
        Assert.That(lookups.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task Get_ClockMovedBackwards_DoesNotTrustTheResult()
    {
        // Arrange
        var cache = CreateCache();
        var lookups = new Lookups(Valid(AccountA));
        await cache.GetAsync("k", lookups.Run, CancellationToken.None);

        // Act
        _time.Advance(-Moment);
        await cache.GetAsync("k", lookups.Run, CancellationToken.None);

        // Assert
        Assert.That(lookups.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task Get_LookupFails_TheErrorIsNotKept()
    {
        // Arrange
        var cache = CreateCache();
        var failing = new Lookups(Valid(AccountA)) { Failure = new InvalidOperationException("資料庫連不上") };
        var working = new Lookups(Valid(AccountA));

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetAsync("k", failing.Run, CancellationToken.None));
        var result = await cache.GetAsync("k", working.Run, CancellationToken.None);
        Assert.That(result.Session, Is.Not.Null);
        Assert.That(working.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task Get_OneWaiterGivesUp_TheOthersStillGetTheResult()
    {
        // Arrange
        var cache = CreateCache();
        var lookups = new Lookups(Valid(AccountA)) { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var impatient = new CancellationTokenSource();
        var cancelled = cache.GetAsync("k", lookups.Run, impatient.Token);
        var patient = cache.GetAsync("k", lookups.Run, CancellationToken.None);

        // Act
        await impatient.CancelAsync();
        lookups.Gate.SetResult();

        // Assert
        Assert.CatchAsync<OperationCanceledException>(() => cancelled);
        Assert.That((await patient.WaitAsync(Patience)).Session, Is.Not.Null);
    }

    [Test]
    public async Task Get_Full_DropsTheOldestEntryFirst()
    {
        // Arrange：上限兩筆。三個權杖依序查過，第三個進來時最早的那一個被擠掉。
        var cache = CreateCache(maxEntries: 2);
        var lookups = new Lookups(Valid(AccountA));
        foreach (var key in new[] { "k1", "k2", "k3" })
        {
            await cache.GetAsync(key, lookups.Run, CancellationToken.None);
            _time.Advance(Moment);
        }

        // Act
        await cache.GetAsync("k3", lookups.Run, CancellationToken.None);
        var beforeTheOldest = lookups.Count;
        await cache.GetAsync("k1", lookups.Run, CancellationToken.None);

        // Assert
        Assert.That(beforeTheOldest, Is.EqualTo(3), "k3 還在快取裡");
        Assert.That(lookups.Count, Is.EqualTo(4), "k1 被擠掉了，要再查一次");
    }

    [Test]
    public async Task Get_UnknownResultWhenNotKept_IsLookedUpAgainEachTime()
    {
        // Arrange
        var cache = CreateCache();
        var lookups = new Lookups(SessionLookup.Unknown);

        // Act
        for (var i = 0; i < 3; i++)
        {
            await cache.GetAsync("pat:k", lookups.Run, CancellationToken.None, keepUnknown: false);
        }

        // Assert
        Assert.That(lookups.Count, Is.EqualTo(3));
    }

    [Test]
    public async Task Get_UnknownResultWhenNotKept_StillSharesTheLookupInFlight()
    {
        // Arrange
        var cache = CreateCache();
        var lookups = new Lookups(SessionLookup.Unknown) { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };

        // Act
        var waiting = Enumerable.Range(0, 5).Select(_ => cache.GetAsync("pat:k", lookups.Run, CancellationToken.None, keepUnknown: false)).ToArray();
        lookups.Gate.SetResult();
        await Task.WhenAll(waiting).WaitAsync(Patience);

        // Assert
        Assert.That(lookups.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task Get_KnownButInvalidResultWhenNotKept_IsStillKept()
    {
        // Arrange：只有查無此權杖的不留；撤銷過的權杖有真的對應的資料列，不是亂數能造出來的。
        var cache = CreateCache();
        var lookups = new Lookups(new SessionLookup(AccountA, null));

        // Act
        for (var i = 0; i < 3; i++)
        {
            await cache.GetAsync("pat:k", lookups.Run, CancellationToken.None, keepUnknown: false);
        }

        // Assert
        Assert.That(lookups.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task Get_ManyUnknownTokens_DoNotEvictAValidSession()
    {
        // Arrange：上限五筆，先存一個有效的工作階段，再用一堆不認得的權杖灌。
        var cache = CreateCache(maxEntries: 5);
        var session = new Lookups(Valid(AccountA));
        var unknown = new Lookups(SessionLookup.Unknown);
        await cache.GetAsync("session", session.Run, CancellationToken.None);
        _time.Advance(Moment);

        // Act
        for (var i = 0; i < 50; i++)
        {
            await cache.GetAsync($"pat:{i}", unknown.Run, CancellationToken.None, keepUnknown: false);
        }

        await cache.GetAsync("session", session.Run, CancellationToken.None);

        // Assert
        Assert.That(session.Count, Is.EqualTo(1), "工作階段還在快取裡");
    }

    [Test]
    public void TryPeek_NothingCached_ReturnsNull()
    {
        // Arrange
        var cache = CreateCache();

        // Act & Assert
        Assert.That(cache.TryPeek("k"), Is.Null);
    }

    [Test]
    public async Task TryPeek_UsableResult_ReturnsItWithoutLookingAgain()
    {
        // Arrange
        var cache = CreateCache();
        var lookups = new Lookups(Valid(AccountA));
        await cache.GetAsync("k", lookups.Run, CancellationToken.None);

        // Act
        var peeked = cache.TryPeek("k");

        // Assert
        Assert.That(peeked?.AccountId, Is.EqualTo(AccountA));
        Assert.That(lookups.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task TryPeek_AfterTheDuration_ReturnsNull()
    {
        // Arrange
        var cache = CreateCache();
        await cache.GetAsync("k", new Lookups(Valid(AccountA)).Run, CancellationToken.None);
        _time.Advance(Duration);

        // Act & Assert
        Assert.That(cache.TryPeek("k"), Is.Null);
    }

    [Test]
    public async Task TryPeek_AfterTheAccountWasForgotten_ReturnsNull()
    {
        // Arrange
        var cache = CreateCache();
        await cache.GetAsync("k", new Lookups(Valid(AccountA)).Run, CancellationToken.None);
        cache.Forget(AccountA);

        // Act & Assert
        Assert.That(cache.TryPeek("k"), Is.Null);
    }

    [Test]
    public async Task TryPeek_LookupStillRunning_ReturnsNullAndDoesNotJoinIt()
    {
        // Arrange：還沒有結果的查詢不算「快取裡有」
        var cache = CreateCache();
        var gate = new TaskCompletionSource();
        var lookups = new Lookups(Valid(AccountA)) { Gate = gate };
        var pending = cache.GetAsync("k", lookups.Run, CancellationToken.None);

        // Act
        var peeked = cache.TryPeek("k");
        gate.SetResult();
        await pending.WaitAsync(Patience);

        // Assert
        Assert.That(peeked, Is.Null);
    }

    private SessionCache CreateCache(int maxEntries = 100, IClusterSignals? signals = null) =>
        new(_time, new SessionCacheLimits { Duration = Duration, MaxEntries = maxEntries }, signals);

    private sealed class RecordingSignals : IClusterSignals
    {
        public List<ClusterSignal> Published { get; } = [];

        public void Publish(ClusterSignal signal) => Published.Add(signal);
    }

    [Test]
    public void Forget_PublishesCredentialsRevokedForTheAccount_AndForgetLocalDoesNot()
    {
        // Arrange
        var signals = new RecordingSignals();
        var cache = CreateCache(signals: signals);

        // Act
        cache.ForgetLocal(AccountB);
        cache.Forget(AccountA);

        // Assert
        Assert.That(signals.Published.Select(s => (s.Kind, s.AccountId)), Is.EqualTo(new[] { (ClusterSignalKind.CredentialsRevoked, (Guid?)AccountA) }));
    }

    [Test]
    public async Task ForgetLocal_DropsTheAccountsResult_LikeForget()
    {
        // Arrange
        var cache = CreateCache();
        var lookups = new Lookups(Valid(AccountA));
        await cache.GetAsync("k", lookups.Run, CancellationToken.None);

        // Act
        cache.ForgetLocal(AccountA);
        await cache.GetAsync("k", lookups.Run, CancellationToken.None);

        // Assert
        Assert.That(lookups.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task Clear_DropsEveryAccountsResult()
    {
        // Arrange
        var cache = CreateCache();
        var a = new Lookups(Valid(AccountA));
        var b = new Lookups(Valid(AccountB));
        await cache.GetAsync("a", a.Run, CancellationToken.None);
        await cache.GetAsync("b", b.Run, CancellationToken.None);

        // Act
        cache.Clear();
        await cache.GetAsync("a", a.Run, CancellationToken.None);
        await cache.GetAsync("b", b.Run, CancellationToken.None);

        // Assert
        Assert.That((a.Count, b.Count), Is.EqualTo((2, 2)));
    }

    [Test]
    public async Task Clear_ALookupThatStartedBeforeIt_IsNotKeptAfterwards()
    {
        // Arrange
        var cache = CreateCache();
        var gate = new TaskCompletionSource();
        var lookups = new Lookups(Valid(AccountA)) { Gate = gate };
        var pending = cache.GetAsync("k", lookups.Run, CancellationToken.None);

        // Act
        cache.Clear();
        gate.SetResult();
        await pending.WaitAsync(Patience);
        await cache.GetAsync("k", lookups.Run, CancellationToken.None);

        // Assert
        Assert.That(lookups.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task SessionCacheSignals_CredentialsRevokedFromElsewhere_ClearsLocallyWithoutPublishingAgain()
    {
        // Arrange
        var signals = new RecordingSignals();
        var cache = CreateCache(signals: signals);
        var lookups = new Lookups(Valid(AccountA));
        await cache.GetAsync("k", lookups.Run, CancellationToken.None);
        var handler = new SessionCacheSignals(cache);

        // Act
        await handler.HandleAsync(new ClusterSignal(ClusterSignalKind.CredentialsRevoked) { AccountId = AccountA }, CancellationToken.None);
        await cache.GetAsync("k", lookups.Run, CancellationToken.None);

        // Assert
        Assert.That(lookups.Count, Is.EqualTo(2));
        Assert.That(signals.Published, Is.Empty);
    }

    [Test]
    public async Task SessionCacheSignals_Resynchronize_ClearsTheWholeCache()
    {
        // Arrange
        var cache = CreateCache();
        var lookups = new Lookups(Valid(AccountA));
        await cache.GetAsync("k", lookups.Run, CancellationToken.None);

        // Act
        await new SessionCacheSignals(cache).ResynchronizeAsync(CancellationToken.None);
        await cache.GetAsync("k", lookups.Run, CancellationToken.None);

        // Assert
        Assert.That(lookups.Count, Is.EqualTo(2));
    }

    private SessionLookup Valid(Guid accountId, DateTimeOffset? expiresAt = null)
    {
        var user = new AuthenticatedUser(
            accountId, Guid.CreateVersion7(), Guid.CreateVersion7(), "範例組織", "範例使用者", "user@example.com", OrganizationRole.Member);
        return new SessionLookup(accountId, new ActiveSession(Guid.CreateVersion7(), user, expiresAt ?? _time.GetUtcNow().AddDays(7)));
    }

    /// <summary>代替資料庫的查詢：記次數，可以停在半路（<see cref="Gate"/>），也可以失敗。</summary>
    private sealed class Lookups(SessionLookup result)
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public TaskCompletionSource? Gate { get; init; }

        public Exception? Failure { get; init; }

        public async Task<SessionLookup> Run()
        {
            Interlocked.Increment(ref _count);
            if (Gate is not null)
            {
                await Gate.Task;
            }

            return Failure is null ? result : throw Failure;
        }
    }
}
