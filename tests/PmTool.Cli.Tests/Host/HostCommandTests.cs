using System.Net;
using System.Text.Json.Nodes;
using PmTool.Cli.Agent;
using PmTool.Cli.Host;

namespace PmTool.Cli.Tests.Host;

[TestFixture]
[Platform(Include = "Linux,MacOsX")]
public sealed class HostCommandTests
{
    private const string PairingCode = "K7QD-3MZP-W9XA";
    private const string LoginCode = "LOGN-CODE-2222";
    private static readonly Guid AgentId = Guid.Parse("0190a000-0000-7000-8000-0000000000aa");
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

    private void OnPair(HttpStatusCode status = HttpStatusCode.OK) => _cli.Server.On("POST", "/v1/host-agent/pair", _ =>
        status == HttpStatusCode.OK
            ? FakeServer.Envelope(status, new { agentId = AgentId, agentName = "主機一號", credential = HostTestKit.Credential })
            : HostTestKit.Failure(status, "ai.host_pairing_invalid", "配對碼不正確或已經過期。"));

    [Test]
    public async Task Pair_ReadsTheCodeFromStandardInputAndSavesTheStateWithoutPrintingTheCredential()
    {
        // Arrange
        OnPair();
        _cli.StandardInput = PairingCode + "\n";

        // Act
        var result = await _cli.RunAsync("host", "pair", "--server", "https://pm.example.com/", "--name", "ai-worker-1");

        // Assert
        var request = _cli.Server.To("POST", "/v1/host-agent/pair").Single();
        var state = _kit.Store.Load();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(request.Json!["code"]!.GetValue<string>(), Is.EqualTo(PairingCode));
            Assert.That(request.Json!["machineName"]!.GetValue<string>(), Is.EqualTo("ai-worker-1"));
            Assert.That(request.Json!["machineKey"]!.GetValue<string>(), Is.EqualTo(state.MachineKey));
            Assert.That(request.Json!["agentVersion"]!.GetValue<string>(), Is.Not.Empty);
            Assert.That(request.Authorization, Is.Null, "配對不用憑證");
            Assert.That(state.IsPaired, Is.True);
            Assert.That(state.Server, Is.EqualTo("https://pm.example.com"));
            Assert.That(state.Credential, Is.EqualTo(HostTestKit.Credential));
            Assert.That(state.AgentId, Is.EqualTo(AgentId));
            Assert.That(result.Out + result.Error, Does.Not.Contain(HostTestKit.Credential).And.Not.Contain(PairingCode));
            Assert.That(result.Out, Does.Contain("主機一號"));
        }
    }

    [Test]
    public async Task Pair_OnATerminal_PromptsAndReadsWithoutEcho()
    {
        // Arrange
        OnPair();
        _cli.IsInteractive = true;
        _cli.Secret = PairingCode;
        _cli.Environment["KUMI_SERVER"] = CliHarness.ServerUrl;

        // Act
        var result = await _cli.RunAsync("host", "pair");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Error, Does.Contain("請輸入配對碼"));
            Assert.That(_cli.Server.To("POST", "/v1/host-agent/pair").Single().Json!["code"]!.GetValue<string>(), Is.EqualTo(PairingCode));
        }
    }

    [Test]
    public async Task Pair_AgainKeepsTheMachineKey()
    {
        // Arrange
        OnPair();
        _cli.Environment["KUMI_SERVER"] = CliHarness.ServerUrl;
        _cli.StandardInput = PairingCode;
        await _cli.RunAsync("host", "pair");
        var first = _kit.Store.Load().MachineKey;
        _cli.StandardInput = PairingCode;

        // Act
        var result = await _cli.RunAsync("host", "pair");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(_kit.Store.Load().MachineKey, Is.EqualTo(first));
            Assert.That(_cli.Server.To("POST", "/v1/host-agent/pair").Select(request => request.Json!["machineKey"]!.GetValue<string>()), Is.All.EqualTo(first));
        }
    }

    [Test]
    public async Task Pair_WhenTheServerRejectsTheCode_ExplainsAndStoresNoCredential()
    {
        // Arrange
        OnPair(HttpStatusCode.UnprocessableEntity);
        _cli.Environment["KUMI_SERVER"] = CliHarness.ServerUrl;
        _cli.StandardInput = PairingCode;

        // Act
        var result = await _cli.RunAsync("host", "pair");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(result.Error, Does.Contain("配對碼不正確"));
            Assert.That(_kit.Store.Load().IsPaired, Is.False);
        }
    }

    [Test]
    public async Task Pair_TheCodeCanNotBeGivenOnTheCommandLine()
    {
        // Act
        var result = await _cli.RunAsync("host", "pair", PairingCode, "--server", CliHarness.ServerUrl);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(_cli.Server.Requests, Is.Empty);
        }
    }

    [Test]
    public async Task Pair_WithNoCode_IsAUsageError()
    {
        // Arrange
        _cli.Environment["KUMI_SERVER"] = CliHarness.ServerUrl;
        _cli.StandardInput = string.Empty;

        // Act
        var result = await _cli.RunAsync("host", "pair");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
    }

    [Test]
    public async Task Pair_OverPlainHttpToARemoteServer_NeedsAnExplicitAllowance()
    {
        // Arrange
        OnPair();
        _cli.StandardInput = PairingCode;

        // Act
        var refused = await _cli.RunAsync("host", "pair", "--server", "http://api.internal:8080");
        _cli.Environment["KUMI_ALLOW_INSECURE"] = "1";
        _cli.StandardInput = PairingCode;
        var allowed = await _cli.RunAsync("host", "pair", "--server", "http://api.internal:8080");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(refused.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(allowed.ExitCode, Is.EqualTo(ExitCodes.Success), allowed.Error);
            Assert.That(_kit.Store.Load().Server, Is.EqualTo("http://api.internal:8080"));
        }
    }

    [Test]
    public async Task Login_ClaimsRunsTheCliLoginReportsAndStoresTheLoginId()
    {
        // Arrange
        _kit.InstallFakeClaude(HostTestKit.LoggedOutAuth, afterLogin: HostTestKit.ApiAuth);
        _kit.Pair();
        _cli.Server.On("POST", "/v1/host-agent/logins/claim", new { loginId = LoginId, engine = "claude_code", expiresAt = DateTimeOffset.UtcNow.AddMinutes(10) });
        _cli.Server.On("POST", $"/v1/host-agent/logins/{LoginId}/complete", new { loginId = LoginId, engine = "claude_code" });
        _cli.StandardInput = LoginCode;

        // Act
        var result = await _cli.RunAsync("host", "login", "claude-code");

        // Assert
        var report = _cli.Server.To("POST", $"/v1/host-agent/logins/{LoginId}/complete").Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(_cli.Server.To("POST", "/v1/host-agent/logins/claim").Single().Json!["code"]!.GetValue<string>(), Is.EqualTo(LoginCode));
            Assert.That(report.Authorization, Is.EqualTo($"Bearer {HostTestKit.Credential}"));
            Assert.That(report.Json!["loggedIn"]!.GetValue<bool>(), Is.True);
            Assert.That(report.Json!["billing"]!.GetValue<string>(), Is.EqualTo("api"));
            Assert.That(_kit.Scripts.Read("login-args"), Does.Contain("--console"));
            Assert.That(_kit.Store.Load().Logins["claude_code"].LoginId, Is.EqualTo(LoginId));
            Assert.That(report.Body, Does.Not.Contain("someone@example.com").And.Not.Contain("Example Org"), "帳號資料不送出");
            Assert.That(result.Out + result.Error, Does.Not.Contain(LoginCode).And.Not.Contain(HostTestKit.Credential));
        }
    }

    [Test]
    public async Task Login_WhenTheServerRejectsTheLogin_LogsOutTheCliClearsTheLoginAndExitsWithOne()
    {
        // Arrange
        _kit.InstallFakeClaude(HostTestKit.LoggedOutAuth, afterLogin: HostTestKit.SubscriptionAuth);
        _kit.Pair(loginId: Guid.NewGuid());
        _cli.Server.On("POST", "/v1/host-agent/logins/claim", new { loginId = LoginId, engine = "claude_code", expiresAt = DateTimeOffset.UtcNow.AddMinutes(10) });
        _cli.Server.On("POST", $"/v1/host-agent/logins/{LoginId}/complete", _ =>
            HostTestKit.Failure(HttpStatusCode.UnprocessableEntity, "ai.host_login_rejected", "訂閱帳號不能給全組織共用。"));
        _cli.StandardInput = LoginCode;

        // Act
        var result = await _cli.RunAsync("host", "login", "claude-code");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("訂閱帳號不能給全組織共用"));
            Assert.That(_cli.Server.To("POST", $"/v1/host-agent/logins/{LoginId}/complete").Single().Json!["billing"]!.GetValue<string>(), Is.EqualTo("subscription"));
            Assert.That(_kit.Scripts.Exists("logout-called"), Is.True, "被拒絕就把 CLI 登出");
            Assert.That(_kit.Store.Load().Logins, Is.Empty);
        }
    }

    [Test]
    public async Task Login_ForTheOtherEngineThanTheCode_ReportsFailureAndDoesNotRunTheCliLogin()
    {
        // Arrange
        _kit.InstallFakeClaude(HostTestKit.LoggedOutAuth);
        _kit.Pair();
        _cli.Server.On("POST", "/v1/host-agent/logins/claim", new { loginId = LoginId, engine = "codex", expiresAt = DateTimeOffset.UtcNow.AddMinutes(10) });
        _cli.Server.On("POST", $"/v1/host-agent/logins/{LoginId}/complete", new { loginId = LoginId, engine = "codex" });
        _cli.StandardInput = LoginCode;

        // Act
        var result = await _cli.RunAsync("host", "login", "claude-code");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(_kit.Scripts.Exists("login-args"), Is.False);
            Assert.That(_cli.Server.To("POST", $"/v1/host-agent/logins/{LoginId}/complete").Single().Json!["loggedIn"]!.GetValue<bool>(), Is.False);
        }
    }

    [Test]
    public async Task Login_WhenNotPaired_SaysToPairFirst()
    {
        // Arrange
        _cli.StandardInput = LoginCode;

        // Act
        var result = await _cli.RunAsync("host", "login", "codex");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("kumi host pair"));
        }
    }

    [Test]
    public async Task Login_WithAnUnknownEngine_IsAUsageError()
    {
        // Act
        var result = await _cli.RunAsync("host", "login", "gemini");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
    }

    [Test]
    public async Task Status_ShowsBillingAndAuthorizationButNeverTheCredentialOrAccountText()
    {
        // Arrange
        _kit.InstallFakeClaude();
        _kit.InstallFakeCodex(statusText: "Logged in using ChatGPT");
        _kit.Pair(loginId: LoginId);

        // Act
        var text = await _cli.RunAsync("host", "status");
        var json = await _cli.RunAsync("host", "status", "--json");

        // Assert
        var data = JsonNode.Parse(json.Out)!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(text.Out, Does.Contain("主機一號").And.Contain("API 用量計費").And.Contain("Kumi 已授權").And.Contain("訂閱"));
            Assert.That(text.Out + json.Out + text.Error, Does.Not.Contain(HostTestKit.Credential).And.Not.Contain("someone@example.com").And.Not.Contain("Example Org"));
            Assert.That(data["paired"]!.GetValue<bool>(), Is.True);
            Assert.That(data["engines"]![0]!["billing"]!.GetValue<string>(), Is.EqualTo("api"));
            Assert.That(data["engines"]![0]!["authorized"]!.GetValue<bool>(), Is.True);
            Assert.That(data["engines"]![1]!["billing"]!.GetValue<string>(), Is.EqualTo("subscription"));
            Assert.That(data["engines"]![1]!["authorized"]!.GetValue<bool>(), Is.False);
            Assert.That(_cli.Server.Requests, Is.Empty, "status 不連伺服器");
        }
    }

    [Test]
    public async Task Logout_LogsOutTheCliAndClearsTheStoredLogin()
    {
        // Arrange
        _kit.InstallFakeClaude();
        _kit.Pair(loginId: LoginId);

        // Act
        var result = await _cli.RunAsync("host", "logout", "claude-code");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(_kit.Scripts.Exists("logout-called"), Is.True);
            Assert.That(_kit.Store.Load().Logins, Is.Empty);
            Assert.That(_kit.Store.Load().IsPaired, Is.True, "憑證不受影響");
        }
    }

    [Test]
    public async Task Logout_WhenTheCliIsMissing_StillClearsTheKumiAuthorizationAndSaysSo()
    {
        // Arrange
        _kit.Pair(loginId: LoginId);

        // Act
        var result = await _cli.RunAsync("host", "logout", "claude-code");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(_kit.Store.Load().Logins, Is.Empty);
        }
    }

    [Test]
    public async Task Help_ListsTheHostGroup()
    {
        // Act
        var result = await _cli.RunAsync("--help");

        // Assert
        Assert.That(result.Out, Does.Contain("host pair").And.Contain("host run").And.Contain("host login"));
    }

    [Test]
    public void ReadLineFromStream_StopsAtTheFirstNewlineAndLeavesTheRestForTheChildProcess()
    {
        // Arrange
        using var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("K7QD-3MZP-W9XA\r\nsk-example-key\n"));

        // Act
        var code = PmTool.Cli.Commands.HostCommands.ReadLineFromStream(input);
        var rest = new StreamReader(input).ReadToEnd();

        // Assert
        Assert.That(code, Is.EqualTo("K7QD-3MZP-W9XA"));
        Assert.That(rest, Is.EqualTo("sk-example-key\n"));
    }
}
