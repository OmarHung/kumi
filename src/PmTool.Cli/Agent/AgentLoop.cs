using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Cli.Api;

namespace PmTool.Cli.Agent;

/// <summary>
/// 本機代理的主迴圈：登記並長輪詢領工作、同時最多跑 <see cref="AgentOptions.MaxConcurrentJobs"/> 件、回報進度、送結束。
/// 只往外連，沒有開任何連接埠。網路出問題時退避重試；權杖失效（401／403）就停下來說明。
/// </summary>
internal sealed class AgentLoop(
    ApiClient api,
    IReadOnlyList<IAgentEngine> engines,
    string machineKey,
    string machineName,
    string agentVersion,
    AgentOptions options,
    AgentConsole console,
    Func<DateTimeOffset> now)
{
    private sealed record RunningJob(Guid JobId, Task Task, CancellationTokenSource Cancel);

    private CliException? _fatal;
    private bool _shuttingDown;

    /// <summary>跑到 <paramref name="stop"/> 取消為止（Ctrl-C）；正在跑的照「中斷」結束、送出結果之後才回來。權杖失效時丟 <see cref="CliException"/>。</summary>
    public async Task RunAsync(CancellationToken stop)
    {
        using var loopStop = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var running = new List<RunningJob>();
        var failures = 0;
        try
        {
            while (!loopStop.IsCancellationRequested)
            {
                running.RemoveAll(job => job.Task.IsCompleted);
                var capacity = options.MaxConcurrentJobs - running.Count;
                var started = Stopwatch.GetTimestamp();
                JsonElement? response;
                try
                {
                    response = await CheckInAsync(capacity, loopStop.Token);
                    failures = 0;
                }
                catch (OperationCanceledException) when (loopStop.IsCancellationRequested)
                {
                    break;
                }
                catch (CliException exception) when (IsTransient(exception))
                {
                    failures++;
                    var wait = Backoff(failures);
                    console.Notice($"{exception.Message}（{wait.TotalSeconds:0} 秒後重試）");
                    await WaitAsync(wait, loopStop.Token);
                    continue;
                }
                catch (CliException exception)
                {
                    throw Explain(exception);
                }

                if (response?.Get("job") is { } jobElement)
                {
                    StartJob(jobElement, running, loopStop);
                }
                else
                {
                    await IdleAsync(capacity, running, started, loopStop.Token);
                }
            }
        }
        finally
        {
            await ShutDownAsync(running);
        }

        if (_fatal is not null)
        {
            throw Explain(_fatal);
        }
    }

    /// <summary>沒領到工作時的等待：滿了就等一件結束（或該登記了），長輪詢很快就回來就補足最短的間隔。</summary>
    private async Task IdleAsync(int capacity, List<RunningJob> running, long started, CancellationToken cancellation)
    {
        if (capacity == 0)
        {
            // 已經滿了：不必一直登記，等其中一件結束或該登記（讓伺服器知道還在線）了再說。
            await Task.WhenAny([.. running.Select(job => job.Task), WaitAsync(options.BusyCheckInInterval, cancellation)]);
        }
        else if (Stopwatch.GetElapsedTime(started) < options.MinPollGap)
        {
            await WaitAsync(options.MinPollGap - Stopwatch.GetElapsedTime(started), cancellation);
        }
    }

    private async Task<JsonElement?> CheckInAsync(int capacity, CancellationToken cancellation)
    {
        var body = new JsonObject
        {
            ["machineKey"] = machineKey,
            ["machineName"] = machineName,
            ["agentVersion"] = agentVersion,
            ["engines"] = new JsonArray([.. engines.Select(engine => (JsonNode?)new JsonObject
            {
                ["kind"] = engine.Kind,
                ["version"] = engine.Version,
                ["models"] = new JsonArray([.. engine.Models.Select(model => (JsonNode?)model.ToJson())]),
            })]),
            ["capacity"] = capacity,
        };
        return await api.PostAsync("agent/check-in", body, cancellation);
    }

    private void StartJob(JsonElement jobElement, List<RunningJob> running, CancellationTokenSource loopStop)
    {
        if (AgentJob.Parse(jobElement) is not { } job)
        {
            console.Notice("伺服器送來的工作少了必要的欄位，已經略過。");
            return;
        }

        // 只信自己數的：伺服器多派一件、或重複派同一件，都不能讓同時在跑的超過上限。
        if (running.Count >= options.MaxConcurrentJobs || running.Any(existing => existing.JobId == job.JobId))
        {
            console.Notice("伺服器送來的工作超過同時能跑的件數或編號重複，已經略過。");
            return;
        }

        var cancel = new CancellationTokenSource();
        running.Add(new RunningJob(job.JobId, Task.Run(() => RunJobAsync(job, cancel, loopStop)), cancel));
    }

    private async Task RunJobAsync(AgentJob job, CancellationTokenSource cancel, CancellationTokenSource loopStop)
    {
        using (cancel)
        {
            var started = Stopwatch.GetTimestamp();
            var reporter = new JobReporter(api, job.JobId, options, () => TryCancel(cancel), fatal => OnFatal(fatal, loopStop));
            using var pumpStop = new CancellationTokenSource();
            var pump = reporter.PumpAsync(pumpStop.Token);
            var outcome = await RunEngineAsync(job, reporter, cancel.Token);
            await pumpStop.CancelAsync();
            await pump;
            // 結果一定要送：Ctrl-C 之後也一樣，所以不用代理的取消，只給一個短的時限。
            using var finishTimeout = new CancellationTokenSource(options.FinishTimeout);
            await FinishAsync(job, reporter, outcome, finishTimeout.Token);
            console.JobFinished(new JobSummary(job, outcome, Stopwatch.GetElapsedTime(started), now()));
        }
    }

    private async Task<JobOutcome> RunEngineAsync(AgentJob job, JobReporter reporter, CancellationToken cancellation)
    {
        var engine = engines.FirstOrDefault(candidate => candidate.Kind == job.Engine);
        if (engine is null)
        {
            return JobOutcome.Failed($"這台電腦沒有提供 {EngineKinds.DisplayName(job.Engine)}。");
        }

        JobOutcome outcome;
        try
        {
            outcome = await engine.RunAsync(job, reporter, cancellation);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 只說型別：例外的訊息可能帶著引擎的輸出或路徑。
            return JobOutcome.Failed($"本機代理執行時出了預期之外的錯誤（{exception.GetType().Name}）。");
        }

        // 被取消（使用者按「停止」、伺服器逾時、Ctrl-C）的工作一律以「中斷」結束，不管引擎被打斷時吐了什麼
        // （例如 Claude Code 會吐一個 is_error 的 result，裡面是它的診斷字串）：那些不送給伺服器，也不印出來。
        if (cancellation.IsCancellationRequested)
        {
            return JobOutcome.Stopped(_shuttingDown ? "你電腦上的 Kumi 代理被關掉了。" : null);
        }

        return outcome;
    }

    private async Task FinishAsync(AgentJob job, JobReporter reporter, JobOutcome outcome, CancellationToken cancellation)
    {
        try
        {
            await reporter.FlushRemainingAsync(cancellation);
            await PostFinishAsync(job, outcome, cancellation);
        }
        catch (OperationCanceledException)
        {
            console.Notice("送出結果超過時限，伺服器會當作逾時。");
        }
    }

    private async Task PostFinishAsync(AgentJob job, JobOutcome outcome, CancellationToken cancellation)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await api.PostAsync($"agent/jobs/{job.JobId}/finish", outcome.ToFinishJson(), cancellation);
                return;
            }
            catch (CliException exception) when (IsTransient(exception) && attempt < 3)
            {
                await WaitAsync(Backoff(attempt), cancellation);
            }
            catch (CliException exception)
            {
                // 404：伺服器已經結束這件工作了，沒事。其他：送不出去也只能說一聲，下一件工作照常。
                if (exception.HttpStatus is 401 or 403)
                {
                    _fatal ??= exception;
                }
                else if (exception.HttpStatus != 404)
                {
                    console.Notice($"沒辦法送出工作的結果：{exception.Message}");
                }

                return;
            }
        }
    }

    private void OnFatal(CliException exception, CancellationTokenSource loopStop)
    {
        _fatal ??= exception;
        loopStop.Cancel();
    }

    private async Task ShutDownAsync(List<RunningJob> running)
    {
        _shuttingDown = true;
        foreach (var job in running)
        {
            TryCancel(job.Cancel);
        }

        await Task.WhenAll(running.Select(job => job.Task));
    }

    private static void TryCancel(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 工作已經結束了。
        }
    }

    /// <summary>網路不通、逾時、伺服器出錯、被限流：等一下再試；其他（權杖無效、沒權限、不認得的端點）重試也一樣，要人處理。</summary>
    internal static bool IsTransient(CliException exception) => exception.HttpStatus is null or >= 500 or 429;

    private TimeSpan Backoff(int failures) => BackoffFor(failures, options.MaxBackoff);

    /// <summary>1、2、4…秒，最多 <paramref name="max"/>；開機自動啟動的背景程序開頭連不上伺服器時也用同一套。</summary>
    internal static TimeSpan BackoffFor(int failures, TimeSpan max)
    {
        var wait = TimeSpan.FromSeconds(Math.Pow(2, Math.Min(failures - 1, 10)));
        return wait > max ? max : wait;
    }

    /// <summary>等待；被取消時不丟例外，呼叫端的迴圈自己看取消的旗標收尾。</summary>
    private async Task WaitAsync(TimeSpan duration, CancellationToken cancellation)
    {
        try
        {
            await options.Delay(duration, cancellation);
        }
        catch (OperationCanceledException)
        {
            // 迴圈的條件會看到已經取消。
        }
    }

    private static CliException Explain(CliException exception) => (exception.HttpStatus, exception.Code) switch
    {
        (401, _) => new CliException(ExitCodes.Error, exception.Message, code: exception.Code, httpStatus: 401),
        (403, "ai.personal_disabled") => new CliException(
            ExitCodes.Error,
            "你的組織沒有開放成員使用自己的 AI 帳號，本機代理不能用。請組織管理員到「AI 設定」打開「允許成員使用自己的 AI 帳號」。",
            code: exception.Code, httpStatus: 403),
        (403, _) => new CliException(
            ExitCodes.Error,
            "這把權杖不能當本機代理用。請執行 kumi agent login，用瀏覽器登入並建立本機代理用的權杖（需要 agent:run、blueprint:read、blueprint:write）。",
            code: exception.Code, httpStatus: 403),
        (404, _) => new CliException(ExitCodes.Error, "這個伺服器還不支援本機代理（找不到 /v1/agent/check-in）。請確認伺服器的版本。", httpStatus: 404),
        _ => exception,
    };
}
