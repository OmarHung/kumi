using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PmTool.Api.RateLimiting;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

/// <summary>登入失敗的節流：延遲的算法（直接測節流）與端點的行為（429、Retry-After、帳號存不存在一樣）。</summary>
[TestFixture]
public sealed class SignInThrottleTests
{
    private static readonly SignInThrottleLimits Limits = new();

    private static (SignInThrottle Throttle, ManualTimeProvider Time) NewThrottle(SignInThrottleLimits? limits = null)
    {
        var time = new ManualTimeProvider();
        return (new SignInThrottle(limits ?? Limits, time), time);
    }

    private const string Source = "203.0.113.1";

    private static void Fail(SignInThrottle throttle, string email, int times, string source = Source)
    {
        for (var i = 0; i < times; i++)
        {
            var admission = throttle.Begin(email, source);
            Assert.That(admission.IsAllowed, Is.True, $"第 {i + 1} 次失敗之前就被擋了");
            throttle.Failed(admission);
        }
    }

    private static void AssertAdmitted(SignInAdmission admission, int delaySeconds, string? message = null)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(admission.IsAllowed, Is.True, message);
            Assert.That(admission.Delay, Is.EqualTo(TimeSpan.FromSeconds(delaySeconds)), message);
            Assert.That(admission.RetryAfter, Is.EqualTo(TimeSpan.Zero), message);
        }
    }

    [Test]
    public void Begin_FirstFourFailures_AreNotDelayed()
    {
        // Arrange
        var (throttle, _) = NewThrottle();

        // Act & Assert
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var admission = throttle.Begin("a@example.com", Source);
            AssertAdmitted(admission, 0, $"第 {attempt} 次");
            throttle.Failed(admission);
        }
    }

    [TestCase(4, 1)]
    [TestCase(5, 2)]
    [TestCase(10, 7)]
    [TestCase(33, 30)]
    [TestCase(34, 30)]
    [TestCase(500, 30)]
    public void Begin_AfterEnoughFailures_DelaysOneMoreSecondEachTimeUpToThirty(int failures, int expectedSeconds)
    {
        // Arrange
        var (throttle, _) = NewThrottle();
        Fail(throttle, "a@example.com", failures);

        // Act
        var admission = throttle.Begin("a@example.com", Source);

        // Assert
        AssertAdmitted(admission, expectedSeconds);
    }

    [Test]
    public void Begin_WhileAnAttemptFromTheSameSourceIsWaiting_RejectsWithTheRemainingWait()
    {
        // Arrange
        var (throttle, time) = NewThrottle();
        Fail(throttle, "a@example.com", 6);
        var first = throttle.Begin("a@example.com", Source);
        time.Advance(TimeSpan.FromSeconds(1));

        // Act
        var second = throttle.Begin("A@example.com", Source);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            AssertAdmitted(first, 3);
            Assert.That(second.IsAllowed, Is.False);
            Assert.That(second.RetryAfter, Is.EqualTo(TimeSpan.FromSeconds(2)));
        }
    }

    [Test]
    public void Begin_WhileAnotherSourceIsWaitingForTheSameAccount_StillAllowsThisSourceAndWaitsLonger()
    {
        // Arrange
        var (throttle, _) = NewThrottle();
        Fail(throttle, "a@example.com", 4);
        var other = throttle.Begin("a@example.com", "203.0.113.2");

        // Act
        var mine = throttle.Begin("a@example.com", Source);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            AssertAdmitted(other, 1);
            AssertAdmitted(mine, 2, "別的來源正在試，算進延遲，但不擋我");
        }
    }

    [Test]
    public void Begin_TenFirstAttemptsAtOnceForOneAccount_AreNotAllWaved()
    {
        // Arrange
        var (throttle, _) = NewThrottle();

        // Act
        var delays = Enumerable.Range(1, 10).Select(i => throttle.Begin("a@example.com", $"198.51.100.{i}").Delay.TotalSeconds).ToList();

        // Assert
        Assert.That(delays, Is.EqualTo(new double[] { 0, 0, 0, 0, 1, 2, 3, 4, 5, 6 }));
    }

    [Test]
    public void Begin_MoreAttemptsInProgressForOneAccountThanTheLimit_RejectsTheRest()
    {
        // Arrange
        var (throttle, _) = NewThrottle(new SignInThrottleLimits { MaxPendingPerAccount = 3 });
        var admitted = Enumerable.Range(1, 3).Select(i => throttle.Begin("a@example.com", $"198.51.100.{i}")).ToList();

        // Act
        var over = throttle.Begin("a@example.com", "198.51.100.9");
        var otherAccount = throttle.Begin("b@example.com", "198.51.100.9");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(admitted.All(a => a.IsAllowed), Is.True);
            Assert.That(over.IsAllowed, Is.False);
            Assert.That(otherAccount.IsAllowed, Is.True);
        }
    }

    [Test]
    public void Begin_AfterTheWaitingAttemptFinishes_AllowsTheNextOneWithALongerDelay()
    {
        // Arrange
        var (throttle, _) = NewThrottle();
        Fail(throttle, "a@example.com", 4);
        throttle.Failed(throttle.Begin("a@example.com", Source));

        // Act
        var next = throttle.Begin("a@example.com", Source);

        // Assert
        AssertAdmitted(next, 2);
    }

    [Test]
    public void Begin_AfterTheWaitingAttemptIsAbandoned_FreesTheSlotWithoutCountingAFailure()
    {
        // Arrange
        var (throttle, _) = NewThrottle();
        Fail(throttle, "a@example.com", 4);
        throttle.Abandoned(throttle.Begin("a@example.com", Source));

        // Act
        var next = throttle.Begin("a@example.com", Source);

        // Assert
        AssertAdmitted(next, 1);
    }

    [Test]
    public void Abandoned_WithAnotherAttemptsAdmission_DoesNotFreeSomeoneElsesSlot()
    {
        // Arrange
        var (throttle, _) = NewThrottle();
        var owner = throttle.Begin("a@example.com", Source);
        var rejected = throttle.Begin("a@example.com", Source);
        var elsewhere = throttle.Begin("a@example.com", "203.0.113.2");

        // Act
        throttle.Abandoned(rejected);
        throttle.Abandoned(default);
        throttle.Abandoned(elsewhere);
        var again = throttle.Begin("a@example.com", Source);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(owner.IsAllowed, Is.True);
            Assert.That(rejected.IsAllowed, Is.False);
            Assert.That(again.IsAllowed, Is.False, "放掉的只有自己佔的位子");
        }
    }

    [Test]
    public void Abandoned_CalledTwice_DoesNotFreeANewerSlotOfTheSameSource()
    {
        // Arrange
        var (throttle, _) = NewThrottle();
        var first = throttle.Begin("a@example.com", Source);
        throttle.Abandoned(first);
        var second = throttle.Begin("a@example.com", Source);

        // Act
        throttle.Abandoned(first);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(second.IsAllowed, Is.True);
            Assert.That(throttle.Begin("a@example.com", Source).IsAllowed, Is.False);
        }
    }

    [Test]
    public void Failed_CalledTwiceWithTheSameAdmission_CountsOnlyOneFailure()
    {
        // Arrange
        var (throttle, _) = NewThrottle();
        Fail(throttle, "a@example.com", 3);
        var admission = throttle.Begin("a@example.com", Source);

        // Act
        throttle.Failed(admission);
        throttle.Failed(admission);

        // Assert
        AssertAdmitted(throttle.Begin("a@example.com", Source), 1);
    }

    [Test]
    public void Begin_AfterASuccess_StartsOverFromZero()
    {
        // Arrange
        var (throttle, _) = NewThrottle();
        Fail(throttle, "a@example.com", 8);
        throttle.Succeeded(throttle.Begin("a@example.com", Source));

        // Act
        var next = throttle.Begin("a@example.com", Source);

        // Assert
        AssertAdmitted(next, 0);
    }

    [Test]
    public void Begin_FifteenMinutesWithoutAFailure_StartsOverFromZero()
    {
        // Arrange
        var (throttle, time) = NewThrottle();
        Fail(throttle, "a@example.com", 8);
        time.Advance(TimeSpan.FromMinutes(15));

        // Act
        var next = throttle.Begin("a@example.com", Source);

        // Assert
        AssertAdmitted(next, 0);
    }

    [Test]
    public void Begin_JustBeforeFifteenMinutes_StillRemembersTheFailures()
    {
        // Arrange
        var (throttle, time) = NewThrottle();
        Fail(throttle, "a@example.com", 8);
        time.Advance(TimeSpan.FromMinutes(15) - TimeSpan.FromSeconds(1));

        // Act
        var next = throttle.Begin("a@example.com", Source);

        // Assert
        Assert.That(next.Delay, Is.EqualTo(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public void Begin_CaseAndFullWidthVariantsOfAnEmail_ShareOneCounter()
    {
        // Arrange
        var (throttle, _) = NewThrottle();
        Fail(throttle, "Owner@Example.com", 2);
        Fail(throttle, "  ｏｗｎｅｒ@example.com ", 2);

        // Act
        var next = throttle.Begin("OWNER@EXAMPLE.COM", Source);

        // Assert
        Assert.That(next.Delay, Is.EqualTo(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void Begin_AnEmailWithALoneSurrogate_DoesNotThrow()
    {
        // Arrange
        var (throttle, _) = NewThrottle();

        // Act & Assert
        Assert.That(() => throttle.Begin("a\uD800@example.com", Source), Throws.Nothing);
    }

    [Test]
    public void Begin_AnotherEmail_IsNotAffected()
    {
        // Arrange
        var (throttle, _) = NewThrottle();
        Fail(throttle, "a@example.com", 10);

        // Act
        var other = throttle.Begin("b@example.com", Source);

        // Assert
        AssertAdmitted(other, 0);
    }

    [Test]
    public void Failed_PastTheTrackedKeyLimit_EvictsTheOneThatFailedLongestAgo()
    {
        // Arrange
        var (throttle, time) = NewThrottle(new SignInThrottleLimits { MaxTrackedKeys = 3 });
        foreach (var name in new[] { "a", "b", "c" })
        {
            Fail(throttle, $"{name}@example.com", 4);
            time.Advance(TimeSpan.FromSeconds(1));
        }

        // Act
        Fail(throttle, "d@example.com", 4);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(throttle.Begin("a@example.com", Source).Delay, Is.EqualTo(TimeSpan.Zero), "最久沒失敗的被擠掉");
            Assert.That(throttle.Begin("b@example.com", Source).Delay, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(throttle.Begin("d@example.com", Source).Delay, Is.EqualTo(TimeSpan.FromSeconds(1)));
        }
    }

    [Test]
    public void Failed_PastTheTrackedKeyLimit_DropsExpiredEntriesBeforeEvictingLiveOnes()
    {
        // Arrange
        var (throttle, time) = NewThrottle(new SignInThrottleLimits { MaxTrackedKeys = 2 });
        Fail(throttle, "old@example.com", 4);
        time.Advance(TimeSpan.FromMinutes(14));
        Fail(throttle, "live@example.com", 4);
        time.Advance(TimeSpan.FromMinutes(2));

        // Act
        Fail(throttle, "new@example.com", 4);

        // Assert
        Assert.That(throttle.Begin("live@example.com", Source).Delay, Is.EqualTo(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void Begin_WhenDisabled_NeverDelaysOrRejects()
    {
        // Arrange
        var (throttle, _) = NewThrottle(SignInThrottleLimits.Disabled);
        Fail(throttle, "a@example.com", 20);

        // Act
        var admission = throttle.Begin("a@example.com", Source);

        // Assert
        AssertAdmitted(admission, 0);
    }

    // ---- 端點 ----

    private static ApiFactory EnabledFactory(RecordingSignInDelay delay) =>
        new(FakeBlueprintValidator.Passing(), new ApiFactoryOptions
        {
            ConfigureServices = services =>
            {
                services.RemoveAll<SignInThrottleLimits>();
                services.AddSingleton(new SignInThrottleLimits());
                services.RemoveAll<ISignInDelay>();
                services.AddSingleton<ISignInDelay>(delay);
            },
        });

    private static Task<HttpResponseMessage> TrySignInAsync(HttpClient client, string email, string password) =>
        client.PostAsync(Sessions.Route, Sessions.Credentials(email, password));

    [Test]
    public async Task SignIn_FirstFourWrongPasswords_AreNotDelayed()
    {
        // Arrange
        var delay = new RecordingSignInDelay();
        using var factory = EnabledFactory(delay);
        using var client = factory.CreateApiClient();

        // Act
        for (var i = 0; i < 4; i++)
        {
            using var response = await TrySignInAsync(client, TestUsers.Owner.Email, "wrong-password-0000");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        // Assert
        Assert.That(delay.Requested, Is.Empty);
    }

    [Test]
    public async Task SignIn_FifthAndSixthWrongPasswords_WaitOneThenTwoSeconds()
    {
        // Arrange
        var delay = new RecordingSignInDelay();
        using var factory = EnabledFactory(delay);
        using var client = factory.CreateApiClient();

        // Act
        for (var i = 0; i < 6; i++)
        {
            using var response = await TrySignInAsync(client, TestUsers.Owner.Email, "wrong-password-0000");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        // Assert
        Assert.That(delay.Requested, Is.EqualTo(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) }));
    }

    [Test]
    public async Task SignIn_AnUnknownEmail_IsDelayedAndAnsweredExactlyLikeAKnownOne()
    {
        // Arrange
        var knownDelay = new RecordingSignInDelay();
        var unknownDelay = new RecordingSignInDelay();
        using var knownFactory = EnabledFactory(knownDelay);
        using var unknownFactory = EnabledFactory(unknownDelay);
        using var knownClient = knownFactory.CreateApiClient();
        using var unknownClient = unknownFactory.CreateApiClient();
        var known = new List<string>();
        var unknown = new List<string>();

        // Act
        for (var i = 0; i < 6; i++)
        {
            using var a = await TrySignInAsync(knownClient, TestUsers.Owner.Email, "wrong-password-0000");
            known.Add($"{(int)a.StatusCode} {await a.Content.ReadAsStringAsync()}");
            using var b = await TrySignInAsync(unknownClient, "nobody@example.com", "wrong-password-0000");
            unknown.Add($"{(int)b.StatusCode} {await b.Content.ReadAsStringAsync()}");
        }

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(unknown, Is.EqualTo(known));
            Assert.That(unknownDelay.Requested, Is.EqualTo(knownDelay.Requested));
        }
    }

    [Test]
    public async Task SignIn_WhileAnotherAttemptForTheSameEmailIsWaiting_Returns429WithRetryAfter()
    {
        // Arrange
        var delay = new RecordingSignInDelay();
        using var factory = EnabledFactory(delay);
        using var client = factory.CreateApiClient();
        for (var i = 0; i < 4; i++)
        {
            using var warmUp = await TrySignInAsync(client, TestUsers.Owner.Email, "wrong-password-0000");
        }

        delay.HoldNext();
        var waiting = TrySignInAsync(client, TestUsers.Owner.Email, TestUsers.Password);
        await delay.WaitUntilHeldAsync();

        // Act
        using var rejected = await TrySignInAsync(client, "OWNER@example.com", TestUsers.Password);
        delay.Release();
        using var released = await waiting;

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(rejected, HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rejected.Headers.RetryAfter?.Delta, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(released.StatusCode, Is.EqualTo(HttpStatusCode.OK), "等的那一個照常驗，密碼對就登入");
            Assert.That(factory.Modules.Authentication.SignInCalls, Has.Count.EqualTo(5), "被擋下的沒有進到驗證");
        }
    }

    [Test]
    public async Task SignIn_AfterTheWaitIsCancelled_TheSameSourceCanTryAgainRightAway()
    {
        // Arrange
        var delay = new RecordingSignInDelay();
        using var factory = EnabledFactory(delay);
        using var client = factory.CreateApiClient();
        for (var i = 0; i < 4; i++)
        {
            using var warmUp = await TrySignInAsync(client, TestUsers.Owner.Email, "wrong-password-0000");
        }

        delay.HoldNext();
        using var cancel = new CancellationTokenSource();
        var waiting = client.PostAsync(Sessions.Route, Sessions.Credentials(TestUsers.Owner.Email, TestUsers.Password), cancel.Token);
        await delay.WaitUntilHeldAsync();
        await cancel.CancelAsync();
        Assert.That(async () => await waiting, Throws.InstanceOf<OperationCanceledException>());
        await delay.WaitUntilCancelledAsync();
        delay.Release();

        // Act
        using var again = await TrySignInAsync(client, TestUsers.Owner.Email, TestUsers.Password);

        // Assert
        Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task SignIn_WhenReadingTheRequestSourceThrows_TheSlotIsStillGivenBack()
    {
        // Arrange
        var delay = new RecordingSignInDelay();
        var source = new SwitchableRequestSource();
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions
        {
            ConfigureServices = services =>
            {
                services.RemoveAll<SignInThrottleLimits>();
                services.AddSingleton(new SignInThrottleLimits());
                services.RemoveAll<ISignInDelay>();
                services.AddSingleton<ISignInDelay>(delay);
                services.RemoveAll<PmTool.Abstractions.IRequestSource>();
                services.AddSingleton<PmTool.Abstractions.IRequestSource>(source);
            },
        });
        using var client = factory.CreateApiClient();
        for (var i = 0; i < 4; i++)
        {
            using var warmUp = await TrySignInAsync(client, TestUsers.Owner.Email, "wrong-password-0000");
        }

        source.Fail = true;
        using var broken = await TrySignInAsync(client, TestUsers.Owner.Email, TestUsers.Password);
        source.Fail = false;

        // Act
        using var again = await TrySignInAsync(client, TestUsers.Owner.Email, TestUsers.Password);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(broken.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
            Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }

    [Test]
    public async Task SignIn_WhileTheSameEmailIsWaiting_ADifferentEmailIsNotRejected()
    {
        // Arrange
        var delay = new RecordingSignInDelay();
        using var factory = EnabledFactory(delay);
        using var client = factory.CreateApiClient();
        for (var i = 0; i < 4; i++)
        {
            using var warmUp = await TrySignInAsync(client, TestUsers.Owner.Email, "wrong-password-0000");
        }

        delay.HoldNext();
        var waiting = TrySignInAsync(client, TestUsers.Owner.Email, TestUsers.Password);
        await delay.WaitUntilHeldAsync();

        // Act
        using var other = await TrySignInAsync(client, TestUsers.Member.Email, TestUsers.Password);
        delay.Release();
        using var released = await waiting;

        // Assert
        Assert.That(other.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task SignIn_ASuccess_ResetsTheCount()
    {
        // Arrange
        var delay = new RecordingSignInDelay();
        using var factory = EnabledFactory(delay);
        using var client = factory.CreateApiClient();
        for (var i = 0; i < 5; i++)
        {
            using var wrong = await TrySignInAsync(client, TestUsers.Owner.Email, "wrong-password-0000");
        }

        using var success = await TrySignInAsync(client, TestUsers.Owner.Email, TestUsers.Password);
        delay.Clear();

        // Act
        using var again = await TrySignInAsync(client, TestUsers.Owner.Email, "wrong-password-0000");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(delay.Requested, Is.Empty);
        }
    }

    [Test]
    public async Task SignIn_FifteenMinutesAfterTheLastFailure_IsNotDelayedAgain()
    {
        // Arrange
        var delay = new RecordingSignInDelay();
        using var factory = EnabledFactory(delay);
        using var client = factory.CreateApiClient();
        for (var i = 0; i < 5; i++)
        {
            using var wrong = await TrySignInAsync(client, TestUsers.Owner.Email, "wrong-password-0000");
        }

        delay.Clear();
        factory.Modules.Time.Advance(TimeSpan.FromMinutes(15));

        // Act
        using var again = await TrySignInAsync(client, TestUsers.Owner.Email, "wrong-password-0000");

        // Assert
        Assert.That(delay.Requested, Is.Empty);
    }

    [Test]
    public async Task SignIn_ByDefaultInTests_NeverDelays()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();

        // Act
        for (var i = 0; i < 8; i++)
        {
            using var response = await TrySignInAsync(client, TestUsers.Owner.Email, "wrong-password-0000");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        // Assert
        Assert.That(factory.Services.GetRequiredService<ISignInDelay>(), Is.InstanceOf<TimeProviderSignInDelay>());
    }
}

/// <summary>可以切換成丟例外的請求來源，模擬取來源時出錯。</summary>
internal sealed class SwitchableRequestSource : PmTool.Abstractions.IRequestSource
{
    public bool Fail { get; set; }

    public PmTool.Abstractions.RequestSource? Current =>
        Fail ? throw new InvalidOperationException("來源讀不到。") : null;
}

/// <summary>記下被要求等多久，立刻回傳；也可以讓下一次停住，直到測試放行。</summary>
internal sealed class RecordingSignInDelay : ISignInDelay
{
    private readonly ConcurrentQueue<TimeSpan> _requested = new();
    private TaskCompletionSource? _gate;
    private readonly TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<TimeSpan> Requested => [.. _requested];

    public void Clear() => _requested.Clear();

    public void HoldNext() => _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource _cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitUntilCancelledAsync() => _cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));

    public Task WaitUntilHeldAsync() => _held.Task.WaitAsync(TimeSpan.FromSeconds(10));

    public void Release() => _gate?.TrySetResult();

    public async Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        _requested.Enqueue(delay);
        if (_gate is { } gate)
        {
            _held.TrySetResult();
            try
            {
                await gate.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _cancelled.TrySetResult();
                throw;
            }
        }
    }
}
