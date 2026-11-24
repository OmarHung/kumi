using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace PmTool.Cli.Login;

internal enum LoopbackKind
{
    Code,
    Denied,
    TimedOut,
}

internal sealed record LoopbackOutcome(LoopbackKind Kind, string? Code = null);

/// <summary>
/// 在 127.0.0.1 的隨機連接埠等瀏覽器導回來（RFC 8252 的原生程式作法）。
/// 用 <see cref="TcpListener"/> 而不是 HttpListener：Windows 的 http.sys 要先替網址保留權限。
/// 只認 <c>GET /kumi-login</c>；state 對不上的（舊的登入頁、別的程式亂打）回說明頁並繼續等，不當成結果。
/// </summary>
internal sealed class LoopbackReceiver : IDisposable
{
    internal const string CallbackPath = "/kumi-login";
    private const int MaxHeadBytes = 8 * 1024;
    private const int MaxCodeLength = 512;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FlushGrace = TimeSpan.FromSeconds(2);

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly byte[] _state;
    private readonly List<Task> _handlers = [];

    public LoopbackReceiver(string state) => _state = Encoding.ASCII.GetBytes(state);

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public void Start() => _listener.Start();

    public async Task<LoopbackOutcome> WaitAsync(TimeSpan timeout, CancellationToken cancellation)
    {
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        guard.CancelAfter(timeout);
        var outcome = new TaskCompletionSource<LoopbackOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var acceptLoop = AcceptAsync(outcome, guard.Token);
        try
        {
            return await outcome.Task.WaitAsync(guard.Token);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return new LoopbackOutcome(LoopbackKind.TimedOut);
        }
        finally
        {
            await guard.CancelAsync();
            _listener.Stop();
            await acceptLoop;
            // 最後那一頁還在送給瀏覽器：給一點時間送完，才不會回到終端機了、瀏覽器卻看到連線中斷。
            Task[] pending;
            lock (_handlers)
            {
                pending = [.. _handlers];
            }

            await Task.WhenAny(Task.WhenAll(pending), Task.Delay(FlushGrace, CancellationToken.None));
        }
    }

    public void Dispose() => _listener.Stop();

    private async Task AcceptAsync(TaskCompletionSource<LoopbackOutcome> outcome, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            // 每條連線自己處理：一條不送完請求的連線（10 秒才放棄）拖不住真正的那一條。
            var handler = HandleAsync(client, outcome);
            lock (_handlers)
            {
                _handlers.Add(handler);
            }
        }
    }

    private async Task HandleAsync(TcpClient client, TaskCompletionSource<LoopbackOutcome> outcome)
    {
        using (client)
        {
            try
            {
                using var timeout = new CancellationTokenSource(RequestTimeout);
                var stream = client.GetStream();
                var target = await ReadRequestTargetAsync(stream, timeout.Token);
                var (response, result) = Decide(target);
                await stream.WriteAsync(response, timeout.Token);
                await stream.FlushAsync(timeout.Token);
                if (result is not null)
                {
                    outcome.TrySetResult(result);
                }
            }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
                // 瀏覽器中途斷線或太慢：這條連線作廢，繼續等下一條。
            }
        }
    }

    private (byte[] Response, LoopbackOutcome? Result) Decide(string? target)
    {
        if (target is null)
        {
            return (LoginPages.Response(404, "Not Found", LoginPages.NotFound), null);
        }

        var question = target.IndexOf('?', StringComparison.Ordinal);
        var path = question < 0 ? target : target[..question];
        if (path != CallbackPath)
        {
            return (LoginPages.Response(404, "Not Found", LoginPages.NotFound), null);
        }

        var query = ParseQuery(question < 0 ? string.Empty : target[(question + 1)..]);
        if (!query.TryGetValue("state", out var state) || !FixedTimeEquals(state))
        {
            return (LoginPages.Response(400, "Bad Request", LoginPages.Stale), null);
        }

        if (query.ContainsKey("error"))
        {
            return (LoginPages.Response(200, "OK", LoginPages.Denied), new LoopbackOutcome(LoopbackKind.Denied));
        }

        if (!query.TryGetValue("code", out var code) || code.Length is 0 or > MaxCodeLength)
        {
            return (LoginPages.Response(400, "Bad Request", LoginPages.Stale), null);
        }

        return (LoginPages.Response(200, "OK", LoginPages.Success), new LoopbackOutcome(LoopbackKind.Code, code));
    }

    private bool FixedTimeEquals(string state) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), _state);

    /// <summary>讀到空行為止（上限 8 KB）；不是 GET 的請求回 null（呼叫端回 404）。</summary>
    private static async Task<string?> ReadRequestTargetAsync(NetworkStream stream, CancellationToken token)
    {
        var buffer = new byte[MaxHeadBytes];
        var length = 0;
        while (length < buffer.Length && !HasBlankLine(buffer, length))
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), token);
            if (read == 0)
            {
                break;
            }

            length += read;
        }

        var text = Encoding.ASCII.GetString(buffer, 0, length);
        var firstLine = text.Split('\n', 2)[0].TrimEnd('\r').Split(' ');
        return firstLine is [var method, var target, ..] && method == "GET" ? target : null;
    }

    private static bool HasBlankLine(byte[] buffer, int length) =>
        Encoding.ASCII.GetString(buffer, 0, length) is var text && (text.Contains("\r\n\r\n", StringComparison.Ordinal) || text.Contains("\n\n", StringComparison.Ordinal));

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            var name = equals < 0 ? pair : pair[..equals];
            var value = equals < 0 ? string.Empty : pair[(equals + 1)..];
            try
            {
                values.TryAdd(Uri.UnescapeDataString(name), Uri.UnescapeDataString(value.Replace('+', ' ')));
            }
            catch (UriFormatException)
            {
                // 壞掉的跳脫序列：當作沒有這個欄位。
            }
        }

        return values;
    }
}
