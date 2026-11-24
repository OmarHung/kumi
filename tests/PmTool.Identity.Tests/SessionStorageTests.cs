using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;

namespace PmTool.Identity.Tests;

/// <summary>
/// 工作階段怎麼存。這兩件事刻意讓公開介面看不出來（只存雜湊、結束很久的會被刪掉），
/// 所以這裡破例直接看資料表；其他行為都從公開介面觀察。
/// </summary>
[TestFixture]
public sealed class SessionStorageTests
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
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

    [Test]
    public async Task StartAsync_StoresTheHashOfTheTokenAndNeverTheToken()
    {
        // Arrange
        var user = await _host.AddUserAsync(_owner.ToActor());

        // Act
        var session = await _host.StartSessionAsync(user);

        // Assert
        await using var db = await _host.CreateDbContextAsync();
        var row = await db.Sessions.SingleAsync(s => s.Id == session.SessionId);
        Assert.That(row.TokenHash, Is.EqualTo(SHA256.HashData(Base64Url.DecodeFromChars(session.Token))));
        Assert.That(row.AccountId, Is.EqualTo(user.AccountId));
        Assert.That(row.OrganizationId, Is.EqualTo(user.OrganizationId));
    }

    [Test]
    public async Task StartAsync_DeletesSessionsOfTheOrganizationThatEndedMoreThan30DaysAgo()
    {
        // Arrange：登出的、閒置失效的、到絕對期限的各一個，結束超過 30 天；另一個登出剛好不到 30 天。
        var user = await _host.AddUserAsync(_owner.ToActor());
        var signedOut = await _host.StartSessionAsync(user);
        var idle = await _host.StartSessionAsync(user);
        var expired = await _host.StartSessionAsync(user);
        await _host.Sessions.EndAsync(signedOut.Token, CancellationToken.None);
        for (var elapsed = TimeSpan.Zero; elapsed < TimeSpan.FromDays(IdentityRules.SessionAbsoluteDays); elapsed += TimeSpan.FromHours(6))
        {
            _time.Advance(TimeSpan.FromHours(6));
            await _host.FindAsync(expired);
        }

        _time.Advance(Retention + Moment);
        var recent = await _host.StartSessionAsync(user);
        await _host.Sessions.EndAsync(recent.Token, CancellationToken.None);
        _time.Advance(Retention - Moment);

        // Act
        await _host.StartSessionAsync(user);

        // Assert
        await using var db = await _host.CreateDbContextAsync();
        var remaining = await db.Sessions.Where(s => s.AccountId == user.AccountId).Select(s => s.Id).ToListAsync();
        Assert.That(remaining, Does.Contain(recent.SessionId));
        Assert.That(remaining, Has.No.Member(signedOut.SessionId).And.No.Member(idle.SessionId).And.No.Member(expired.SessionId));
    }
}
