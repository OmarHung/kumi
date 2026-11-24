using System.Text.Json;

namespace PmTool.Cli.Agent;

/// <summary>鎖現在被誰拿著。<see cref="Pid"/> 讀不到時是 null（拿鎖的人還來不及寫，或寫到一半）。</summary>
internal sealed record AgentHolder(int? Pid, bool Background);

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

    private AgentLock(FileStream stream, string infoPath)
    {
        _stream = stream;
        _infoPath = infoPath;
    }

    /// <summary>拿得到就回鎖（用完要 Dispose）；被別人拿著回 null 與拿著的人。</summary>
    public static (AgentLock? Lock, AgentHolder? Holder) TryAcquire(string configPath, int pid, bool background)
    {
        var directory = Path.GetDirectoryName(configPath)!;
        EnsureDirectory(directory);
        var infoPath = Path.Combine(directory, InfoFileName);
        FileStream stream;
        try
        {
            stream = new FileStream(Path.Combine(directory, FileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return (null, ReadHolder(infoPath));
        }

        var held = new AgentLock(stream, infoPath);
        held.WriteInfo(pid, background);
        return (held, null);
    }

    /// <summary>
    /// 背景（--service）啟動時用：<see cref="Peek"/> 只拿一下子，剛好撞上就多等一下再放棄，免得開機時被 status／enable 的一瞥擋掉。
    /// </summary>
    public static async Task<(AgentLock? Lock, AgentHolder? Holder)> TryAcquireWithRetryAsync(
        string configPath, int pid, bool background, int attempts, TimeSpan interval,
        Func<TimeSpan, CancellationToken, Task> delay, CancellationToken cancellation)
    {
        var result = TryAcquire(configPath, pid, background);
        for (var attempt = 1; result.Lock is null && attempt < attempts; attempt++)
        {
            await delay(interval, cancellation);
            result = TryAcquire(configPath, pid, background);
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

    public void Dispose()
    {
        // 先刪資訊檔再放鎖：反過來的話，放鎖的瞬間別人拿到新鎖、寫了自己的資訊，這裡會把它刪掉。
        try
        {
            File.Delete(_infoPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 資訊檔留著不影響：沒有鎖的時候不會有人讀它。
        }

        _stream.Dispose();
    }

    private static AgentHolder? ReadHolder(string infoPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(infoPath));
            var root = document.RootElement;
            int? pid = root.TryGetProperty("pid", out var pidValue) && pidValue.TryGetInt32(out var parsed) && parsed > 0 ? parsed : null;
            var background = root.TryGetProperty("background", out var flag) && flag.ValueKind == JsonValueKind.True;
            return new AgentHolder(pid, background);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AgentHolder(null, false);
        }
    }

    private void WriteInfo(int pid, bool background)
    {
        if (pid <= 0)
        {
            return;
        }

        try
        {
            File.WriteAllText(_infoPath, JsonSerializer.Serialize(new { pid, background }));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 寫不進去只是讓別人讀不到 PID，鎖本身照樣有效。
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
