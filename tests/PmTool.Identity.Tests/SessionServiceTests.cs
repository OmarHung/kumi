using System.Buffers.Text;
using System.Security.Cryptography;

namespace PmTool.Identity.Tests;

/// <summary>伺服器端的工作階段：開、查、登出、登出其他裝置，以及閒置與絕對期限。時間用假的時鐘推進。</summary>
[TestFixture]
public sealed class SessionServiceTests
{
    private static readonly TimeSpan IdleLimit = TimeSpan.FromHours(IdentityRules.SessionIdleHours);
    private static readonly TimeSpan AbsoluteLimit = TimeSpan.FromDays(IdentityRules.SessionAbsoluteDays);
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(IdentityRules.SessionCacheSeconds);
    private static readonly TimeSpan Moment = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Step = TimeSpan.FromHours(6);

    private readonly ManualTimeProvider _time = new();
    private IdentityTestHost _host = null!;
    private AuthenticatedUser _owner = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _host = await SessionTestSupport.StartWithClockAsync(_time);
        _owner = await _host.StartWithOwnerAsync();
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task StartAsync_ThenFindAsync_ReturnsTheSessionAndTheUser()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var now = _time.GetUtcNow();

        // Act
        var started = await _host.StartSessionAsync(user);
        var found = await _host.FindAsync(started);

        // Assert
        Assert.That(started.ExpiresAt, Is.EqualTo(now + AbsoluteLimit));
        Assert.That(found, Is.EqualTo(new ActiveSession(started.SessionId, user, started.ExpiresAt)));
    }

    [Test]
    public async Task StartAsync_Token_Is32RandomBytesInBase64UrlAndDifferentEachTime()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());

        // Act
        var first = await _host.StartSessionAsync(user);
        var second = await _host.StartSessionAsync(user);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Token, Does.Match("^[A-Za-z0-9_-]{43}$"));
            Assert.That(Base64Url.DecodeFromChars(first.Token), Has.Length.EqualTo(32));
            Assert.That(second.Token, Is.Not.EqualTo(first.Token));
            Assert.That(second.SessionId, Is.Not.EqualTo(first.SessionId));
        }
    }

    [Test]
    public async Task FindAsync_WellFormedButUnknownToken_ReturnsNull()
    {
        // Act
        var found = await _host.Sessions.FindAsync(Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)), CancellationToken.None);

        // Assert
        Assert.That(found, Is.Null);
    }

    [Test]
    public async Task EndAsync_ThenFindAsync_ReturnsNullRightAway()
    {
        // Arrange：先查一次，結果進了快取；登出要立刻讓它失效，不必等快取過期。
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);
        Assert.That(await _host.FindAsync(session), Is.Not.Null);

        // Act
        await _host.Sessions.EndAsync(session.Token, CancellationToken.None);

        // Assert
        Assert.That(await _host.FindAsync(session), Is.Null);
    }

    [Test]
    public async Task EndAsync_OneSession_LeavesTheOtherSessionsOfTheAccount()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var ending = await _host.StartSessionAsync(user);
        var other = await _host.StartSessionAsync(user);

        // Act
        await _host.Sessions.EndAsync(ending.Token, CancellationToken.None);

        // Assert
        Assert.That(await _host.AreActiveAsync(ending, other), Is.EqualTo(new[] { false, true }));
    }

    [Test]
    public async Task EndAsync_AlreadyEndedUnknownOrMalformedToken_Succeeds()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);
        await _host.Sessions.EndAsync(session.Token, CancellationToken.None);

        // Act & Assert：結果一樣是登出了，不當成錯誤。
        Assert.DoesNotThrowAsync(() => _host.Sessions.EndAsync(session.Token, CancellationToken.None));
        Assert.DoesNotThrowAsync(() => _host.Sessions.EndAsync(Base64Url.EncodeToString(new byte[32]), CancellationToken.None));
        Assert.DoesNotThrowAsync(() => _host.Sessions.EndAsync("not-a-token", CancellationToken.None));
    }

    [Test]
    public async Task FindAsync_IdleForTheIdleLimit_ReturnsNull()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);

        // Act
        _time.Advance(IdleLimit);
        var found = await _host.FindAsync(session);

        // Assert
        Assert.That(found, Is.Null);
    }

    [Test]
    public async Task FindAsync_JustBeforeTheIdleLimit_IsStillValid()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);

        // Act
        _time.Advance(IdleLimit - Moment);
        var found = await _host.FindAsync(session);

        // Assert
        Assert.That(found, Is.Not.Null);
    }

    [Test]
    public async Task FindAsync_UsedRegularly_StaysValidPastTheIdleLimit()
    {
        // Arrange：每隔六小時用一次，從登入算起已經十八小時，但從沒閒置超過八小時。
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);
        var found = new List<bool>();

        // Act
        for (var i = 0; i < 3; i++)
        {
            _time.Advance(Step);
            found.Add(await _host.FindAsync(session) is not null);
        }

        // Assert
        Assert.That(found, Is.All.True);
    }

    [Test]
    public async Task FindAsync_UsedRegularly_ExpiresAtTheAbsoluteLimitCountedFromSignIn()
    {
        // Arrange：每隔六小時用一次，閒置期限一直延長，但登入的時刻不跟著延長。
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);
        var elapsed = TimeSpan.Zero;
        var found = new List<bool>();
        while (elapsed + Step < AbsoluteLimit)
        {
            _time.Advance(Step);
            elapsed += Step;
            found.Add(await _host.FindAsync(session) is not null);
        }

        // Act：到期前一秒查一次（結果進了快取），一秒後就到期，快取裡的結果也不能再用。
        _time.Advance(AbsoluteLimit - elapsed - Moment);
        var justBefore = await _host.FindAsync(session);
        _time.Advance(Moment);
        var atTheLimit = await _host.FindAsync(session);

        // Assert
        Assert.That(found, Is.Not.Empty.And.All.True);
        Assert.That(justBefore, Is.Not.Null);
        Assert.That(atTheLimit, Is.Null);
    }

    [Test]
    public async Task FindAsync_AccountDisabledBehindTheModulesBack_IsRejectedOnceTheCachedResultExpires()
    {
        // Arrange：直接改資料表，模擬別的程序停用了帳號。這個程序的快取最多晚一個快取期間才知道。
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);
        Assert.That(await _host.FindAsync(session), Is.Not.Null);
        await _host.DisableAccountAsync(user.AccountId);

        // Act
        _time.Advance(CacheDuration - Moment);
        var stillCached = await _host.FindAsync(session);
        _time.Advance(Moment);
        var afterTheCache = await _host.FindAsync(session);

        // Assert
        Assert.That(stillCached, Is.Not.Null);
        Assert.That(afterTheCache, Is.Null);
    }

    [Test]
    public async Task FindAsync_PersonRenamed_ReturnsTheCurrentDataOnceTheCachedResultExpires()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);
        await _host.RenamePersonAsync(user.PersonId, "改過名的範例使用者");

        // Act
        _time.Advance(CacheDuration);
        var found = await _host.FindAsync(session);

        // Assert
        Assert.That(found?.User, Is.EqualTo(user with { DisplayName = "改過名的範例使用者" }));
    }

    [Test]
    public async Task EndOthersAsync_EndsTheOtherActiveSessionsOfTheAccountAndCountsThem()
    {
        // Arrange：同一個帳號三個還有效的、一個早就閒置失效的；另一個帳號一個。
        var user = await _host.AddUserAsync(_owner.ToActor());
        var otherUser = await _host.AddUserAsync(_owner.ToActor());
        var idle = await _host.StartSessionAsync(user);
        _time.Advance(IdleLimit);
        var current = await _host.StartSessionAsync(user);
        var second = await _host.StartSessionAsync(user);
        var third = await _host.StartSessionAsync(user);
        var someoneElse = await _host.StartSessionAsync(otherUser);
        Assert.That(await _host.FindAsync(second), Is.Not.Null, "先進快取，作廢時要立刻清掉");

        // Act
        var ended = await _host.Sessions.EndOthersAsync(current.Token, CancellationToken.None);

        // Assert：閒置失效的那一個不算在「登出了幾個」裡。
        Assert.That(ended, Is.EqualTo(2));
        Assert.That(
            await _host.AreActiveAsync(current, second, third, idle, someoneElse),
            Is.EqualTo(new[] { true, false, false, false, true }));
    }

    [Test]
    public async Task EndOthersAsync_NoOtherSessions_ReturnsZero()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);

        // Act
        var ended = await _host.Sessions.EndOthersAsync(session.Token, CancellationToken.None);

        // Assert
        Assert.That(ended, Is.Zero);
        Assert.That(await _host.FindAsync(session), Is.Not.Null);
    }

    [Test]
    public async Task EndOthersAsync_FromAnEndedSession_ReturnsZeroAndEndsNothing()
    {
        // Arrange：已經登出的工作階段不能拿來把別人登出。
        var user = await _host.AddUserAsync(_owner.ToActor());
        var ended = await _host.StartSessionAsync(user);
        var other = await _host.StartSessionAsync(user);
        await _host.Sessions.EndAsync(ended.Token, CancellationToken.None);

        // Act
        var count = await _host.Sessions.EndOthersAsync(ended.Token, CancellationToken.None);

        // Assert
        Assert.That(count, Is.Zero);
        Assert.That(await _host.FindAsync(other), Is.Not.Null);
    }

    [TestCase("not-a-token")]
    [TestCase("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task EndOthersAsync_MalformedOrUnknownToken_ReturnsZero(string token)
    {
        // Act
        var ended = await _host.Sessions.EndOthersAsync(token, CancellationToken.None);

        // Assert
        Assert.That(ended, Is.Zero);
    }
}
