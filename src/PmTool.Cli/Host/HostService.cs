using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Cli.Agent;
using PmTool.Cli.Api;

namespace PmTool.Cli.Host;

/// <summary>
/// <c>kumi host run</c>：常駐在主機（容器）的 worker。登記並長輪詢領工作、依租約回報、送結束。
/// 設定有問題時<b>不結束</b>（容器會一直重啟）：沒配對、憑證失效、網路不通都記一行、等一等再試；只有 SIGTERM／SIGINT 才結束
/// （正在跑的以「中斷」結束、送出結果）。機器憑證只用來呼叫 <c>/v1/host-agent/*</c>，不進引擎子程序的環境；
/// 引擎拿到的只有這件工作的工具授權。
/// </summary>
internal sealed class HostService(
    HostStateStore store,
    HttpClient http,
    HostEngineProbe probe,
    IReadOnlyList<string> kinds,
    HostOptions hostOptions,
    AgentOptions options,
    AgentConsole console,
    string agentVersion,
    Func<DateTimeOffset> now,
    Action<string> ensureTransport)
{
    private static readonly string[] AllKinds = [EngineKinds.ClaudeCode, EngineKinds.Codex];

    private sealed record RunningJob(Guid JobId, Task Task, CancellationTokenSource Cancel);

    private IReadOnlyList<ProbedEngine> _probes = [];
    private DateTimeOffset _probedAt = DateTimeOffset.MinValue;
    private bool _reprobe;
    private string? _rejectedCredential;
    private DateTimeOffset _retryAt;
    private bool _shuttingDown;
    private bool _disabledNoticed;
    private string _loginSignature = string.Empty;
    private string? _checkedServer;
    private string? _announcedCredential;
    private string? _pendingCredential;

    public async Task RunAsync(CancellationToken stop)
    {
        var running = new List<RunningJob>();
        var failures = 0;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                running.RemoveAll(job => job.Task.IsCompleted);
                var started = Stopwatch.GetTimestamp();
                var outcome = await StepAsync(running, failures, stop);
                failures = outcome.Failures;
                if (outcome.Wait is { } wait)
                {
                    await WaitAsync(wait, stop);
                }
                else if (outcome.Idle is { } capacity)
                {
                    await IdleAsync(capacity, running, started, stop);
                }
            }
        }
        finally
        {
            await ShutDownAsync(running);
        }
    }

    /// <summary>一輪的結果：要等多久（<see cref="Wait"/>）、或領不到工作時的空檔（<see cref="Idle"/> 是這輪登記時的空位）。</summary>
    private sealed record StepResult(int Failures, TimeSpan? Wait = null, int? Idle = null);

    private async Task<StepResult> StepAsync(List<RunningJob> running, int failures, CancellationToken stop)
    {
        HostState state;
        try
        {
            state = store.Load();
        }
        catch (CliException exception)
        {
            console.Notice($"{exception.Message}（{hostOptions.PairingPoll.TotalSeconds:0} 秒後再看）");
            return new StepResult(failures, hostOptions.PairingPoll);
        }

        if (!state.IsPaired)
        {
            console.Notice("等待配對：請在主機上執行 docker compose exec -it ai-worker kumi host pair。");
            return new StepResult(failures, hostOptions.PairingPoll);
        }

        if (state.Credential == _rejectedCredential && now() < _retryAt)
        {
            return new StepResult(failures, hostOptions.StateRecheck);
        }

        var api = new ApiClient(http, state.Server!, state.Credential!)
        {
            ExtraHeaders = HostMachineHeader.For(state.MachineKey ?? store.EnsureMachineKey()),
        };
        var capacity = Math.Max(0, hostOptions.MaxConcurrentJobs - running.Count);
        try
        {
            if (state.Server != _checkedServer)
            {
                // 不是 https 又不是本機的伺服器要明確放行（KUMI_ALLOW_INSECURE）；每個網址只檢查（與警告）一次。
                ensureTransport(state.Server!);
                _checkedServer = state.Server;
            }

            if (await RefreshProbesAsync(state, stop) is { } refreshed)
            {
                state = refreshed;
            }

            var response = await CheckInAsync(api, state, capacity, stop);
            _rejectedCredential = null;
            Announce(state);
            var disabled = await HandleResponseAsync(response, api, state, running, stop);
            AdoptRotatedCredential(state, running);
            return disabled ? new StepResult(0, hostOptions.DisabledCheckIn) : new StepResult(0, Idle: capacity);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return new StepResult(failures);
        }
        catch (CliException exception) when (exception.HttpStatus == 401)
        {
            _rejectedCredential = state.Credential;
            _retryAt = now() + hostOptions.InvalidCredentialRetry;
            console.Notice($"伺服器不認這把機器憑證：這台主機代理被停用、憑證過期，或需要重新配對。請組織擁有者到網頁確認（必要時重新配對）；{hostOptions.InvalidCredentialRetry.TotalSeconds:0} 秒後再試。");
            return new StepResult(failures, hostOptions.StateRecheck);
        }
        catch (CliException exception) when (exception.Code == ApiClient.RedirectCode)
        {
            // 設了 Security:RequireHttps 的部署會把內部的 http 轉到 https：設定不改不會好，說清楚要改哪個設定。
            console.Notice($"{exception.Message} 主機的 worker 要用對外的 https 網址：把 AI_WORKER_SERVER 設成它（和 Security:PublicHost 一致），重啟 ai-worker 後重新配對。（{hostOptions.RedirectRetry.TotalSeconds:0} 秒後再試）");
            return new StepResult(failures, hostOptions.RedirectRetry);
        }
        catch (CliException exception)
        {
            var wait = Backoff(failures + 1);
            console.Notice($"{exception.Message}（{wait.TotalSeconds:0} 秒後重試）");
            return new StepResult(failures + 1, wait);
        }
    }

    /// <summary>
    /// 第一次用這份機器憑證登記成功時記一行（配對之後、重新配對之後、憑證失效又換了新的之後），
    /// 不然日誌只停在「等待配對」，看不出服務已經開始工作。不印憑證。
    /// </summary>
    private void Announce(HostState state)
    {
        if (state.Credential == _announcedCredential)
        {
            return;
        }

        _announcedCredential = state.Credential;
        console.Notice($"已配對「{state.AgentName}」，開始登記到 {state.Server}。");
    }

    private async Task<JsonElement> CheckInAsync(ApiClient api, HostState state, int capacity, CancellationToken cancellation)
    {
        var body = new JsonObject
        {
            ["agentVersion"] = agentVersion,
            ["serverUrl"] = state.Server,
            ["capacity"] = capacity,
            // 伺服器用機器編號認得這台電腦：憑證被拿到別台用時，這裡的編號對不上，伺服器會拒絕並警告擁有者。
            ["machineKey"] = state.MachineKey ?? store.EnsureMachineKey(),
            ["machineName"] = MachineIdentity.ResolveName(null),
            ["engines"] = new JsonArray([.. _probes.Select(engine => (JsonNode?)engine.ToReport(LoginIdOf(state, engine)))]),
        };
        return await api.PostAsync("host-agent/check-in", body, cancellation);
    }

    /// <summary>只在指紋對得上時才報 Kumi 的登入編號：在 Kumi 以外換了登入（指紋不同）就是「未授權」。</summary>
    private static Guid? LoginIdOf(HostState state, ProbedEngine engine)
    {
        if (!engine.LoggedIn || !state.Logins.TryGetValue(engine.Kind, out var login))
        {
            return null;
        }

        return login.Fingerprint is null || engine.Fingerprint is null || login.Fingerprint == engine.Fingerprint ? login.LoginId : null;
    }

    /// <summary>處理登記的回應；回傳代理是不是被停用了。</summary>
    private async Task<bool> HandleResponseAsync(
        JsonElement response, ApiClient api, HostState state, List<RunningJob> running, CancellationToken stop)
    {
        var disabled = response.Flag("disabled");
        if (disabled && !_disabledNoticed)
        {
            console.Notice("這台主機代理被組織擁有者停用了，不會派工作；保持登記。");
        }

        _disabledNoticed = disabled;
        if (response.Text("rotatedCredential") is { Length: > 0 } rotated)
        {
            _pendingCredential = rotated;
        }

        foreach (var kind in response.List("logout").Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!))
        {
            if (AllKinds.Contains(kind))
            {
                await LogoutAsync(kind, stop);
            }
        }

        if (response.Get("job") is { ValueKind: JsonValueKind.Object } jobElement)
        {
            await StartJobAsync(jobElement, api, state, running);
        }

        return disabled;
    }

    /// <summary>
    /// 伺服器發了新的機器憑證（快到期）：沒有工作在跑的時候才存下來換掉。伺服器在新的一把第一次被用到時讓舊的作廢，
    /// 而跑到一半的工作還拿著舊的回報，太早換會讓它們被拒絕。存的時候只換掉「發新憑證時的那一把」：
    /// 這中間擁有者重新配對、狀態檔已經是別把了，就不蓋掉。寫入失敗只提醒，下一次登記伺服器會再發一把。
    /// </summary>
    private void AdoptRotatedCredential(HostState state, List<RunningJob> running)
    {
        if (_pendingCredential is not { } next || running.Any(job => !job.Task.IsCompleted))
        {
            return;
        }

        _pendingCredential = null;
        try
        {
            store.Update(current => current.Credential == state.Credential ? current with { Credential = next } : current);
            console.Notice("機器憑證快到期，已換成伺服器新發的一把。");
        }
        catch (CliException exception)
        {
            console.Notice($"存不下新的機器憑證：{exception.Message}");
        }
    }

    /// <summary>擁有者在網頁登出：執行 CLI 的登出、清掉存的登入編號，之後的登記就報成沒登入。</summary>
    private async Task LogoutAsync(string kind, CancellationToken cancellation)
    {
        var done = await probe.LogoutAsync(kind, cancellation);
        store.Update(state => state.WithLogin(kind, null));
        _reprobe = true;
        console.Notice(done
            ? $"{EngineKinds.DisplayName(kind)} 已依擁有者的要求登出。"
            : $"{EngineKinds.DisplayName(kind)} 的登出沒有成功（或沒有登入），已經清掉 Kumi 的授權；請在主機上確認。");
    }

    /// <summary>啟動時、每 5 分鐘、登出之後與工作失敗像是登入失效時重新探測；探測時發現登入換掉了，順便清掉存的授權。</summary>
    private async Task<HostState?> RefreshProbesAsync(HostState state, CancellationToken cancellation)
    {
        // 另一個程序（kumi host login）剛完成或清掉登入時，不必等 5 分鐘。
        var signature = string.Join(',', state.Logins.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value.LoginId}"));
        if (!_reprobe && signature == _loginSignature && _probes.Count > 0 && now() - _probedAt < hostOptions.ProbeInterval)
        {
            return null;
        }

        _reprobe = false;
        _loginSignature = signature;
        var probed = new List<ProbedEngine>();
        foreach (var kind in AllKinds)
        {
            // 沒要提供的引擎照樣要報（兩個各一筆），報成沒裝。
            probed.Add(kinds.Contains(kind) ? await probe.ProbeAsync(kind, cancellation) : ProbedEngine.NotInstalled(kind, "這台 worker 沒有開放這個引擎。"));
        }

        _probes = probed;
        _probedAt = now();
        var stale = probed.Where(engine => state.Logins.ContainsKey(engine.Kind) && LoginIdOf(state, engine) is null).ToArray();
        if (stale.Length == 0)
        {
            return null;
        }

        foreach (var engine in stale)
        {
            console.Notice($"{EngineKinds.DisplayName(engine.Kind)} 的登入已經不是透過 Kumi 完成的那一份，需要擁有者重新授權。");
        }

        // 只清掉剛才比對的那一份：這中間 kumi host login 可能已經存了新的。
        var observed = stale.ToDictionary(engine => engine.Kind, engine => state.Logins[engine.Kind]);
        return store.Update(current => observed.Aggregate(
            current,
            (next, entry) => current.Logins.TryGetValue(entry.Key, out var latest) && latest == entry.Value ? next.WithLogin(entry.Key, null) : next));
    }

    private async Task StartJobAsync(JsonElement jobElement, ApiClient api, HostState state, List<RunningJob> running)
    {
        if (HostJob.Parse(jobElement) is not { } job)
        {
            console.Notice("伺服器送來的工作少了必要的欄位，已經略過。");
            return;
        }

        // 同一件已經在跑：什麼都不做（它自己會回報、結束）。
        if (running.Any(existing => existing.JobId == job.Job.JobId))
        {
            console.Notice("伺服器重複送來一件正在跑的工作，已經略過。");
            return;
        }

        // 只信自己數的；伺服器已經把它標成執行中，不能默默丟掉，不然要等租約逾時、使用者才看到失敗。
        if (running.Count >= hostOptions.MaxConcurrentJobs)
        {
            console.Notice("伺服器送來的工作超過同時能跑的件數，已經回報失敗。");
            using var timeout = new CancellationTokenSource(options.FinishTimeout);
            await PostFinishAsync(job, api, JobOutcome.Failed("主機代理同時在跑的件數已滿，這則沒有執行，請再送一次。"), timeout.Token);
            return;
        }

        var cancel = new CancellationTokenSource();
        running.Add(new RunningJob(job.Job.JobId, Task.Run(() => RunJobAsync(job, api, state, cancel)), cancel));
    }

    private async Task RunJobAsync(HostJob job, ApiClient api, HostState state, CancellationTokenSource cancel)
    {
        using (cancel)
        {
            var started = Stopwatch.GetTimestamp();
            var reporter = new HostJobReporter(api, job.Job.JobId, job.Lease, options, () => TryCancel(cancel));
            using var pumpStop = new CancellationTokenSource();
            var pump = reporter.PumpAsync(pumpStop.Token);
            var outcome = await RunEngineAsync(job, api, state, reporter, cancel.Token);
            await pumpStop.CancelAsync();
            await pump;
            // 結果一定要送：SIGTERM 之後也一樣，所以不用服務的取消，只給一個短的時限。
            using var finishTimeout = new CancellationTokenSource(options.FinishTimeout);
            await FinishAsync(job, api, reporter, outcome, finishTimeout.Token);
            console.JobFinished(new JobSummary(job.Job, outcome, Stopwatch.GetElapsedTime(started), now()));
            if (outcome.Status == JobStatus.Failed && LooksLikeLoginProblem(outcome.Error))
            {
                _reprobe = true;
            }
        }
    }

    private async Task<JobOutcome> RunEngineAsync(HostJob job, ApiClient api, HostState state, HostJobReporter reporter, CancellationToken cancellation)
    {
        var engine = _probes.FirstOrDefault(candidate => candidate.Kind == job.Job.Engine);
        if (engine is not { IsRunnable: true })
        {
            return JobOutcome.Failed($"這台主機上的 {EngineKinds.DisplayName(job.Job.Engine)} 現在不能用。");
        }

        if (await LoginChangedAsync(job.Job.Engine, state, cancellation) is { } changed)
        {
            _reprobe = true;
            return JobOutcome.Failed(changed);
        }

        // 每件工作各建一個：MCP 權杖是這件工作的工具授權，不是機器憑證。
        var runner = new ProcessEngine(engine.Spec!, engine.Executable!, engine.Version ?? "unknown", engine.Models, job.ToolToken, state.Server!, options);
        JobOutcome outcome;
        try
        {
            outcome = await runner.RunAsync(job.Job, reporter, cancellation, new HostAttachmentSource(api, job.Lease));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 只說型別：例外的訊息可能帶著引擎的輸出或路徑。
            return JobOutcome.Failed($"主機代理執行時出了預期之外的錯誤（{exception.GetType().Name}）。");
        }

        // 被取消（使用者按「停止」、伺服器逾時、租約不對、SIGTERM）的工作一律以「中斷」結束，引擎被打斷時吐的東西不送。
        return cancellation.IsCancellationRequested ? JobOutcome.Stopped(_shuttingDown ? "主機代理被關掉了。" : null) : outcome;
    }

    /// <summary>
    /// 跑之前再確認一次登入（只跑狀態指令、不送模型請求）：兩次完整探測之間有人在 Kumi 以外換成訂閱帳號，
    /// 不能讓這件工作用到它。沒有問題回 null。
    /// </summary>
    private async Task<string?> LoginChangedAsync(string kind, HostState state, CancellationToken cancellation)
    {
        var name = EngineKinds.DisplayName(kind);
        var now = await probe.CheckLoginAsync(kind, cancellation);
        if (now is not { LoggedIn: true })
        {
            return $"主機上的 {name} 沒有登入了，這則沒有執行；需要組織擁有者重新授權。";
        }

        if (now.Billing != Billing.Api)
        {
            return $"主機上的 {name} 現在登入的不是 API 用量計費的帳號，這則沒有執行；需要組織擁有者重新授權。";
        }

        var authorized = state.Logins.TryGetValue(kind, out var login) ? login.Fingerprint : null;
        return authorized is not null && now.Fingerprint is not null && authorized != now.Fingerprint
            ? $"主機上的 {name} 換了登入的帳號，這則沒有執行；需要組織擁有者重新授權。"
            : null;
    }

    private async Task FinishAsync(HostJob job, ApiClient api, HostJobReporter reporter, JobOutcome outcome, CancellationToken cancellation)
    {
        try
        {
            await reporter.FlushRemainingAsync(cancellation);
            await PostFinishAsync(job, api, outcome, cancellation);
        }
        catch (OperationCanceledException)
        {
            console.Notice("送出結果超過時限，伺服器會當作結果不明。");
        }
    }

    private async Task PostFinishAsync(HostJob job, ApiClient api, JobOutcome outcome, CancellationToken cancellation)
    {
        var body = outcome.ToFinishJson();
        body["lease"] = job.Lease;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await api.PostAsync($"host-agent/jobs/{job.Job.JobId}/finish", body, cancellation);
                return;
            }
            catch (CliException exception) when (IsTransient(exception) && attempt < 3)
            {
                await WaitAsync(Backoff(attempt), cancellation);
            }
            catch (CliException exception)
            {
                // 404、409：伺服器已經結束這件工作了（逾時、取消），沒事。其他：送不出去也只能說一聲。
                if (exception.HttpStatus is not (404 or 409))
                {
                    console.Notice($"沒辦法送出工作的結果：{exception.Message}");
                }

                return;
            }
        }
    }

    /// <summary>沒領到工作時的等待：滿了就等一件結束（或該登記了），長輪詢很快就回來就補足最短的間隔。</summary>
    private async Task IdleAsync(int capacity, List<RunningJob> running, long started, CancellationToken cancellation)
    {
        if (capacity == 0)
        {
            await Task.WhenAny([.. running.Select(job => job.Task), WaitAsync(options.BusyCheckInInterval, cancellation)]);
        }
        else if (Stopwatch.GetElapsedTime(started) < options.MinPollGap)
        {
            await WaitAsync(options.MinPollGap - Stopwatch.GetElapsedTime(started), cancellation);
        }
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

    private static bool IsTransient(CliException exception) => exception.HttpStatus is null or >= 500 or 429;

    private TimeSpan Backoff(int failures)
    {
        var wait = TimeSpan.FromTicks(hostOptions.MinBackoff.Ticks * (1L << Math.Min(failures - 1, 10)));
        return wait > hostOptions.MaxBackoff ? hostOptions.MaxBackoff : wait;
    }

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

    private static bool LooksLikeLoginProblem(string? error) =>
        error is not null && LoginProblem.IsMatch(error);

    private static readonly System.Text.RegularExpressions.Regex LoginProblem = new(
        @"log ?in|logged in|authenticat|unauthori[sz]ed|\b401\b|api key|登入|認證|憑證",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(200));
}

/// <summary>check-in 回的一件工作：本機代理那一套欄位，加上租約與這件工作的工具授權。</summary>
internal sealed record HostJob(AgentJob Job, int Lease, string ToolToken)
{
    public static HostJob? Parse(JsonElement element)
    {
        if (AgentJob.Parse(element) is not { } job
            || element.Number("lease") is not { } lease
            || element.Text("toolToken") is not { Length: > 0 } token)
        {
            return null;
        }

        return new HostJob(job, (int)Math.Clamp(lease, 0, int.MaxValue), token);
    }

    public override string ToString() => $"HostJob {{ JobId = {Job.JobId}, ToolToken = *** }}";
}
