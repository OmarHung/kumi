using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Api.Live;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests.Live;

/// <summary>連線的名額：不管連線怎麼結束（取消、算群組失敗、斷線）都要歸還。</summary>
[TestFixture]
public sealed class LiveHubQuotaTests
{
    private static async Task WaitForCountAsync(ApiFactory factory, int expected)
    {
        var connections = factory.Services.GetRequiredService<LiveConnections>();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (connections.Count != expected && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        Assert.That(connections.Count, Is.EqualTo(expected));
    }

    [Test]
    public async Task Connect_TheConnectionIsCancelledWhileTheGroupsAreBeingWorkedOut_ReleasesItsSlot()
    {
        // Arrange：算群組卡在半路，直到連線被取消。
        using var factory = LiveGuardTests.NewFactory();
        factory.Modules.Access.BeforeReadable = cancellation => Task.Delay(Timeout.Infinite, cancellation);
        using var signIn = factory.CreateApiClient();
        var cookie = await Sessions.SignInForCookieAsync(signIn, TestUsers.Member);
        await using var client = LiveHubClient.Create(factory.Server, cookie, HttpTransportType.WebSockets);
        await client.StartAsync();
        await WaitForCountAsync(factory, 1);

        // Act
        await client.DisposeAsync();

        // Assert
        await WaitForCountAsync(factory, 0);
        Assert.That(factory.Services.GetRequiredService<LiveConnections>().CountOf(TestUsers.Member.PersonId), Is.Zero);
    }

    [Test]
    public async Task Connect_WorkingOutTheGroupsFails_ReleasesItsSlotAndClosesTheConnection()
    {
        // Arrange
        using var factory = LiveGuardTests.NewFactory();
        factory.Modules.Access.BeforeReadable = _ => throw new TimeoutException("工作引擎暫時連不上");
        using var signIn = factory.CreateApiClient();
        var cookie = await Sessions.SignInForCookieAsync(signIn, TestUsers.Member);
        await using var client = LiveHubClient.Create(factory.Server, cookie, HttpTransportType.WebSockets);

        // Act
        await client.StartAsync();
        await client.WaitUntilClosedAsync();

        // Assert
        await WaitForCountAsync(factory, 0);
    }

    [Test]
    public async Task Disconnect_ReleasesTheSlotSoThePersonCanConnectAgainAtTheLimit()
    {
        // Arrange
        using var factory = LiveGuardTests.NewFactory();
        using var signIn = factory.CreateApiClient();
        var cookie = await Sessions.SignInForCookieAsync(signIn, TestUsers.Member);
        var clients = new List<LiveHubClient>();
        try
        {
            for (var i = 0; i < LiveConnections.MaxPerPerson; i++)
            {
                var client = LiveHubClient.Create(factory.Server, cookie, HttpTransportType.WebSockets);
                clients.Add(client);
                await client.StartAsync();
            }

            await WaitForCountAsync(factory, LiveConnections.MaxPerPerson);

            // Act
            await clients[0].DisposeAsync();
            await WaitForCountAsync(factory, LiveConnections.MaxPerPerson - 1);
            var again = LiveHubClient.Create(factory.Server, cookie, HttpTransportType.WebSockets);
            clients.Add(again);
            await again.StartAsync();

            // Assert
            await WaitForCountAsync(factory, LiveConnections.MaxPerPerson);
        }
        finally
        {
            foreach (var client in clients)
            {
                await client.DisposeAsync();
            }
        }
    }
}
