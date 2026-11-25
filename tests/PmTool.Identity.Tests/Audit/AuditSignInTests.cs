using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Identity.Audit;

namespace PmTool.Identity.Tests.Audit;

/// <summary>登入的稽核：只替找得到的帳號寫一列，找不到的只記 Warning，而且量大時不會塞爆資料表。</summary>
[TestFixture]
public sealed class AuditSignInTests
{
    private static readonly CancellationToken NoCancel = CancellationToken.None;

    private static Task<IdentityTestHost> StartHostAsync(ManualTimeProvider time, CollectingLoggerProvider logs) =>
        IdentityTestHost.StartAsync(services =>
        {
            services.AddSingleton<TimeProvider>(time);
            services.AddLogging(builder => builder.AddProvider(logs));
        });

    /// <summary>放進佇列，再處理完：稽核是背景寫的，測試要等它寫完才看得到。</summary>
    private static async Task RecordAsync(IdentityTestHost host, SignInAttempt attempt, CancellationToken cancellationToken)
    {
        await host.Audit.RecordSignInAsync(attempt, cancellationToken);
        await host.DrainSignInAuditAsync();
    }

    private static async Task<IReadOnlyList<AuditEntry>> EntriesAsync(IdentityTestHost host, AuthenticatedUser owner) =>
        (await host.Audit.ListAsync(owner.ToActor(), new AuditQuery(Limit: AuditRules.MaxLimit), NoCancel)).Value.Items;

    [Test]
    public async Task RecordSignInAsync_WrongPassword_IsRecordedAgainstThatAccount()
    {
        // Arrange
        await using var host = await StartHostAsync(new ManualTimeProvider(), new CollectingLoggerProvider());
        var owner = await host.StartWithOwnerAsync();
        var user = await host.AddUserAsync(owner.ToActor());

        // Act
        await RecordAsync(host, new SignInAttempt($" {user.Email.ToUpperInvariant()} ", false, "203.0.113.7", "example-agent/1.0"), NoCancel);

        // Assert
        var entry = (await EntriesAsync(host, owner)).Single(e => e.Action == AuditActions.SignIn);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.Outcome, Is.EqualTo(AuditOutcomes.Failed));
            Assert.That(entry.ActorPersonId, Is.EqualTo(user.PersonId));
            Assert.That(entry.SourceAddress, Is.EqualTo("203.0.113.7"));
        }
    }

    [Test]
    public async Task RecordSignInAsync_Success_IsRecordedAsSucceeded()
    {
        // Arrange
        await using var host = await StartHostAsync(new ManualTimeProvider(), new CollectingLoggerProvider());
        var owner = await host.StartWithOwnerAsync();

        // Act
        await RecordAsync(host, new SignInAttempt(owner.Email, true, "203.0.113.7", null), NoCancel);

        // Assert
        var entry = (await EntriesAsync(host, owner)).Single(e => e.Action == AuditActions.SignIn);
        Assert.That(entry.Outcome, Is.EqualTo(AuditOutcomes.Succeeded));
    }

    [Test]
    public async Task RecordSignInAsync_UnknownEmail_WritesNothingAndLogsAWarningWithoutTheEmail()
    {
        // Arrange
        var logs = new CollectingLoggerProvider();
        await using var host = await StartHostAsync(new ManualTimeProvider(), logs);
        var owner = await host.StartWithOwnerAsync();
        const string email = "nobody-here@example.com";

        // Act
        await RecordAsync(host, new SignInAttempt(email, false, "203.0.113.7", null), NoCancel);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((await EntriesAsync(host, owner)).Where(e => e.Action == AuditActions.SignIn), Is.Empty);
            Assert.That(logs.Warnings, Has.Count.EqualTo(1));
            Assert.That(logs.Warnings[0], Does.Not.Contain(email).And.Not.Contain("nobody-here"));
        }
    }

    [Test]
    public async Task RecordSignInAsync_UnusableEmail_DoesNotThrow()
    {
        // Arrange
        await using var host = await StartHostAsync(new ManualTimeProvider(), new CollectingLoggerProvider());

        // Act & Assert
        Assert.That(
            async () => await RecordAsync(host, new SignInAttempt("a\uD800\0b", false, null, null), NoCancel), Throws.Nothing);
    }

    [Test]
    public async Task RecordSignInAsync_DoesNotTouchTheDatabaseOrThrow_EvenWhenItIsUnreachable()
    {
        // Arrange：連不上資料庫的主機，一碰資料庫就會丟例外
        await using var host = IdentityTestHost.StartWithoutDatabase(null);

        // Act & Assert
        Assert.That(async () => await host.Audit.RecordSignInAsync(new SignInAttempt("a@example.com", false, null, null), NoCancel), Throws.Nothing);
    }

    [Test]
    public async Task RecordSignInAsync_NothingIsWrittenUntilTheConsumerRuns()
    {
        // Arrange
        await using var host = await StartHostAsync(new ManualTimeProvider(), new CollectingLoggerProvider());
        var owner = await host.StartWithOwnerAsync();

        // Act
        await host.Audit.RecordSignInAsync(new SignInAttempt(owner.Email, false, null, null), NoCancel);

        // Assert
        Assert.That((await EntriesAsync(host, owner)).Where(e => e.Action == AuditActions.SignIn), Is.Empty);
    }

    [Test]
    public async Task RecordSignInAsync_QueueIsFull_DropsTheNewestAndWarnsAtMostOncePerMinute()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var logs = new CollectingLoggerProvider();
        await using var host = await StartHostAsync(time, logs);
        var attempt = new SignInAttempt("a@example.com", false, null, null);
        for (var i = 0; i < SignInAuditQueue.Capacity; i++)
        {
            await host.Audit.RecordSignInAsync(attempt, NoCancel);
        }

        // Act
        for (var i = 0; i < 50; i++)
        {
            await host.Audit.RecordSignInAsync(attempt, NoCancel);
        }

        var inFirstMinute = logs.Warnings.Count;
        time.Advance(TimeSpan.FromMinutes(1));
        await host.Audit.RecordSignInAsync(attempt, NoCancel);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(inFirstMinute, Is.EqualTo(1));
            Assert.That(logs.Warnings, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task Worker_WritesWhatIsQueued()
    {
        // Arrange
        await using var host = await StartHostAsync(new ManualTimeProvider(), new CollectingLoggerProvider());
        var owner = await host.StartWithOwnerAsync();
        var worker = host.Services.GetServices<IHostedService>().OfType<SignInAuditWorker>().Single();
        await worker.StartAsync(NoCancel);

        // Act
        await host.Audit.RecordSignInAsync(new SignInAttempt(owner.Email, true, "203.0.113.7", null), NoCancel);
        var written = await WaitForAsync(async () => (await EntriesAsync(host, owner)).Any(e => e.Action == AuditActions.SignIn));
        await worker.StopAsync(NoCancel);

        // Assert
        Assert.That(written, Is.True);
    }

    [Test]
    public async Task Worker_WhenStopped_WritesWhatIsStillInTheQueue()
    {
        // Arrange：消費者還沒開始跑的時候先排了幾筆，一啟動就接著停
        await using var host = await StartHostAsync(new ManualTimeProvider(), new CollectingLoggerProvider());
        var owner = await host.StartWithOwnerAsync();
        var worker = host.Services.GetServices<IHostedService>().OfType<SignInAuditWorker>().Single();
        for (var i = 0; i < 3; i++)
        {
            await host.Audit.RecordSignInAsync(new SignInAttempt(owner.Email, true, null, null), NoCancel);
        }

        // Act
        await worker.StartAsync(NoCancel);
        await worker.StopAsync(NoCancel);

        // Assert
        Assert.That((await EntriesAsync(host, owner)).Count(e => e.Action == AuditActions.SignIn), Is.EqualTo(3));
    }

    [Test]
    public async Task Worker_StoppedBeforeItsLoopEverRan_StillWritesWhatIsInTheQueue()
    {
        // Arrange：主機一啟動就關閉時，背景的迴圈可能還沒排上就被取消；用已經取消的權杖啟動，必定是這種情形
        await using var host = await StartHostAsync(new ManualTimeProvider(), new CollectingLoggerProvider());
        var owner = await host.StartWithOwnerAsync();
        var worker = host.Services.GetServices<IHostedService>().OfType<SignInAuditWorker>().Single();
        for (var i = 0; i < 2; i++)
        {
            await host.Audit.RecordSignInAsync(new SignInAttempt(owner.Email, true, null, null), NoCancel);
        }

        // Act
        await worker.StartAsync(new CancellationToken(canceled: true));
        await worker.StopAsync(NoCancel);

        // Assert
        Assert.That((await EntriesAsync(host, owner)).Count(e => e.Action == AuditActions.SignIn), Is.EqualTo(2));
    }

    private static async Task<bool> WaitForAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return false;
    }

    [Test]
    public async Task RecordSignInAsync_MoreFailuresForOneAccountThanTheLimitInAMinute_WritesTheLimit()
    {
        // Arrange
        await using var host = await StartHostAsync(new ManualTimeProvider(), new CollectingLoggerProvider());
        var owner = await host.StartWithOwnerAsync();
        var attempt = new SignInAttempt(owner.Email, false, null, null);

        // Act
        for (var i = 0; i < AuditRules.MaxSignInFailuresPerAccountPerMinute + 5; i++)
        {
            await RecordAsync(host, attempt, NoCancel);
        }

        // Assert
        Assert.That((await CountAsync(host, owner)).Count(e => e.Action == AuditActions.SignIn), Is.EqualTo(AuditRules.MaxSignInFailuresPerAccountPerMinute));
    }

    [Test]
    public async Task RecordSignInAsync_TheNextMinute_WritesFailuresAgain()
    {
        // Arrange
        var time = new ManualTimeProvider();
        await using var host = await StartHostAsync(time, new CollectingLoggerProvider());
        var owner = await host.StartWithOwnerAsync();
        var attempt = new SignInAttempt(owner.Email, false, null, null);
        for (var i = 0; i < AuditRules.MaxSignInFailuresPerAccountPerMinute + 1; i++)
        {
            await RecordAsync(host, attempt, NoCancel);
        }

        time.Advance(TimeSpan.FromMinutes(1));

        // Act
        await RecordAsync(host, attempt, NoCancel);

        // Assert
        Assert.That((await CountAsync(host, owner)).Count(e => e.Action == AuditActions.SignIn), Is.EqualTo(AuditRules.MaxSignInFailuresPerAccountPerMinute + 1));
    }

    [Test]
    public async Task RecordSignInAsync_SuccessesAreNotLimited()
    {
        // Arrange
        await using var host = await StartHostAsync(new ManualTimeProvider(), new CollectingLoggerProvider());
        var owner = await host.StartWithOwnerAsync();
        var failure = new SignInAttempt(owner.Email, false, null, null);
        for (var i = 0; i < AuditRules.MaxSignInFailuresPerAccountPerMinute + 1; i++)
        {
            await RecordAsync(host, failure, NoCancel);
        }

        // Act
        await RecordAsync(host, new SignInAttempt(owner.Email, true, null, null), NoCancel);

        // Assert
        Assert.That((await CountAsync(host, owner)).Count(e => e.Outcome == AuditOutcomes.Succeeded && e.Action == AuditActions.SignIn), Is.EqualTo(1));
    }

    /// <summary>一頁最多 100 筆，所以逐頁讀完。</summary>
    private static async Task<IReadOnlyList<AuditEntry>> CountAsync(IdentityTestHost host, AuthenticatedUser owner)
    {
        var all = new List<AuditEntry>();
        string? cursor = null;
        do
        {
            var page = (await host.Audit.ListAsync(owner.ToActor(), new AuditQuery(cursor, AuditRules.MaxLimit), NoCancel)).Value;
            all.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return all;
    }
}

/// <summary>登入失敗的額度：總量與每個帳號各自的上限，以及每個帳號每分鐘的第一筆一定寫得進去。</summary>
[TestFixture]
public sealed class SignInFailureBudgetTests
{
    private static (SignInFailureBudget Budget, ManualTimeProvider Time, CollectingLoggerProvider Logs) NewBudget(
        int perMinute = 5, int perAccount = 3, int maxAccounts = 100)
    {
        var time = new ManualTimeProvider();
        var logs = new CollectingLoggerProvider();
        var budget = new SignInFailureBudget(time, logs.CreateLogger("test"), perMinute, perAccount, maxAccounts);
        return (budget, time, logs);
    }

    [Test]
    public void TryTake_OneAccountOverItsLimit_IsRefused()
    {
        // Arrange
        var (budget, _, _) = NewBudget(perMinute: 100, perAccount: 3);
        var account = Guid.NewGuid();

        // Act
        var results = Enumerable.Range(0, 5).Select(_ => budget.TryTake(account)).ToList();

        // Assert
        Assert.That(results, Is.EqualTo(new[] { true, true, true, false, false }));
    }

    [Test]
    public void TryTake_TheProcessLimitIsUsedUp_LaterFailuresOfKnownAccountsAreRefusedAndWarnedOnce()
    {
        // Arrange
        var (budget, _, logs) = NewBudget(perMinute: 4, perAccount: 10);
        var accounts = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToList();
        foreach (var account in accounts)
        {
            budget.TryTake(account);
        }

        // Act
        var second = accounts.Select(budget.TryTake).ToList();
        var again = budget.TryTake(accounts[0]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(second, Is.All.False);
            Assert.That(again, Is.False);
            Assert.That(logs.Warnings, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void TryTake_TheFirstFailureOfAnAccountInAMinute_IsAlwaysWritten_EvenWhenTheProcessLimitIsGone()
    {
        // Arrange
        var (budget, _, _) = NewBudget(perMinute: 3, perAccount: 10);
        for (var i = 0; i < 20; i++)
        {
            budget.TryTake(Guid.NewGuid());
        }

        // Act
        var firstOfNewAccount = budget.TryTake(Guid.NewGuid());

        // Assert
        Assert.That(firstOfNewAccount, Is.True);
    }

    [Test]
    public void TryTake_TheNextMinute_StartsOverForEveryAccount()
    {
        // Arrange
        var (budget, time, _) = NewBudget(perMinute: 100, perAccount: 1);
        var account = Guid.NewGuid();
        budget.TryTake(account);

        // Act
        var sameMinute = budget.TryTake(account);
        time.Advance(TimeSpan.FromMinutes(1));
        var nextMinute = budget.TryTake(account);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sameMinute, Is.False);
            Assert.That(nextMinute, Is.True);
        }
    }

    [Test]
    public void TryTake_MoreAccountsThanItTracks_FallsBackToTheProcessLimitInsteadOfGrowing()
    {
        // Arrange
        var (budget, _, _) = NewBudget(perMinute: 5, perAccount: 10, maxAccounts: 2);
        budget.TryTake(Guid.NewGuid());
        budget.TryTake(Guid.NewGuid());

        // Act
        var results = Enumerable.Range(0, 5).Select(_ => budget.TryTake(Guid.NewGuid())).ToList();

        // Assert：總量 5，前面已經用掉 2
        Assert.That(results, Is.EqualTo(new[] { true, true, true, false, false }));
    }
}

/// <summary>收集 Warning 以上的訊息，測試看得出模組記了什麼。</summary>
internal sealed class CollectingLoggerProvider : ILoggerProvider
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _warnings = new();

    public IReadOnlyList<string> Warnings => [.. _warnings];

    public ILogger CreateLogger(string categoryName) => new Collector(_warnings);

    public void Dispose()
    {
    }

    private sealed class Collector(System.Collections.Concurrent.ConcurrentQueue<string> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                sink.Enqueue(formatter(state, exception));
            }
        }
    }
}
