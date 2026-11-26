namespace PmTool.Cli.Agent;

/// <summary>
/// 用一個外部的命令列程式（claude、codex）跑工作：每件一個空的暫存資料夾、子程序環境拿掉會讓它改用 API 計費的變數、
/// 提示從標準輸入送、事件交給轉換器。引擎的失敗一律轉成結果，不丟例外。
/// </summary>
internal sealed class ProcessEngine(
    IEngineSpec spec,
    string executable,
    string version,
    IReadOnlyList<EngineModel> models,
    string mcpToken,
    string server,
    AgentOptions options,
    string? detail = null) : IAgentEngine
{
    /// <summary>
    /// 拿掉的環境變數：要用的是使用者自己的訂閱登入，不是 API 金鑰；也不讓引擎看到 kumi 自己的權杖變數。
    /// 加上的 KUMI_MCP_TOKEN 只給 MCP 的設定引用。
    /// </summary>
    internal static readonly IReadOnlyList<string> RemovedVariables =
    [
        "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "ANTHROPIC_BASE_URL",
        "CLAUDE_CODE_USE_BEDROCK", "CLAUDE_CODE_USE_VERTEX", "CLAUDE_CODE_USE_FOUNDRY",
        "OPENAI_API_KEY", "CODEX_API_KEY", "CODEX_ACCESS_TOKEN", "OPENAI_BASE_URL",
        "ANTHROPIC_FOUNDRY_API_KEY", "AWS_BEARER_TOKEN_BEDROCK", "ANTHROPIC_BEDROCK_BASE_URL", "ANTHROPIC_VERTEX_BASE_URL", "ANTHROPIC_CUSTOM_HEADERS",
        "KUMI_TOKEN", "PM_TOKEN", ClaudeCodeSpec.TokenVariable,
    ];

    private readonly ProcessRunner _runner = new(options);

    public string Kind => spec.Kind;

    public string Version => version;

    public IReadOnlyList<EngineModel> Models => models;

    public string? Detail => detail;

    public async Task<JobOutcome> RunAsync(AgentJob job, IEventSink sink, CancellationToken cancellation, IAttachmentSource? attachments = null)
    {
        if (JobValidation.Check(job, server) is { } problem)
        {
            return JobOutcome.Failed($"伺服器送來的工作內容不正確（{problem}），已經略過。");
        }

        // 寫進設定的是檢查過、正規化的網址，不是伺服器送來的原文。
        job = job with { McpUrl = JobValidation.NormalizeMcpUrl(job.McpUrl, server)! };

        var name = EngineKinds.DisplayName(spec.Kind);
        var workDirectory = Directory.CreateTempSubdirectory("kumi-agent-");
        try
        {
            var staged = await AttachmentStaging.StageAsync(job, workDirectory.FullName, attachments, cancellation);
            var plan = spec.Plan(job, workDirectory.FullName, staged);
            foreach (var (file, content) in plan.Files)
            {
                await File.WriteAllTextAsync(Path.Combine(workDirectory.FullName, file), content, cancellation);
            }

            return await RunProcessAsync(plan, workDirectory.FullName, name, sink, cancellation);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return JobOutcome.Stopped();
        }
        catch (AttachmentStagingException exception)
        {
            return JobOutcome.Failed(ErrorText.Sanitize(exception.Message, mcpToken));
        }
        catch (IOException)
        {
            return JobOutcome.Failed("寫不進暫存資料夾，沒辦法啟動引擎。");
        }
        finally
        {
            DeleteQuietly(workDirectory);
        }
    }

    private async Task<JobOutcome> RunProcessAsync(LaunchPlan plan, string workDirectory, string name, IEventSink sink, CancellationToken cancellation)
    {
        var translator = spec.CreateTranslator();
        var scrubbed = new ScrubbingSink(sink, mcpToken);
        JobOutcome? outcome = null;
        var request = new ProcessRequest(
            executable, plan.Arguments, workDirectory, plan.StandardInput,
            new Dictionary<string, string> { [ClaudeCodeSpec.TokenVariable] = mcpToken }, RemovedVariables);

        var result = await _runner.RunAsync(request, line =>
        {
            TranslatorStep step;
            try
            {
                step = translator.Feed(line);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // 讀不懂的輸出不能讓工作卡到逾時：停下引擎、回報失敗（只說型別，內容可能是對話）。
                step = TranslatorStep.Stop($"本機代理沒辦法處理 {name} 的輸出（{exception.GetType().Name}），已經停下。");
            }

            foreach (var agentEvent in step.Events)
            {
                scrubbed.Emit(agentEvent);
            }

            outcome ??= step.Outcome;
            return step.Abort ? LineVerdict.Abort : step.Outcome is null ? LineVerdict.Continue : LineVerdict.Done;
        }, options.EngineTimeout, cancellation);

        if (result.StartFailed)
        {
            return JobOutcome.Failed($"沒辦法啟動 {name}，請確認它還裝在原來的位置。");
        }

        // 已經有結果（含「做了不該做的事」）就照它：之後才被取消或逾時，也不該把完成的改成中斷。
        if (outcome is not null)
        {
            return Scrub(outcome);
        }

        if (result.TimedOut)
        {
            var limit = options.EngineTimeout.TotalMinutes >= 1
                ? $"{(int)options.EngineTimeout.TotalMinutes} 分鐘"
                : $"{(int)Math.Ceiling(options.EngineTimeout.TotalSeconds)} 秒";
            return JobOutcome.Failed($"本機的 {name} 超過 {limit}沒有完成，已經停下。");
        }

        return result.Cancelled ? JobOutcome.Stopped() : Scrub(translator.Conclude(result.ExitCode, result.StandardError));
    }

    private JobOutcome Scrub(JobOutcome outcome) =>
        outcome.Error is null ? outcome : outcome with { Error = ErrorText.Sanitize(outcome.Error, mcpToken) };

    private static void DeleteQuietly(DirectoryInfo directory)
    {
        try
        {
            directory.Delete(recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 刪不掉就算了：裡面沒有機密（權杖只在環境變數裡），系統的暫存資料夾也會清。
        }
    }
}

/// <summary>送出之前再清一次錯誤文字：權杖的字面值（轉換器不知道它）與路徑都不能出現在伺服器上。</summary>
internal sealed class ScrubbingSink(IEventSink inner, string secret) : IEventSink
{
    public void Emit(AgentEvent agentEvent) =>
        inner.Emit(agentEvent is ToolEvent { Error: { } error } tool ? tool with { Error = ErrorText.Sanitize(error, secret) } : agentEvent);
}
