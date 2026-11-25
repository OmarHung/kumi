using System.Net;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PmTool.Abstractions;
using PmTool.Api.Cluster;
using PmTool.Api.Live;
using PmTool.Api.Tests.Live;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 同一個資料庫開兩個主機（甲、乙，Cookie 的金鑰在資料庫裡共用），訊號經過真的 LISTEN/NOTIFY：
/// 在甲作廢的工作階段、移除的成員，乙不必等快取到期或兩分鐘的檢查就跟上；接收的連線被砍掉之後重連並整個作廢。
/// </summary>
[TestFixture]
public sealed class ClusterSignalTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private sealed class ResynchronizeCounter : IClusterSignalHandler
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public Task HandleAsync(ClusterSignal signal, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ResynchronizeAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            return Task.CompletedTask;
        }
    }

    private readonly ResynchronizeCounter _resynchronized = new();
    private RealStackFactory _a = null!;
    private RealStackFactory _b = null!;
    private LiveUser _owner = null!;

    private static RealStackOptions Options(Action<IServiceCollection>? configure = null) => new()
    {
        UseEphemeralDataProtection = false,
        ConfigureServices = configure,
        Settings = new Dictionary<string, string?>
        {
            [ClusterRegistration.EnabledSetting] = "true",
            [LiveRegistration.EnabledSetting] = "true",
            [LiveUpdateWorker.EnabledSetting] = "false",
        },
    };

    [OneTimeSetUp]
    public async Task StartTwoHostsAsync()
    {
        var database = await RealStackDatabase.CreateDatabaseAsync();
        _a = new RealStackFactory(database, Options());
        _ = _a.Server;
        _b = new RealStackFactory(database, Options(services => services.AddSingleton<IClusterSignalHandler>(_resynchronized)));
        _ = _b.Server;
        _owner = await LiveUser.SignInAsync(_a, RealStackFactory.OwnerEmail, RealStackFactory.OwnerPassword);
        await WaitForListenersAsync(2);
    }

    [OneTimeTearDown]
    public void StopTheHosts()
    {
        _owner?.Dispose();
        _b?.Dispose();
        _a?.Dispose();
    }

    private async Task<(string Cookie, Guid PersonId)> NewMemberCookieAsync(string name)
    {
        var email = RealStackSteps.UniqueEmail(name);
        var person = await RealStackSteps.CreateMemberAsync(_owner.Client, name, email);
        using var client = Sessions.CreateReplayClient(_a);
        var cookie = await RealStackSteps.SignInForCookieAsync(client, email, RealStackSteps.MemberPassword);
        return (cookie, person["personId"]!.GetValue<Guid>());
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string failure)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail(failure);
    }

    private async Task<long> CountListenersAsync()
    {
        await using var connection = new NpgsqlConnection(_a.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE application_name = @name AND datname = current_database()", connection);
        command.Parameters.AddWithValue("name", ClusterSignalListener.ApplicationName);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private Task WaitForListenersAsync(int expected) =>
        WaitUntilAsync(async () => await CountListenersAsync() >= expected, $"{expected} 條接收訊號的連線都要接上");

    private static async Task<HttpStatusCode> StatusAsync(RealStackFactory host, string cookie)
    {
        using var client = Sessions.CreateReplayClient(host);
        return await Sessions.SendWithCookieAsync(client, Sessions.Route, cookie);
    }

    private static async Task SignOutAsync(RealStackFactory host, string cookie)
    {
        using var client = Sessions.CreateReplayClient(host);
        using var request = Sessions.WithCookie(HttpMethod.Delete, Sessions.Route, cookie);
        using var response = await client.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task SignOutOnOneHost_TheOtherHostRejectsTheSameCookieWithoutWaitingForItsCacheToExpire()
    {
        // Arrange：乙先查過一次，結果進了乙的快取（30 秒）。
        var (cookie, _) = await NewMemberCookieAsync("signout");
        Assert.That(await StatusAsync(_b, cookie), Is.EqualTo(HttpStatusCode.OK));

        // Act
        await SignOutAsync(_a, cookie);

        // Assert
        await WaitUntilAsync(async () => await StatusAsync(_b, cookie) == HttpStatusCode.Unauthorized, "乙要在快取到期之前就拒絕作廢的 Cookie");
    }

    [Test]
    public async Task SignOutOnOneHost_AnOpenLiveConnectionOnTheOtherHostIsClosedAtOnce()
    {
        // Arrange
        var (cookie, person) = await NewMemberCookieAsync("liveout");
        await using var live = LiveHubClient.Create(_b.Server, cookie, HttpTransportType.WebSockets);
        await live.StartAsync();
        var connections = _b.Services.GetRequiredService<LiveConnections>();
        await WaitUntilAsync(() => Task.FromResult(connections.CountOf(person) == 1), "乙上的即時連線要登記");

        // Act
        await SignOutAsync(_a, cookie);

        // Assert：不必等兩分鐘的重新檢查。
        await live.WaitUntilClosedAsync();
        await WaitUntilAsync(() => Task.FromResult(connections.CountOf(person) == 0), "斷開之後名額要還回去");
    }

    [Test]
    public async Task RemovingAMemberOnOneHost_TheOtherHostsLiveConnectionLeavesTheProjectsGroup()
    {
        // Arrange
        var (cookie, person) = await NewMemberCookieAsync("removed");
        var project = await RealStackSteps.CreateProjectAsync(_owner.Client, "CLA", "simple_tasks");
        var group = LiveGroups.Project(project["id"]!.GetValue<Guid>());
        await RealStackSteps.SetMemberAsync(_owner.Client, "CLA", person, "editor");
        await using var live = LiveHubClient.Create(_b.Server, cookie, HttpTransportType.WebSockets);
        await live.StartAsync();
        var connections = _b.Services.GetRequiredService<LiveConnections>();
        await WaitUntilAsync(
            () => Task.FromResult(connections.Snapshot().Any(c => c.PersonId == person && c.Groups.Contains(group))), "乙上的連線要先進專案的群組");

        // Act
        using var removed = await _owner.Client.DeleteAsync($"/v1/projects/CLA/members/{person}");
        Assert.That(removed.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Assert
        await WaitUntilAsync(
            () => Task.FromResult(connections.Snapshot().Where(c => c.PersonId == person).All(c => !c.Groups.Contains(group))),
            "乙上的連線要離開專案的群組");
    }

    [Test]
    public async Task InboxChangedOnOneHost_ReachesAConnectionOnTheOtherHost()
    {
        // Arrange
        var (cookie, person) = await NewMemberCookieAsync("inbox");
        await using var live = LiveHubClient.Create(_b.Server, cookie, HttpTransportType.WebSockets);
        await live.StartAsync();
        var connections = _b.Services.GetRequiredService<LiveConnections>();
        await WaitUntilAsync(
            () => Task.FromResult(connections.Snapshot().Any(c => c.PersonId == person && c.Groups.Contains(LiveGroups.Person(person)))),
            "乙上的連線要先進自己的收件匣群組");

        // Act
        _a.Services.GetRequiredService<IClusterSignals>().Publish(new ClusterSignal(ClusterSignalKind.InboxChanged) { PersonIds = [person] });

        // Assert
        var message = await live.NextAsync();
        Assert.That(message.Name, Is.EqualTo(LiveMessages.Inbox));
    }

    [Test]
    public async Task ListenerConnectionKilled_ReconnectsResynchronizesAndKeepsDeliveringSignals()
    {
        // Arrange
        await WaitForListenersAsync(2);
        var before = _resynchronized.Count;

        // Act：從資料庫那一端砍掉所有接收訊號的連線。
        await using (var connection = new NpgsqlConnection(_a.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var kill = new NpgsqlCommand(
                "SELECT count(pg_terminate_backend(pid)) FROM pg_stat_activity WHERE application_name = @name AND datname = current_database()",
                connection);
            kill.Parameters.AddWithValue("name", ClusterSignalListener.ApplicationName);
            await kill.ExecuteScalarAsync();
        }

        // Assert：重新接上之後整個作廢；之後的訊號照樣送得到。
        await WaitUntilAsync(() => Task.FromResult(_resynchronized.Count > before), "重新接上之後接收端要整個作廢");
        await WaitForListenersAsync(2);
        var (cookie, _) = await NewMemberCookieAsync("afterkill");
        Assert.That(await StatusAsync(_b, cookie), Is.EqualTo(HttpStatusCode.OK));
        await SignOutAsync(_a, cookie);
        await WaitUntilAsync(async () => await StatusAsync(_b, cookie) == HttpStatusCode.Unauthorized, "重連之後乙仍要收到作廢的訊號");
    }
}
