using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using PmTool.Api.Live;

namespace PmTool.Api.Tests.Live;

/// <summary>
/// 接上 TestServer 的 SignalR 用戶端，像網頁一樣用 Cookie 登入、帶防偽標頭。收到的 <c>items</c> 與 <c>inbox</c> 訊息依順序放進佇列，
/// 測試用 <see cref="NextAsync"/> 等，或用 <see cref="ExpectNothingAsync"/> 確認什麼都沒來。
/// </summary>
internal sealed class LiveHubClient : IAsyncDisposable
{
    public const string Route = "http://localhost" + LiveRegistration.Route;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Silence = TimeSpan.FromMilliseconds(400);

    private readonly HubConnection _connection;
    private readonly Channel<(string Name, JsonElement Payload)> _messages = Channel.CreateUnbounded<(string, JsonElement)>();

    private LiveHubClient(HubConnection connection)
    {
        _connection = connection;
        connection.On<JsonElement>(LiveMessages.Items, payload => _messages.Writer.TryWrite((LiveMessages.Items, payload)));
        connection.On<JsonElement>(LiveMessages.Inbox, payload => _messages.Writer.TryWrite((LiveMessages.Inbox, payload)));
        connection.Closed += _ =>
        {
            Closed = true;
            return Task.CompletedTask;
        };
    }

    public bool Closed { get; private set; }

    /// <param name="cookie">登入時拿到的 <c>pmtool_session=…</c>。</param>
    /// <param name="transport">預設長輪詢：不必另外接 WebSocket，測試用 TestServer 的處理常式就夠；WebSocket 另有專門的測試。</param>
    /// <param name="origin">模擬瀏覽器帶的 Origin；null 是不帶（非瀏覽器的用戶端）。</param>
    public static LiveHubClient Create(
        TestServer server, string cookie, HttpTransportType transport = HttpTransportType.LongPolling, string? origin = null)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(Route, options =>
            {
                options.Transports = transport;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Headers["Cookie"] = cookie;
                options.Headers["X-PmTool-Csrf"] = "1";
                if (origin is not null)
                {
                    options.Headers["Origin"] = origin;
                }

                options.WebSocketFactory = async (context, cancellationToken) =>
                {
                    var client = server.CreateWebSocketClient();
                    client.ConfigureRequest = request =>
                    {
                        request.Headers.Cookie = cookie;
                        if (origin is not null)
                        {
                            request.Headers.Origin = origin;
                        }
                    };
                    return await client.ConnectAsync(context.Uri, cancellationToken);
                };
            })
            .Build();
        return new LiveHubClient(connection);
    }

    public Task StartAsync() => _connection.StartAsync();

    public async Task<(string Name, JsonElement Payload)> NextAsync()
    {
        using var timeout = new CancellationTokenSource(Patience);
        try
        {
            return await _messages.Reader.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            throw new AssertionException($"{Patience.TotalSeconds} 秒內沒有收到任何訊息。");
        }
    }

    /// <summary>等一小段時間，確認這段時間裡沒有任何訊息進來。</summary>
    public async Task ExpectNothingAsync()
    {
        await Task.Delay(Silence);
        Assert.That(_messages.Reader.TryRead(out var message), Is.False, $"不該收到訊息，卻收到 {message.Name} {message.Payload}");
    }

    /// <summary>等一小段時間，把這段時間裡收到的訊息全部取出。</summary>
    public async Task<IReadOnlyList<(string Name, JsonElement Payload)>> CollectAsync()
    {
        await Task.Delay(Silence);
        List<(string Name, JsonElement Payload)> received = [];
        while (_messages.Reader.TryRead(out var message))
        {
            received.Add(message);
        }

        return received;
    }

    /// <summary>連線被伺服器斷開（或自己關掉）。</summary>
    public async Task WaitUntilClosedAsync()
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!Closed && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.That(Closed, Is.True, "連線應該被伺服器斷開");
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
