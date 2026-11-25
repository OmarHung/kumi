using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Api.Live;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests.Live;

/// <summary>
/// 握手之後就不再讀資料的連線：用真的 Kestrel 與真的 WebSocket，才有真的 TCP 緩衝可以塞滿。
/// 卡住的那一條要被斷開，同一個人別的連線照樣收到每一則。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class LiveSlowReaderTests
{
    private const char RecordSeparator = '\u001e';

    private static readonly TimeSpan SendTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    // 單則訊息不能超過用戶端預設的 32 KiB；大一點才塞得滿緩衝。
    private const int KeysPerMessage = 400;
    private const int MaxRounds = 20_000;

    private static ApiFactory NewFactory() => new(
        FakeBlueprintValidator.Passing(),
        new ApiFactoryOptions
        {
            UseKestrel = true,
            Settings = new Dictionary<string, string?> { [LiveRegistration.EnabledSetting] = "true" },
            ConfigureServices = services => services.AddSingleton(new LiveOptions { SendTimeout = SendTimeout }),
        });

    private static async Task<ClientWebSocket> ConnectWithoutReadingAsync(Uri address, string cookie)
    {
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Cookie", cookie);
        var builder = new UriBuilder(address) { Scheme = "ws", Path = LiveRegistration.Route };
        await socket.ConnectAsync(builder.Uri, CancellationToken.None);

        // SignalR 的 JSON 握手；伺服器回 {} 之後，這一端就再也不讀。
        var handshake = Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}" + RecordSeparator);
        await socket.SendAsync(handshake, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
        var buffer = new byte[256];
        _ = await socket.ReceiveAsync(buffer, CancellationToken.None);
        return socket;
    }

    private static HubConnection ConnectHealthy(Uri address, string cookie)
    {
        var builder = new UriBuilder(address) { Path = LiveRegistration.Route };
        return new HubConnectionBuilder()
            .WithUrl(builder.Uri, options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.Headers["Cookie"] = cookie;
                options.Headers["X-PmTool-Csrf"] = "1";
            })
            .Build();
    }

    private static bool HasBeenStuckLongerThanTheTimeout(LiveConnection connection) =>
        connection.PendingSend is { IsCompleted: false } && DateTimeOffset.UtcNow - connection.SendingSince > SendTimeout * 2;

    private static async Task WaitUntilAsync(Func<bool> condition, string failure)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        Assert.That(condition(), Is.True, failure);
    }

    [Test]
    public async Task SendTo_AConnectionThatStoppedReading_IsAbortedWhileAnotherConnectionOfTheSamePersonKeepsReceivingEveryMessage()
    {
        // Arrange
        using var factory = NewFactory();
        using var signIn = factory.CreateApiClient();
        var address = signIn.BaseAddress!;
        var cookie = await Sessions.SignInForCookieAsync(signIn, TestUsers.Member);
        var connections = factory.Services.GetRequiredService<LiveConnections>();
        var sender = factory.Services.GetRequiredService<LiveSender>();

        using var slow = await ConnectWithoutReadingAsync(address, cookie);
        await using var healthy = ConnectHealthy(address, cookie);
        var received = 0;
        healthy.On<LiveItemsMessage>(LiveMessages.Items, _ => Interlocked.Increment(ref received));
        await healthy.StartAsync();
        await WaitUntilAsync(() => connections.Count == 2, "兩條連線都要登記");
        var slowId = connections.Snapshot().Single(c => c.ConnectionId != healthy.ConnectionId).ConnectionId;
        var payload = new LiveItemsMessage("EXA", [.. Enumerable.Range(0, KeysPerMessage).Select(i => $"EXA-{i}")], null);

        // Act：一直推，直到卡住的那一條送出中的時間超過逾時（斷開已經下過）；條件成立就停，不靠固定的等待。
        var slowConnection = connections.Snapshot().Single(c => c.ConnectionId == slowId);
        var rounds = 0;
        while (rounds < MaxRounds && !HasBeenStuckLongerThanTheTimeout(slowConnection))
        {
            await sender.SendToAsync(connections.Snapshot(), LiveMessages.Items, payload, CancellationToken.None);
            rounds++;
        }

        // Assert
        await WaitUntilAsync(() => connections.Snapshot().All(c => c.ConnectionId != slowId), "不讀資料的連線要被斷開");
        Assert.That(connections.Snapshot().Select(c => c.ConnectionId), Does.Contain(healthy.ConnectionId));
        Assert.That(connections.CountOf(TestUsers.Member.PersonId), Is.EqualTo(1), "斷開的連線要歸還名額");

        await WaitUntilAsync(() => Volatile.Read(ref received) == rounds, $"正常的連線要收到每一則（推了 {rounds} 則）");
    }

    [Test]
    public async Task SendTo_ARoundWithAStuckConnection_TakesAboutOneTimeoutNotOnePerConnection()
    {
        // Arrange：兩條都不讀的連線同時卡住，一輪的時間仍然只有一個逾時。
        using var factory = NewFactory();
        using var signIn = factory.CreateApiClient();
        var address = signIn.BaseAddress!;
        var cookie = await Sessions.SignInForCookieAsync(signIn, TestUsers.Member);
        var connections = factory.Services.GetRequiredService<LiveConnections>();
        var sender = factory.Services.GetRequiredService<LiveSender>();
        using var first = await ConnectWithoutReadingAsync(address, cookie);
        using var second = await ConnectWithoutReadingAsync(address, cookie);
        await WaitUntilAsync(() => connections.Count == 2, "兩條連線都要登記");
        var payload = new LiveItemsMessage("EXA", [.. Enumerable.Range(0, KeysPerMessage).Select(i => $"EXA-{i}")], null);

        // Act：塞滿之後，找到最慢的一輪（兩條都在逾時之後被斷開，之後的輪不再等）。
        TimeSpan slowestRound = TimeSpan.Zero;
        var rounds = 0;
        var stuck = connections.Snapshot();
        while (rounds < MaxRounds && !stuck.All(HasBeenStuckLongerThanTheTimeout))
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            await sender.SendToAsync(stuck, LiveMessages.Items, payload, CancellationToken.None);
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started);
            slowestRound = elapsed > slowestRound ? elapsed : slowestRound;
            rounds++;
        }

        // Assert
        await WaitUntilAsync(() => connections.Count == 0, "兩條卡住的連線都要被斷開");
        Assert.That(connections.CountOf(TestUsers.Member.PersonId), Is.Zero, "斷開的連線要歸還名額");
        Assert.That(slowestRound, Is.LessThan(SendTimeout * 2.5), "兩條一起卡住，一輪也只該等一個逾時");
    }
}
