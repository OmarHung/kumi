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
/// 收到正確的結果之後回 303 導到不帶查詢字串的 <c>/kumi-login/done</c>，登入碼就不會留在網址列與瀏覽紀錄裡；
/// 結果出來之後再撐到那一頁被回應（或 <see cref="DoneGrace"/> 到）才關，瀏覽器才不會看到連線被拒。
/// </summary>
internal sealed class LoopbackReceiver : IDisposable
{
    internal const string CallbackPath = "/kumi-login";
    internal const string DonePath = "/kumi-login/done";
    private const int MaxHeadBytes = 8 * 1024;
    private const int MaxCodeLength = 512;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FlushGrace = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultDoneGrace = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 伺服器只把登入碼送回這個以上的連接埠。作業系統配的暫時埠平常都在這以上（macOS、Windows 從 49152，Linux 從 32768），
    /// 但 Linux 的範圍可以調：配到比較小的就自己在高的範圍裡另外挑，不然同意頁會直接拒絕。
    /// </summary>
    internal const int MinPort = 32768;
    private const int FallbackMinPort = 49152;
    private const int MaxPort = 65535;
    private const int MaxPortAttempts = 20;

    private TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly byte[] _state;
    private readonly List<Task> _handlers = [];
    private readonly TaskCompletionSource _doneServed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeSpan _doneGrace;
    private volatile LoopbackOutcome? _received;

    /// <param name="state">這次登入的 state。</param>
    /// <param name="doneGrace">結果出來之後，最多等多久讓瀏覽器跟著導向取走最後一頁；預設 5 秒。</param>
    public LoopbackReceiver(string state, TimeSpan? doneGrace = null)
    {
        _state = Encoding.ASCII.GetBytes(state);
        _doneGrace = doneGrace ?? DefaultDoneGrace;
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public void Start() => _listener = StartOnAllowedPort(
        _listener,
        port => new TcpListener(IPAddress.Loopback, port),
        () => RandomNumberGenerator.GetInt32(FallbackMinPort, MaxPort + 1));

    /// <summary>啟動 <paramref name="first"/>；配到的連接埠太小就關掉，改試 <paramref name="pickPort"/> 挑的，都被占用時丟 <see cref="SocketException"/>。</summary>
    internal static TcpListener StartOnAllowedPort(TcpListener first, Func<int, TcpListener> create, Func<int> pickPort)
    {
        first.Start();
        if (((IPEndPoint)first.LocalEndpoint).Port >= MinPort)
        {
            return first;
        }

        first.Dispose();
        for (var attempt = 0; attempt < MaxPortAttempts; attempt++)
        {
            var listener = create(pickPort());
            try
            {
                listener.Start();
                return listener;
            }
            catch (SocketException)
            {
                // 剛好被別的程式占用：換一個再試。
                listener.Dispose();
            }
        }

        throw new SocketException((int)SocketError.AddressAlreadyInUse);
    }

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
            if (_received is not null && !cancellation.IsCancellationRequested)
            {
                // 導向後的最後一頁還沒被取走就關掉的話，使用者會看到連線被拒。
                await Task.WhenAny(_doneServed.Task, Task.Delay(_doneGrace, CancellationToken.None));
            }

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
                var (response, result, isDone) = Decide(target);
                // 先記下結果再回應：瀏覽器跟著導向來要最後一頁時，這邊已經知道成功了。
                if (result is not null)
                {
                    _received ??= result;
                }

                await stream.WriteAsync(response, timeout.Token);
                await stream.FlushAsync(timeout.Token);
                if (result is not null)
                {
                    outcome.TrySetResult(result);
                }

                if (isDone)
                {
                    _doneServed.TrySetResult();
                }
            }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
                // 瀏覽器中途斷線或太慢：這條連線作廢，繼續等下一條。
            }
        }
    }

    private (byte[] Response, LoopbackOutcome? Result, bool IsDone) Decide(string? target)
    {
        var notFound = (LoginPages.Response(404, "Not Found", LoginPages.NotFound), (LoopbackOutcome?)null, false);
        if (target is null)
        {
            return notFound;
        }

        var question = target.IndexOf('?', StringComparison.Ordinal);
        var path = question < 0 ? target : target[..question];
        if (path == DonePath)
        {
            return DonePage();
        }

        if (path != CallbackPath)
        {
            return notFound;
        }

        var query = ParseQuery(question < 0 ? string.Empty : target[(question + 1)..]);
        if (!query.TryGetValue("state", out var state) || !FixedTimeEquals(state))
        {
            return (LoginPages.Response(400, "Bad Request", LoginPages.Stale), null, false);
        }

        var done = $"http://127.0.0.1:{Port}{DonePath}";
        if (query.ContainsKey("error"))
        {
            return (LoginPages.See(done), new LoopbackOutcome(LoopbackKind.Denied), false);
        }

        if (!query.TryGetValue("code", out var code) || code.Length is 0 or > MaxCodeLength)
        {
            return (LoginPages.Response(400, "Bad Request", LoginPages.Stale), null, false);
        }

        return (LoginPages.See(done), new LoopbackOutcome(LoopbackKind.Code, code), false);
    }

    /// <summary>登入碼收到之後才有這一頁；還沒收到結果時它什麼都不代表（404），不會被當成成功。</summary>
    private (byte[] Response, LoopbackOutcome? Result, bool IsDone) DonePage() => _received?.Kind switch
    {
        LoopbackKind.Code => (LoginPages.Response(200, "OK", LoginPages.Success), null, true),
        LoopbackKind.Denied => (LoginPages.Response(200, "OK", LoginPages.Denied), null, true),
        _ => (LoginPages.Response(404, "Not Found", LoginPages.NotFound), null, false),
    };

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
