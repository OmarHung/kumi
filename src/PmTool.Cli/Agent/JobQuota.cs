using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PmTool.Cli.Agent;

/// <summary>某個時間範圍內領了幾件、上限是多少（0 是不限）。</summary>
internal sealed record QuotaWindow(int Used, int Limit)
{
    public bool IsUnlimited => Limit == 0;

    public int Remaining => IsUnlimited ? int.MaxValue : Math.Max(0, Limit - Used);
}

/// <summary><paramref name="LimitsAreDefaults"/>：代理沒在跑，上限是預設值（不是哪一次啟動的設定）。</summary>
internal sealed record QuotaUsage(QuotaWindow Hour, QuotaWindow Day, bool LimitsAreDefaults = false);

/// <summary>
/// 本機代理自己的件數上限（每小時、每天）：伺服器端的用量限制是組織的，擋不住「這台電腦每天被用掉整個訂閱額度」；
/// 開機自動啟動之後沒人看著，這是使用者自己的訂閱額度的最後一道門。超過就不領（照樣登記），所以伺服器派不出新的工作。
/// 領走的時間存在設定檔旁邊的 <c>agent-usage.json</c>：重啟、當機都不會讓計數歸零。「今天」依本機時區的日曆日，「這一小時」是往回 60 分鐘。
/// 只算領走的件數（不管後來成功或失敗）：額度是照領走的算。
/// </summary>
internal sealed class JobQuota
{
    public const string FileName = "agent-usage.json";
    public const int DefaultPerHour = 30;
    public const int DefaultPerDay = 200;
    public const int MaxLimit = 100_000;

    private static readonly TimeSpan HourWindow = TimeSpan.FromHours(1);

    // 保留 48 小時的紀錄就夠算「今天」（跨時區、夏令時間也不會漏）；再舊的沒有用。
    private static readonly TimeSpan Retention = TimeSpan.FromHours(48);

    private readonly string _path;
    private readonly int _perHour;
    private readonly int _perDay;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new();
    private List<DateTimeOffset> _starts;

    public JobQuota(string configPath, int perHour, int perDay, Func<DateTimeOffset> now, Action<string>? log = null)
    {
        _path = PathFor(configPath);
        _perHour = perHour;
        _perDay = perDay;
        _now = now;
        var read = ReadStarts(_path);
        _starts = read.starts;
        if (read.corrupt)
        {
            // 計數檔壞了不能當作沒用過（不然改壞檔案就能繞過上限）：保守當作現在的上限都已經用完，原檔改名留著給人看。
            _starts = [.. Enumerable.Repeat(now(), Math.Max(perHour, perDay))];
            log?.Invoke($"{FileName} 壞了，讀不出領過幾件：這一小時與今天都當作已經用到上限（壞掉的檔案留在 {KeepCorruptFile(_path)}），明天才會恢復，或自己刪掉壞檔。");
        }

        // 啟動時就記下這次的上限，agent status 才顯示這次啟動的值。
        Save();
    }

    public static string PathFor(string configPath) => Path.Combine(Path.GetDirectoryName(configPath)!, FileName);

    /// <summary>現在能再領幾件（0 是現在不能領；沒有上限是 <see cref="int.MaxValue"/>）。</summary>
    public int Remaining()
    {
        lock (_gate)
        {
            var usage = Compute(_starts, _perHour, _perDay, _now());
            return Math.Min(usage.Hour.Remaining, usage.Day.Remaining);
        }
    }

    /// <summary>目前超過的是哪一個上限（給記錄用）；沒超過是 null。</summary>
    public string? ExhaustedReason()
    {
        lock (_gate)
        {
            var usage = Compute(_starts, _perHour, _perDay, _now());
            return usage.Hour.Remaining == 0 ? $"每小時 {_perHour} 件" : usage.Day.Remaining == 0 ? $"每天 {_perDay} 件" : null;
        }
    }

    /// <summary>領了一件：還有額度就記下來並回 true；沒額度回 false（不記）。</summary>
    public bool TryReserve()
    {
        lock (_gate)
        {
            var now = _now();
            var usage = Compute(_starts, _perHour, _perDay, now);
            if (Math.Min(usage.Hour.Remaining, usage.Day.Remaining) == 0)
            {
                return false;
            }

            _starts = [.. _starts.Where(start => now - start <= Retention), now];
            Save();
            return true;
        }
    }

    /// <summary>
    /// 不開代理、只讀檔案：<c>agent status</c> 用。代理在跑時上限用那次啟動記下的；沒在跑就用預設值（舊的啟動值已經不算數），並標出來。
    /// 檔案壞了時一樣照保守的算（用到上限）。
    /// </summary>
    public static QuotaUsage Read(string configPath, Func<DateTimeOffset> now, bool agentRunning = true)
    {
        var (starts, perHour, perDay, corrupt) = ReadStarts(PathFor(configPath));
        var hourLimit = agentRunning ? perHour ?? DefaultPerHour : DefaultPerHour;
        var dayLimit = agentRunning ? perDay ?? DefaultPerDay : DefaultPerDay;
        var current = now();
        List<DateTimeOffset> counted = corrupt ? [.. Enumerable.Repeat(current, Math.Max(hourLimit, dayLimit))] : starts;
        return Compute(counted, hourLimit, dayLimit, current) with { LimitsAreDefaults = !agentRunning };
    }

    private static string KeepCorruptFile(string path)
    {
        var kept = $"{path}.corrupt";
        try
        {
            File.Move(path, kept, overwrite: true);
            return kept;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return path;
        }
    }

    internal static QuotaUsage Compute(IReadOnlyList<DateTimeOffset> starts, int perHour, int perDay, DateTimeOffset now)
    {
        var hour = starts.Count(start => (now - start) is { } age && age >= TimeSpan.Zero && age < HourWindow);
        // 「今天」用現在的時區偏移算日曆日：背景程序跑在使用者的本機時區，偏移就是本機的。
        var day = starts.Count(start => start.ToOffset(now.Offset).Date == now.Date);
        return new QuotaUsage(new QuotaWindow(hour, perHour), new QuotaWindow(day, perDay));
    }

    private void Save()
    {
        try
        {
            var document = new JsonObject
            {
                ["perHour"] = _perHour,
                ["perDay"] = _perDay,
                ["starts"] = new JsonArray([.. _starts.Select(start => (JsonNode?)start.ToString("O", CultureInfo.InvariantCulture))]),
            };
            var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.Read };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            // 先寫暫存檔再換掉：寫到一半當機不會留下半個檔案（半個檔案會被當成壞檔、整天不能領）。
            var temporary = _path + ".tmp";
            using (var stream = new FileStream(temporary, options))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(document.ToJsonString());
            }

            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 存不下來就只剩這個程序的記憶體裡有計數：上限照樣有效，只是重啟會歸零。
        }
    }

    private static (List<DateTimeOffset> starts, int? perHour, int? perDay, bool corrupt) ReadStarts(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return ([], null, null, false);
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return ([], null, null, true);
            }

            var starts = root.TryGetProperty("starts", out var array) && array.ValueKind == JsonValueKind.Array
                ? array.EnumerateArray()
                    .Select(item => item.ValueKind == JsonValueKind.String
                        && DateTimeOffset.TryParse(item.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                        ? (DateTimeOffset?)parsed : null)
                    .OfType<DateTimeOffset>().ToList()
                : [];
            return (starts, ReadLimit(root, "perHour"), ReadLimit(root, "perDay"), false);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // 沒有檔案是沒用過（上面已處理）；讀不出來或內容壞掉一律算壞檔，保守處理。
            return ([], null, null, true);
        }
    }

    private static int? ReadLimit(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) && number is >= 0 and <= MaxLimit ? number : null;
}
