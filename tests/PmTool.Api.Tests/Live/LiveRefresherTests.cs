using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Api.Live;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests.Live;

/// <summary>定期重新檢查連線：失效的斷開，查詢出錯的維持原樣。</summary>
[TestFixture]
public sealed class LiveRefresherTests
{
    [Test]
    public async Task RunOnce_SessionLookupFails_KeepsTheConnectionsAndLooksAgainNextTime()
    {
        // Arrange
        using var factory = LiveGuardTests.NewFactory();
        using var signIn = factory.CreateApiClient();
        var cookie = await Sessions.SignInForCookieAsync(signIn, TestUsers.Member);
        // WebSocket 連線建立之後不會再有 HTTP 請求去問工作階段，查詢出錯只會影響重新檢查。
        await using var client = LiveHubClient.Create(factory.Server, cookie, HttpTransportType.WebSockets);
        await client.StartAsync();
        var refresher = factory.Services.GetRequiredService<LiveConnectionRefresher>();
        factory.Modules.Sessions.BeforeFind = _ => throw new TimeoutException("身分模組暫時連不上");

        // Act
        var whileFailing = await refresher.RunOnceAsync(CancellationToken.None);
        factory.Modules.Sessions.BeforeFind = null;
        var afterwards = await refresher.RunOnceAsync(CancellationToken.None);

        // Assert
        Assert.That((whileFailing, afterwards), Is.EqualTo((0, 0)));
        Assert.That(client.Closed, Is.False);
    }

    [Test]
    public async Task RunOnce_DoesNotUseFindAsync_SoAnIdleTabCannotKeepItsSessionAlive()
    {
        // Arrange：FindAsync 會記下使用的時刻、延長閒置期限；重新檢查只能問「還有沒有效」。
        using var factory = LiveGuardTests.NewFactory();
        using var signIn = factory.CreateApiClient();
        var cookie = await Sessions.SignInForCookieAsync(signIn, TestUsers.Member);
        await using var client = LiveHubClient.Create(factory.Server, cookie, HttpTransportType.WebSockets);
        await client.StartAsync();
        var findsBefore = factory.Modules.Sessions.FindCalls.Count;

        // Act
        await factory.Services.GetRequiredService<LiveConnectionRefresher>().RunOnceAsync(CancellationToken.None);

        // Assert
        Assert.That(factory.Modules.Sessions.FindCalls, Has.Count.EqualTo(findsBefore));
    }

    [Test]
    public async Task RunOnce_EndedSession_AbortsOnlyThatSessionsConnections()
    {
        // Arrange
        using var factory = LiveGuardTests.NewFactory();
        using var firstSignIn = factory.CreateApiClient();
        using var secondSignIn = factory.CreateApiClient();
        var ended = await Sessions.SignInForCookieAsync(firstSignIn, TestUsers.Member);
        var kept = await Sessions.SignInForCookieAsync(secondSignIn, TestUsers.Member);
        await using var endedClient = LiveHubClient.Create(factory.Server, ended);
        await using var keptClient = LiveHubClient.Create(factory.Server, kept);
        await endedClient.StartAsync();
        await keptClient.StartAsync();
        foreach (var token in factory.Modules.Sessions.TokensOf(TestUsers.Member.AccountId).Take(1))
        {
            factory.Modules.Sessions.End(token);
        }

        // Act
        var aborted = await factory.Services.GetRequiredService<LiveConnectionRefresher>().RunOnceAsync(CancellationToken.None);
        await endedClient.WaitUntilClosedAsync();

        // Assert
        Assert.That(aborted, Is.EqualTo(1));
        Assert.That(keptClient.Closed, Is.False);
    }

    [Test]
    public async Task RunOnce_PendingMigrations_SkipsTheRoundEvenForAnEndedSession()
    {
        // Arrange
        using var factory = LiveGuardTests.NewFactory();
        var migrations = new StubMigrationStatus();
        factory.Modules.MigrationStatuses.Add(migrations);
        using var signIn = factory.CreateApiClient();
        var cookie = await Sessions.SignInForCookieAsync(signIn, TestUsers.Member);
        await using var client = LiveHubClient.Create(factory.Server, cookie, HttpTransportType.WebSockets);
        await client.StartAsync();
        migrations.Pending = ["20261121_Pending"];
        foreach (var token in factory.Modules.Sessions.TokensOf(TestUsers.Member.AccountId))
        {
            factory.Modules.Sessions.End(token);
        }

        // Act：工作階段已經結束，但遷移沒做完時什麼都不做。
        var aborted = await factory.Services.GetRequiredService<LiveConnectionRefresher>().RunOnceAsync(CancellationToken.None);

        // Assert
        Assert.That(aborted, Is.Zero);
        Assert.That(client.Closed, Is.False);
    }

    [Test]
    public async Task RunOnce_CheckingTheMigrationsFails_DoesNotThrow()
    {
        // Arrange：連「有沒有沒套用的遷移」都查不到（外層的例外）。
        using var factory = LiveGuardTests.NewFactory();
        factory.Modules.MigrationStatuses.Add(new StubMigrationStatus { FailWith = new TimeoutException("資料庫連不上") });

        // Act & Assert
        Assert.That(await factory.Services.GetRequiredService<LiveConnectionRefresher>().RunOnceAsync(CancellationToken.None), Is.Zero);
    }

    [Test]
    public async Task Refresh_OnlyThePeopleGiven_LeavesTheOthersAlone()
    {
        // Arrange：兩個人的工作階段都結束了，只重新檢查其中一個。
        using var factory = LiveGuardTests.NewFactory();
        using var memberSignIn = factory.CreateApiClient();
        using var ownerSignIn = factory.CreateApiClient();
        var memberCookie = await Sessions.SignInForCookieAsync(memberSignIn, TestUsers.Member);
        var ownerCookie = await Sessions.SignInForCookieAsync(ownerSignIn, TestUsers.Owner);
        await using var member = LiveHubClient.Create(factory.Server, memberCookie, HttpTransportType.WebSockets);
        await using var owner = LiveHubClient.Create(factory.Server, ownerCookie, HttpTransportType.WebSockets);
        await member.StartAsync();
        await owner.StartAsync();
        foreach (var token in factory.Modules.Sessions.TokensOf(TestUsers.Member.AccountId).Concat(factory.Modules.Sessions.TokensOf(TestUsers.Owner.AccountId)))
        {
            factory.Modules.Sessions.End(token);
        }

        // Act
        var aborted = await factory.Services.GetRequiredService<LiveConnectionRefresher>()
            .RefreshAsync([TestUsers.Member.PersonId], CancellationToken.None);
        await member.WaitUntilClosedAsync();

        // Assert
        Assert.That(aborted, Is.EqualTo(1));
        Assert.That(owner.Closed, Is.False);
    }
}
