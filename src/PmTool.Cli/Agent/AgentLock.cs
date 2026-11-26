using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PmTool.Cli.Agent;

/// <summary>
/// 鎖現在被誰拿著。<see cref="Pid"/> 讀不到時是 null（拿鎖的人還來不及寫，或寫到一半）。
/// <see cref="Connection"/> 是它最近回報的連線狀態；舊版的 kumi 不回報，也是 null。
/// <see cref="StartedAt"/> 是程序的啟動時間（ps 的 lstart），結束它之前用來確認編號沒有被別的程序重用；舊版不記，是 null。
/// </summary>
internal sealed record AgentHolder(int? Pid, bool Background, AgentConnection? Connection = null, string? StartedAt = null);

/// <summary>
/// 這台電腦上本機代理同時只能有一個：前景的 <c>kumi agent start</c> 與開機自動啟動的背景程序搶同一把鎖，
/// 否則同一台電腦會登記兩次、搶同一個引擎。鎖是設定檔資料夾裡的 <c>agent.lock</c>（<see cref="FileShare.None"/>，
/// 程序結束作業系統就放掉，當掉也不會留下卡死的鎖）。
/// 誰拿著、是不是背景，寫在旁邊的 <c>agent.lock.json</c>：被鎖住的檔案在 Windows 上別的程序連讀都不行。
/// 這個資訊只在鎖被拿著時才可信，所以一律先試著拿鎖、拿不到才讀。
/// </summary>
internal sealed class AgentLock : IDisposable
{
    public const string FileName = "agent.lock";
    public const string InfoFileName = "agent.lock.json";

    private readonly FileStream _stream;
    private readonly string _infoPath;
    private readonly object _gate = new();
    private int _pid;
    private bool _background;
    private string? _startedAt;
    private bool _disposed;

    private AgentLock(FileStream stream, string infoPath)
    {
        _stream = stream;
        _infoPath = infoPath;
    }

    /// <summary>拿得到就回鎖（用完要 Dispose）；被別人拿著回 null 與拿著的人。</summary>
    public static (AgentLock? Lock, AgentHolder? Holder) TryAcquire(string configPath, int pid, bool background, string? startedAt = null)
    {
        var directory = Path.GetDirectoryName(configPath)!;
        EnsureDirectory(directory);
        var infoPath = Path.Combine(directory, InfoFileName);
        FileStream stream;
        try
        {
            stream = new FileStream(Path.Combine(directory, FileName), OwnerOnly(FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException)
        {
            return (null, ReadHolder(infoPath));
        }

        var held = new AgentLock(stream, infoPath);
        held.WriteInfo(pid, background, startedAt: startedAt);
        return (held, null);
    }

    /// <summary>
    /// 背景（--service）啟動時用：<see cref="Peek"/> 只拿一下子，剛好撞上就多等一下再放棄，免得開機時被 status／enable 的一瞥擋掉。
    /// </summary>
    public static async Task<(AgentLock? Lock, AgentHolder? Holder)> TryAcquireWithRetryAsync(
        string configPath, int pid, bool background, int attempts, TimeSpan interval,
        Func<TimeSpan, CancellationToken, Task> delay, CancellationToken cancellation, string? startedAt = null)
    {
        var result = TryAcquire(configPath, pid, background, startedAt);
        for (var attempt = 1; result.Lock is null && attempt < attempts; attempt++)
        {
            await delay(interval, cancellation);
            result = TryAcquire(configPath, pid, background, startedAt);
        }

        return result;
    }

    /// <summary>現在有沒有人拿著鎖；沒有回 null。試著拿一下再放掉，所以不會誤信上一個當掉的程序留下的資訊。</summary>
    public static AgentHolder? Peek(string configPath)
    {
        var directory = Path.GetDirectoryName(configPath)!;
        if (!Directory.Exists(directory))
        {
            return null;
        }

        var (acquired, holder) = TryAcquire(configPath, pid: 0, background: false);
        if (acquired is null)
        {
            return holder ?? new AgentHolder(null, false);
        }

        // 只是看一眼：把剛寫的空資訊收掉，免得蓋掉別人下一刻要寫的。
        acquired.Dispose();
        return null;
    }

    /// <summary>把和伺服器的連線狀態寫進資訊檔，給 <c>kumi agent status</c> 看；回傳有沒有寫成。放掉鎖之後再回報就略過。</summary>
    public bool Report(AgentConnection connection)
    {
        lock (_gate)
        {
            return !_disposed && WriteInfo(_pid, _background, connection, _startedAt);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            // 先刪資訊檔再放鎖：反過來的話，放鎖的瞬間別人拿到新鎖、寫了自己的資訊，這裡會把它刪掉。
            try
            {
                File.Delete(_infoPath);
                File.Delete(TemporaryInfoPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 資訊檔留著不影響：沒有鎖的時候不會有人讀它。
            }

            _stream.Dispose();
        }
    }

    /// <summary>設定資料夾裡的檔案建立時就只有自己讀寫（600），不留「先建出來、再改權限」之間別人讀得到的空檔。</summary>
    internal static FileStreamOptions OwnerOnly(FileMode mode, FileAccess access, FileShare share)
    {
        var options = new FileStreamOptions { Mode = mode, Access = access, Share = share };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return options;
    }

    private static AgentHolder? ReadHolder(string infoPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(infoPath));
            var root = document.RootElement;
            int? pid = root.TryGetProperty("pid", out var pidValue) && pidValue.TryGetInt32(out var parsed) && parsed > 0 ? parsed : null;
            var background = root.TryGetProperty("background", out var flag) && flag.ValueKind == JsonValueKind.True;
            var startedAt = root.TryGetProperty("startedAt", out var started) && started.ValueKind == JsonValueKind.String ? started.GetString() : null;
            return new AgentHolder(pid, background, ReadConnection(root), startedAt);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AgentHolder(null, false);
        }
    }

    /// <summary>連線狀態少了欄位或格式不對就當作沒有：只是給人看的資訊，讀不懂不影響鎖。</summary>
    private static AgentConnection? ReadConnection(JsonElement root)
    {
        if (!root.TryGetProperty("connection", out var connection) || connection.ValueKind != JsonValueKind.Object
            || !connection.TryGetProperty("state", out var state) || state.ValueKind != JsonValueKind.String
            || !connection.TryGetProperty("since", out var since) || since.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(since.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var sinceValue))
        {
            return null;
        }

        var error = connection.TryGetProperty("error", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null;
        return new AgentConnection(state.GetString()!, sinceValue, error);
    }

    private string TemporaryInfoPath => _infoPath + ".tmp";

    private bool WriteInfo(int pid, bool background, AgentConnection? connection = null, string? startedAt = null)
    {
        if (pid <= 0)
        {
            return false;
        }

        _pid = pid;
        _background = background;
        _startedAt = startedAt ?? _startedAt;
        var info = new JsonObject { ["pid"] = pid, ["background"] = background };
        if (_startedAt is not null)
        {
            info["startedAt"] = _startedAt;
        }

        if (connection is not null)
        {
            info["connection"] = new JsonObject
            {
                ["state"] = connection.State,
                ["since"] = connection.Since.ToString("O", CultureInfo.InvariantCulture),
                ["error"] = connection.Error,
            };
        }

        // 先寫暫存檔再換名字：status 隨時會讀，不能讀到寫到一半的檔案（讀不懂會被當成「前景、沒有 PID」）。
        try
        {
            // 暫存檔建立時就只有自己讀寫（600），再換名字：status 不會讀到寫到一半的檔案，別人也讀不到。
            using (var stream = new FileStream(TemporaryInfoPath, OwnerOnly(FileMode.Create, FileAccess.Write, FileShare.None)))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(info.ToJsonString());
            }

            File.Move(TemporaryInfoPath, _infoPath, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 寫不進去只是讓別人讀不到 PID 與連線狀態，鎖本身照樣有效；連線狀態由呼叫的人下次再寫。
            return false;
        }
    }

    private static void EnsureDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
            return;
        }

        Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
