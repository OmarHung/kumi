namespace PmTool.Identity.Tests;

/// <summary>唯讀的「還有效嗎」：和 FindAsync 同一個定義，但不記使用時刻、不續命。長時間的即時連線定期用它確認。</summary>
[TestFixture]
public sealed class SessionIsActiveTests
{
    private static readonly TimeSpan IdleLimit = TimeSpan.FromHours(IdentityRules.SessionIdleHours);
    private static readonly TimeSpan AbsoluteLimit = TimeSpan.FromDays(IdentityRules.SessionAbsoluteDays);
    private static readonly TimeSpan Moment = TimeSpan.FromSeconds(1);

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

    private Task<bool> IsActiveAsync(StartedSession session) => _host.Sessions.IsActiveAsync(session.Token, CancellationToken.None);

    [Test]
    public async Task IsActiveAsync_FreshSession_IsTrue()
    {
        // Arrange
        var session = await _host.StartSessionAsync(await _host.AddUserAsync(_owner.ToActor()));

        // Act & Assert
        Assert.That(await IsActiveAsync(session), Is.True);
    }

    [Test]
    public async Task IsActiveAsync_UnknownOrMalformedToken_IsFalse()
    {
        // Act & Assert
        Assert.That(await _host.Sessions.IsActiveAsync("not-a-token", CancellationToken.None), Is.False);
        Assert.That(await _host.Sessions.IsActiveAsync(new string('A', 43), CancellationToken.None), Is.False);
    }

    [Test]
    public async Task IsActiveAsync_SignedOut_IsFalse()
    {
        // Arrange
        var session = await _host.StartSessionAsync(await _host.AddUserAsync(_owner.ToActor()));
        await _host.Sessions.EndAsync(session.Token, CancellationToken.None);

        // Act & Assert
        Assert.That(await IsActiveAsync(session), Is.False);
    }

    [Test]
    public async Task IsActiveAsync_AccountDisabled_IsFalse()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());
        var session = await _host.StartSessionAsync(user);
        await _host.DisableAccountAsync(user.AccountId);

        // Act & Assert：不經過 FindAsync 的快取，現在就看得到。
        Assert.That(await IsActiveAsync(session), Is.False);
    }

    [Test]
    public async Task IsActiveAsync_IdleForTheIdleLimit_IsFalse()
    {
        // Arrange
        var session = await _host.StartSessionAsync(await _host.AddUserAsync(_owner.ToActor()));

        // Act
        _time.Advance(IdleLimit - Moment);
        var before = await IsActiveAsync(session);
        _time.Advance(Moment);
        var atTheLimit = await IsActiveAsync(session);

        // Assert
        Assert.That((before, atTheLimit), Is.EqualTo((true, false)));
    }

    [Test]
    public async Task IsActiveAsync_AskedRegularly_DoesNotExtendTheIdleLimit()
    {
        // Arrange：每兩小時問一次；如果問了就續命，八小時之後還會有效。
        var session = await _host.StartSessionAsync(await _host.AddUserAsync(_owner.ToActor()));
        for (var i = 0; i < 3; i++)
        {
            _time.Advance(TimeSpan.FromHours(2));
            Assert.That(await IsActiveAsync(session), Is.True);
        }

        // Act
        _time.Advance(TimeSpan.FromHours(2));
        var afterIdleLimit = await IsActiveAsync(session);
        var found = await _host.FindAsync(session);

        // Assert
        Assert.That(afterIdleLimit, Is.False);
        Assert.That(found, Is.Null, "問的人沒有續命，FindAsync 也看得到它閒置過期了");
    }

    [Test]
    public async Task IsActiveAsync_UsedThroughFindAsync_FollowsTheExtendedIdleLimit()
    {
        // Arrange
        var session = await _host.StartSessionAsync(await _host.AddUserAsync(_owner.ToActor()));
        _time.Advance(IdleLimit - Moment);
        Assert.That(await _host.FindAsync(session), Is.Not.Null);

        // Act：真的有人在用，閒置期限從那一刻重算。
        _time.Advance(IdleLimit - Moment);

        // Assert
        Assert.That(await IsActiveAsync(session), Is.True);
    }

    [Test]
    public async Task IsActiveAsync_PastTheAbsoluteLimit_IsFalseEvenIfUsedRegularly()
    {
        // Arrange
        var session = await _host.StartSessionAsync(await _host.AddUserAsync(_owner.ToActor()));
        var step = TimeSpan.FromHours(6);
        var elapsed = TimeSpan.Zero;
        while (elapsed + step < AbsoluteLimit)
        {
            _time.Advance(step);
            elapsed += step;
            await _host.FindAsync(session);
        }

        // Act
        _time.Advance(AbsoluteLimit - elapsed - Moment);
        await _host.FindAsync(session);
        var before = await IsActiveAsync(session);
        _time.Advance(Moment);
        var atTheLimit = await IsActiveAsync(session);

        // Assert
        Assert.That((before, atTheLimit), Is.EqualTo((true, false)));
    }
}
