using System.Net;
using System.Text.Json.Nodes;
using PmTool.Cli.Host;

namespace PmTool.Cli.Tests.Host;

[TestFixture]
[Platform(Include = "Linux,MacOsX")]
public sealed class HostServiceTests
{
    private static readonly Guid LoginId = Guid.Parse("0190a000-0000-7000-8000-0000000000cc");

    private CliHarness _cli = null!;
    private HostTestKit _kit = null!;

    [SetUp]
    public void SetUp()
    {
        _cli = new CliHarness();
        _kit = new HostTestKit(_cli);
    }

    [TearDown]
    public void TearDown()
    {
        _kit.Dispose();
        _cli.Dispose();
    }

    private static string EventsPath(Guid jobId) => $"/v1/host-agent/jobs/{jobId}/events";

    private static string FinishPath(Guid jobId) => $"/v1/host-agent/jobs/{jobId}/finish";

    /// <summary>第一次登記給一件工作，之後都是空的。</summary>
    private void OnCheckIn(JsonObject? firstJob, Action<JsonNode>? inspect = null, Func<int, HttpResponseMessage?>? special = null)
    {
        var calls = 0;
        _cli.Server.On("POST", "/v1/host-agent/check-in", request =>
        {
            var call = Interlocked.Increment(ref calls);
            inspect?.Invoke(request.Json!);
            return special?.Invoke(call) ?? HostTestKit.CheckInResponse(call == 1 ? firstJob : null);
        });
    }

    private void OnEventsAckEverything(bool cancelled = false) => _cli.Server.OnPattern("POST", @"/v1/host-agent/jobs/[^/]+/events", request =>
        FakeServer.Envelope(HttpStatusCode.OK, new
        {
            cancelled,
            nextSeq = request.Json!["firstSeq"]!.GetValue<int>() + request.Json!["events"]!.AsArray().Count,
        }));

    private void OnFinishThenInterrupt(HttpStatusCode status = HttpStatusCode.NoContent) =>
        _cli.Server.OnPattern("POST", @"/v1/host-agent/jobs/[^/]+/finish", _ =>
        {
            _cli.Interrupt!();
            return status == HttpStatusCode.NoContent ? new HttpResponseMessage(status) : HostTestKit.Failure(status, "ai.host_job_lease_lost", "租約不對");
        });

    [Test]
    public async Task Run_WhenNotPaired_WaitsAndLogsWithoutCallingTheServerNorExiting()
    {
        // Arrange
        var waits = 0;
        _kit.OnWait = _ =>
        {
            if (Interlocked.Increment(ref waits) == 3)
            {
                _cli.Interrupt!();
            }
        };

        // Act
        var result = await _cli.RunAsync("host", "run");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(_cli.Server.Requests, Is.Empty);
            Assert.That(_kit.Waits.Take(3), Is.All.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(result.Error.Split("等待配對").Length - 1, Is.EqualTo(3));
        }
    }

    [Test]
    public async Task Run_WhenTheCredentialIsRejected_DoesNotExitLogsTheReasonAndPicksUpANewCredential()
    {
        // Arrange
        _kit.InstallFakeClaude();
        _kit.Pair();
        var calls = 0;
        OnCheckIn(null, special: _ =>
        {
            Interlocked.Increment(ref calls);
            return HostTestKit.Failure(HttpStatusCode.Unauthorized, "auth.required", "x");
        });
        var longWaits = 0;
        _kit.OnWait = wait =>
        {
            if (wait != TimeSpan.FromSeconds(30))
            {
                return;
            }

            // 第二次等待時，擁有者重新配對：換了新的憑證；新憑證試過一次就結束測試。
            if (Interlocked.Increment(ref longWaits) == 2)
            {
                _kit.Store.Update(state => state with { Credential = "kha_a_brand_new_credential" });
            }

            if (Volatile.Read(ref calls) >= 2)
            {
                _cli.Interrupt!();
            }
        };

        // Act
        var result = await _cli.RunAsync("host", "run");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Error, Does.Contain("停用").And.Contain("過期").And.Contain("重新配對"));
            Assert.That(_kit.CheckIns[0].Authorization, Is.EqualTo($"Bearer {HostTestKit.Credential}"));
            Assert.That(_kit.CheckIns[1].Authorization, Is.EqualTo("Bearer kha_a_brand_new_credential"), "換了憑證馬上用新的，不必等 5 分鐘");
            Assert.That(_cli.Server.Requests, Has.Count.EqualTo(2), "失效期間不一直打伺服器");
        }
    }

    [Test]
    public async Task Run_WhenTheServerKeepsAnswering401_RetriesWithTheSameCredentialAndDoesNotExit()
    {
        // Arrange：停用、過期、機器編號對不上，伺服器一律 401；重新啟用之後同一把憑證自己接上。
        _cli.HostOptions = new HostOptions { InvalidCredentialRetry = TimeSpan.Zero };
        _kit.InstallFakeClaude();
        _kit.Pair();
        OnCheckIn(null, special: call =>
        {
            if (call >= 4)
            {
                _cli.Interrupt!();
            }

            return call < 3 ? HostTestKit.Failure(HttpStatusCode.Unauthorized, "auth.required", "x") : HostTestKit.CheckInResponse();
        });

        // Act
        var result = await _cli.RunAsync("host", "run");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(_kit.CheckIns, Has.Count.GreaterThanOrEqualTo(4));
            Assert.That(_kit.CheckIns.Select(request => request.Authorization), Is.All.EqualTo($"Bearer {HostTestKit.Credential}"));
            Assert.That(result.Error.Split("停用").Length - 1, Is.EqualTo(2), "每次 401 印一句");
        }
    }

    [Test]
    public async Task Run_AfterAnUnauthorizedAnswer_DoesNotAskAgainBeforeTheRetryInterval()
    {
        // Arrange
        _kit.InstallFakeClaude();
        _kit.Pair();
        OnCheckIn(null, special: _ => HostTestKit.Failure(HttpStatusCode.Unauthorized, "auth.required", "x"));
        var waits = 0;
        _kit.OnWait = _ =>
        {
            if (Interlocked.Increment(ref waits) == 3)
            {
                _cli.Interrupt!();
            }
        };

        // Act
        await _cli.RunAsync("host", "run");

        // Assert
        Assert.That(new HostOptions().InvalidCredentialRetry, Is.EqualTo(TimeSpan.FromSeconds(60)));
        Assert.That(_kit.CheckIns, Has.Count.EqualTo(1), "一分鐘內不再問");
    }

    [Test]
    public async Task CheckIn_SendsTheMachineKeyAndTheComputerName()
    {
        // Arrange
        _kit.InstallFakeClaude();
        var paired = _kit.Pair();
        OnCheckIn(null, inspect: _ => _cli.Interrupt!());

        // Act
        var result = await _cli.RunAsync("host", "run");

        // Assert
        var body = _kit.CheckIns[0].Json!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(body["machineKey"]!.GetValue<string>(), Is.EqualTo(paired.MachineKey));
            Assert.That(body["machineName"]!.GetValue<string>(), Is.Not.Empty);
        }
    }

    [Test]
    public async Task Run_WhenTheServerHandsOverANewCredential_StoresItSafelyAndUsesItFromTheNextCheckIn()
    {
        // Arrange
        const string next = "kha_rotated_machine_credential_value";
        _kit.InstallFakeClaude();
        _kit.Pair();
        OnCheckIn(null, special: call =>
        {
            if (call == 2)
            {
                _cli.Interrupt!();
            }

            return HostTestKit.CheckInResponse(rotatedCredential: call == 1 ? next : null);
        });

        // Act
        var result = await _cli.RunAsync("host", "run");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(_kit.CheckIns[0].Authorization, Is.EqualTo($"Bearer {HostTestKit.Credential}"));
            Assert.That(_kit.CheckIns[1].Authorization, Is.EqualTo($"Bearer {next}"));
            Assert.That(_kit.Store.Load().Credential, Is.EqualTo(next));
            Assert.That(
                OperatingSystem.IsWindows() || File.GetUnixFileMode(_kit.Store.Path) == (UnixFileMode.UserRead | UnixFileMode.UserWrite), Is.True,
                "狀態檔只有自己讀寫");
            Assert.That(result.Error + result.Out, Does.Not.Contain(next));
            Assert.That(Directory.GetFiles(_kit.StateDirectory), Has.Length.EqualTo(1), "暫存檔不留");
        }
    }

    [Test]
    public async Task Run_WhenTheCredentialWasReplacedMeanwhile_DoesNotOverwriteItWithTheRotatedOne()
    {
        // Arrange：擁有者剛重新配對，狀態檔已經是別把憑證；舊憑證換來的新憑證不能蓋掉它。
        _kit.InstallFakeClaude();
        _kit.Pair();
        OnCheckIn(null, special: call =>
        {
            if (call == 1)
            {
                _kit.Store.Update(state => state with { Credential = "kha_freshly_paired_credential" });
                return HostTestKit.CheckInResponse(rotatedCredential: "kha_rotated_from_the_old_one");
            }

            _cli.Interrupt!();
            return HostTestKit.CheckInResponse();
        });

        // Act
        await _cli.RunAsync("host", "run");

        // Assert
        Assert.That(_kit.Store.Load().Credential, Is.EqualTo("kha_freshly_paired_credential"));
    }

    [Test]
    public async Task Run_WhenTheNetworkFails_BacksOffOneTwoFourSecondsAndKeepsGoing()
    {
        // Arrange
        _kit.InstallFakeClaude();
        _kit.Pair();
        OnCheckIn(null, special: call =>
        {
            if (call >= 4)
            {
                _cli.Interrupt!();
            }

            return HostTestKit.Failure(HttpStatusCode.BadGateway, "bad_gateway", "上游連不上");
        });

        // Act
        var result = await _cli.RunAsync("host", "run");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(_kit.Waits.Where(wait => wait != TimeSpan.FromSeconds(3) && wait != TimeSpan.FromSeconds(6)).Take(3), Is.EqualTo(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) }));
            Assert.That(_kit.CheckIns, Has.Count.GreaterThanOrEqualTo(4));
            Assert.That(result.Error, Does.Contain("秒後重試"));
        }
    }

    [Test]
    public async Task CheckIn_ReportsBothEnginesTheServerUrlTheLoginIdAndNoAccountText()
    {
        // Arrange
        _kit.InstallFakeClaude();
        _kit.Pair(loginId: LoginId);
        OnCheckIn(null, inspect: _ => _cli.Interrupt!());

        // Act
        var result = await _cli.RunAsync("host", "run");

        // Assert
        var request = _kit.CheckIns[0];
        var body = request.Json!;
        var claude = body["engines"]![0]!;
        var codex = body["engines"]![1]!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(body["serverUrl"]!.GetValue<string>(), Is.EqualTo(CliHarness.ServerUrl));
            Assert.That(body["capacity"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(body["agentVersion"]!.GetValue<string>(), Is.Not.Empty);
            Assert.That(claude["kind"]!.GetValue<string>(), Is.EqualTo("claude_code"));
            Assert.That(claude["installed"]!.GetValue<bool>(), Is.True);
            Assert.That(claude["supported"]!.GetValue<bool>(), Is.True);
            Assert.That(claude["loggedIn"]!.GetValue<bool>(), Is.True);
            Assert.That(claude["billing"]!.GetValue<string>(), Is.EqualTo("api"));
            Assert.That(claude["loginId"]!.GetValue<Guid>(), Is.EqualTo(LoginId));
            Assert.That(claude["models"]![0]!["id"]!.GetValue<string>(), Is.EqualTo("default"));
            Assert.That(codex["kind"]!.GetValue<string>(), Is.EqualTo("codex"));
            Assert.That(codex["installed"]!.GetValue<bool>(), Is.False);
            Assert.That(codex["models"]!.AsArray(), Is.Empty);
            Assert.That(request.Body, Does.Not.Contain("someone@example.com").And.Not.Contain("Example Org").And.Not.Contain(HostTestKit.Credential));
        }
    }

    [Test]
    public async Task CheckIn_ForASubscriptionLogin_ReportsSubscriptionBillingAndAProblemForTheOwner()
    {
        // Arrange
        _kit.InstallFakeClaude(HostTestKit.SubscriptionAuth);
        _kit.Pair(loginId: LoginId);
        OnCheckIn(null, inspect: _ => _cli.Interrupt!());

        // Act
        await _cli.RunAsync("host", "run");

        // Assert
        var claude = _kit.CheckIns[0].Json!["engines"]![0]!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(claude["billing"]!.GetValue<string>(), Is.EqualTo("subscription"));
            Assert.That(claude["problem"]!.GetValue<string>(), Does.Contain("訂閱").And.Contain("不能給全組織共用"));
        }
    }

    [Test]
    public async Task CheckIn_WhenTheLoginChangedOutsideKumi_DropsTheLoginIdAndClearsItFromTheStateFile()
    {
        // Arrange
        _kit.InstallFakeClaude();
        _kit.Pair(loginId: LoginId, fingerprint: "FINGERPRINT-OF-ANOTHER-ACCOUNT");
        OnCheckIn(null, inspect: _ => _cli.Interrupt!());

        // Act
        var result = await _cli.RunAsync("host", "run");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_kit.CheckIns[0].Json!["engines"]![0]!["loginId"], Is.Null, "不是透過 Kumi 完成的那一份登入，不報登入編號");
            Assert.That(_kit.Store.Load().Logins, Is.Empty);
            Assert.That(result.Error, Does.Contain("需要擁有者重新授權"));
        }
    }

    [Test]
    public async Task Job_RunsWithTheJobsToolTokenNotTheMachineCredentialAndReportsWithLeaseAndSequence()
    {
        // Arrange
        _kit.InstallFakeClaude();
        var paired = _kit.Pair(loginId: LoginId);
        var jobId = Guid.NewGuid();
        OnCheckIn(HostTestKit.HostJobWire(jobId, lease: 7));
        OnEventsAckEverything();
        OnFinishThenInterrupt();

        // Act
        var result = await _cli.RunAsync("host", "run");

        // Assert
        var events = _cli.Server.To("POST", EventsPath(jobId)).Select(request => request.Json!).ToList();
        var finish = _cli.Server.To("POST", FinishPath(jobId)).Single();
        var env = _kit.Scripts.Read("env.txt");
        var seqs = events.SelectMany(body => Enumerable.Range(body["firstSeq"]!.GetValue<int>(), body["events"]!.AsArray().Count)).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(events, Is.Not.Empty);
            Assert.That(events.Select(body => body["lease"]!.GetValue<int>()), Is.All.EqualTo(7));
            Assert.That(seqs, Is.EqualTo(Enumerable.Range(1, seqs.Count)), "流水號從 1 起連續");
            Assert.That(finish.Json!["lease"]!.GetValue<int>(), Is.EqualTo(7));
            Assert.That(finish.Json!["status"]!.GetValue<string>(), Is.EqualTo("complete"));
            Assert.That(env, Does.Contain($"KUMI_MCP_TOKEN={HostTestKit.ToolToken}"));
            Assert.That(env, Does.Not.Contain(HostTestKit.Credential).And.Not.Contain("kha_"), "機器憑證不進引擎的環境");
            Assert.That(_kit.Scripts.Read("mcp.json"), Does.Not.Contain(HostTestKit.ToolToken).And.Not.Contain(HostTestKit.Credential));
            Assert.That(_kit.Scripts.Read("mcp.json"), Does.Contain("https://pm.example.com/mcp"));
            Assert.That(
                _cli.Server.To("POST", EventsPath(jobId)).Concat(_cli.Server.To("POST", FinishPath(jobId))).Select(request => request.Authorization),
                Is.All.EqualTo($"Bearer {HostTestKit.Credential}"), "回報用機器憑證，不是工具授權");
            Assert.That(result.Out + result.Error, Does.Not.Contain(HostTestKit.ToolToken).And.Not.Contain(HostTestKit.Credential));
            Assert.That(result.Out, Does.Contain("DEMO").And.Contain("完成"));
            Assert.That(
                _cli.Server.To("POST", "/v1/host-agent/check-in").Concat(_cli.Server.To("POST", EventsPath(jobId))).Concat(_cli.Server.To("POST", FinishPath(jobId))),
                Is.Not.Empty.And.All.Matches<RecordedRequest>(request => request.Header("X-Kumi-Machine") == paired.MachineKey),
                "登記、回報、結束的每個請求都帶機器編號");
        }
    }

    [Test]
    public async Task Job_WhenTheEventsAreRejectedForALostLease_StopsTheEngineAndFinishesAsStopped()
    {
        // Arrange
        _kit.InstallFakeClaude(beforeRun: "sleep 60");
        _kit.Pair(loginId: LoginId);
        var jobId = Guid.NewGuid();
        OnCheckIn(HostTestKit.HostJobWire(jobId));
        _cli.Server.OnPattern("POST", @"/v1/host-agent/jobs/[^/]+/events", _ => HostTestKit.Failure(HttpStatusCode.Conflict, "ai.host_job_lease_lost", "租約不對"));
        // 引擎還沒印出任何東西就要有第一次回報：用心跳。
        _cli.AgentOptions = _cli.AgentOptions with { HeartbeatInterval = TimeSpan.Zero };
        OnFinishThenInterrupt(HttpStatusCode.Conflict);

        // Act
        var result = await _cli.RunAsync("host", "run");

        // Assert
        var finish = _cli.Server.To("POST", FinishPath(jobId)).Single().Json!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(finish["status"]!.GetValue<string>(), Is.EqualTo("stopped"));
            Assert.That(result.Error, Does.Not.Contain("沒辦法送出"), "409：伺服器已經結束這件工作，不用吵");
        }
    }

    [Test]
    public async Task Job_WhenTheServerCancels_StopsTheEngineAndFinishesAsStopped()
    {
        // Arrange
        _kit.InstallFakeClaude(beforeRun: "sleep 60");
        _kit.Pair(loginId: LoginId);
        var jobId = Guid.NewGuid();
        OnCheckIn(HostTestKit.HostJobWire(jobId));
        OnEventsAckEverything(cancelled: true);
        _cli.AgentOptions = _cli.AgentOptions with { HeartbeatInterval = TimeSpan.Zero };
        OnFinishThenInterrupt();

        // Act
        var result = await _cli.RunAsync("host", "run");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(_cli.Server.To("POST", FinishPath(jobId)).Single().Json!["status"]!.GetValue<string>(), Is.EqualTo("stopped"));
        }
    }

    [Test]
    public async Task Stop_WhileAJobRuns_FinishesItAsStoppedSendsTheResultAndExitsWithZero()
    {
        // Arrange
        _kit.InstallFakeClaude(beforeRun: "touch \"" + _kit.Scripts.Out + "/engine-started\"; sleep 60");
        _kit.Pair(loginId: LoginId);
        var jobId = Guid.NewGuid();
        OnCheckIn(HostTestKit.HostJobWire(jobId));
        OnEventsAckEverything();
        _cli.Server.OnPattern("POST", @"/v1/host-agent/jobs/[^/]+/finish", _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var started = Task.Run(async () =>
        {
            while (!_kit.Scripts.Exists("engine-started"))
            {
                await Task.Delay(20);
            }

            _cli.Interrupt!();
        });

        // Act
        var result = await _cli.RunAsync("host", "run");
        await started;

        // Assert
        var finish = _cli.Server.To("POST", FinishPath(jobId)).Single().Json!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(finish["status"]!.GetValue<string>(), Is.EqualTo("stopped"));
            Assert.That(finish["lease"]!.GetValue<int>(), Is.EqualTo(7));
            Assert.That(result.Out, Does.Contain("中斷"));
        }
    }

    [Test]
    public async Task Logout_FromTheServer_LogsOutTheCliClearsTheLoginIdAndReportsNotLoggedInNext()
    {
        // Arrange
        _kit.InstallFakeClaude();
        _kit.Pair(loginId: LoginId);
        var calls = 0;
        _cli.Server.On("POST", "/v1/host-agent/check-in", _ =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 3)
            {
                _cli.Interrupt!();
            }

            return HostTestKit.CheckInResponse(logout: call == 1 ? ["claude_code"] : null);
        });

        // Act
        var result = await _cli.RunAsync("host", "run");

        // Assert
        var bodies = _kit.CheckIns.Select(request => request.Json!).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(_kit.Scripts.Exists("logout-called"), Is.True);
            Assert.That(_kit.Store.Load().Logins, Is.Empty);
            Assert.That(bodies[0]["engines"]![0]!["loginId"]!.GetValue<Guid>(), Is.EqualTo(LoginId));
            Assert.That(bodies[2]["engines"]![0]!["loggedIn"]!.GetValue<bool>(), Is.False, "登出之後馬上重新探測");
            Assert.That(bodies[2]["engines"]![0]!["loginId"], Is.Null);
        }
    }

    [Test]
    public async Task CheckIn_WhenTheAgentIsDisabled_KeepsRegisteringAndNeverStartsAJob()
    {
        // Arrange
        _kit.InstallFakeClaude();
        _kit.Pair(loginId: LoginId);
        var calls = 0;
        _cli.Server.On("POST", "/v1/host-agent/check-in", _ =>
        {
            if (Interlocked.Increment(ref calls) == 3)
            {
                _cli.Interrupt!();
            }

            return HostTestKit.CheckInResponse(disabled: true);
        });

        // Act
        var result = await _cli.RunAsync("host", "run");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Error.Split("被組織擁有者停用").Length - 1, Is.EqualTo(1), "只說一次");
            Assert.That(_cli.Server.Requests, Has.Count.EqualTo(3));
        }
    }

    [Test]
    public async Task Job_ForAnEngineThatIsNotRunnable_FailsTheJobWithoutStartingAnything()
    {
        // Arrange
        _kit.InstallFakeClaude();
        _kit.Pair(loginId: LoginId);
        var jobId = Guid.NewGuid();
        OnCheckIn(HostTestKit.HostJobWire(jobId, engine: "codex"));
        OnEventsAckEverything();
        OnFinishThenInterrupt();

        // Act
        await _cli.RunAsync("host", "run");

        // Assert
        var finish = _cli.Server.To("POST", FinishPath(jobId)).Single().Json!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(finish["status"]!.GetValue<string>(), Is.EqualTo("failed"));
            Assert.That(finish["error"]!.GetValue<string>(), Does.Contain("Codex"));
            Assert.That(_kit.Scripts.Exists("env.txt"), Is.False);
        }
    }

    [Test]
    public async Task Run_AfterPairing_SaysOnceThatItStartedCheckingIn()
    {
        // Arrange
        _kit.InstallFakeClaude();
        _kit.Pair(loginId: LoginId);
        OnCheckIn(null, special: call =>
        {
            if (call == 3)
            {
                _cli.Interrupt!();
            }

            return null;
        });

        // Act
        var result = await _cli.RunAsync("host", "run");

        // Assert
        Assert.That(result.Error.Split("已配對「主機一號」").Length - 1, Is.EqualTo(1));
        Assert.That(result.Error, Does.Not.Contain(HostTestKit.Credential));
    }

    [Test]
    public async Task CheckIn_WhenTheAgentIsDisabled_WaitsTheDisabledIntervalInsteadOfPollingEverySecond()
    {
        // Arrange
        _kit.InstallFakeClaude();
        _kit.Pair(loginId: LoginId);
        var calls = 0;
        _cli.Server.On("POST", "/v1/host-agent/check-in", _ =>
        {
            if (Interlocked.Increment(ref calls) == 2)
            {
                _cli.Interrupt!();
            }

            return HostTestKit.CheckInResponse(disabled: true);
        });

        // Act
        await _cli.RunAsync("host", "run");

        // Assert
        Assert.That(_kit.Waits, Has.Member(new HostOptions().DisabledCheckIn));
    }

    [Test]
    public async Task CheckIn_WhenTheServerRedirects_ExplainsWhichSettingToChangeAndKeepsRunning()
    {
        // Arrange
        _kit.InstallFakeClaude();
        _kit.Pair(loginId: LoginId);
        var calls = 0;
        _cli.Server.On("POST", "/v1/host-agent/check-in", _ =>
        {
            if (Interlocked.Increment(ref calls) == 2)
            {
                _cli.Interrupt!();
            }

            var redirect = new HttpResponseMessage(HttpStatusCode.PermanentRedirect);
            redirect.Headers.Location = new Uri("https://pm.example.com/v1/host-agent/check-in");
            return redirect;
        });

        // Act
        var result = await _cli.RunAsync("host", "run");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(result.Error, Does.Contain("AI_WORKER_SERVER"));
        Assert.That(_kit.Waits, Has.Member(new HostOptions().RedirectRetry));
    }

    [Test]
    public async Task Job_WhenTheLoginTurnedIntoASubscriptionSinceTheLastProbe_FailsWithoutRunningTheEngine()
    {
        // Arrange
        _kit.InstallFakeClaude();
        _kit.Pair(loginId: LoginId);
        var jobId = Guid.NewGuid();

        // 登記之後、工作開始之前，有人在 Kumi 以外改用訂閱帳號登入。
        OnCheckIn(HostTestKit.HostJobWire(jobId), inspect: _ => _kit.SetAuth(HostTestKit.SubscriptionAuth));
        OnEventsAckEverything();
        OnFinishThenInterrupt();

        // Act
        await _cli.RunAsync("host", "run");

        // Assert
        var finish = _cli.Server.To("POST", FinishPath(jobId)).Single().Json!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(finish["status"]!.GetValue<string>(), Is.EqualTo("failed"));
            Assert.That(finish["error"]!.GetValue<string>(), Does.Contain("不是 API 用量計費"));
            Assert.That(_kit.Scripts.Exists("env.txt"), Is.False, "引擎沒有跑");
        }
    }

    [Test]
    public async Task Job_BeyondTheSlotsTheWorkerHas_IsFinishedAsFailedAtOnceInsteadOfDropped()
    {
        // Arrange
        _cli.HostOptions = _cli.HostOptions with { MaxConcurrentJobs = 1 };
        _kit.InstallFakeClaude(beforeRun: "sleep 3");
        _kit.Pair(loginId: LoginId);
        var running = Guid.NewGuid();
        var extra = Guid.NewGuid();
        OnCheckIn(null, special: call => call switch
        {
            1 => HostTestKit.CheckInResponse(HostTestKit.HostJobWire(running)),
            2 => HostTestKit.CheckInResponse(HostTestKit.HostJobWire(extra)),
            _ => null,
        });
        OnEventsAckEverything();
        _cli.Server.OnPattern("POST", @"/v1/host-agent/jobs/[^/]+/finish", request =>
        {
            if (request.Path.Contains(extra.ToString(), StringComparison.Ordinal))
            {
                _cli.Interrupt!();
            }

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        // Act
        await _cli.RunAsync("host", "run");

        // Assert
        var finish = _cli.Server.To("POST", FinishPath(extra)).Single().Json!;
        Assert.That(finish["status"]!.GetValue<string>(), Is.EqualTo("failed"));
        Assert.That(finish["error"]!.GetValue<string>(), Does.Contain("件數已滿"));
        Assert.That(_cli.Server.To("POST", EventsPath(extra)), Is.Empty);
    }
}
