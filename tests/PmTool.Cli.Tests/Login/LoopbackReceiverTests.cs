using System.Net.Sockets;
using System.Text;
using PmTool.Cli.Login;

namespace PmTool.Cli.Tests.Login;

[TestFixture]
public sealed class LoopbackReceiverTests
{
    private const string State = "state-value-from-the-test";

    private static async Task<string> SendRawAsync(int port, string request)
    {
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request));
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static Task<string> GetAsync(int port, string target) =>
        SendRawAsync(port, $"GET {target} HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n");

    [Test]
    public void Pkce_Challenge_MatchesTheRfc7636Example()
    {
        // Arrange
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

        // Act
        var challenge = Pkce.Challenge(verifier);

        // Assert
        Assert.That(challenge, Is.EqualTo("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM"));
    }

    [Test]
    public void Pkce_RandomToken_Is43Base64UrlCharactersAndDiffersEachTime()
    {
        // Act
        var first = Pkce.RandomToken();
        var second = Pkce.RandomToken();

        // Assert
        Assert.That(first, Has.Length.EqualTo(43).And.Matches("^[A-Za-z0-9_-]+$"));
        Assert.That(first, Is.Not.EqualTo(second));
    }

    [Test]
    public async Task Receiver_CorrectStateAndCode_ReturnsTheCodeAndAnswersWithASafePage()
    {
        // Arrange
        using var receiver = new LoopbackReceiver(State);
        receiver.Start();
        var wait = receiver.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

        // Act
        var page = await GetAsync(receiver.Port, $"/kumi-login?code=abc_DEF-123&state={State}");
        var outcome = await wait;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome, Is.EqualTo(new LoopbackOutcome(LoopbackKind.Code, "abc_DEF-123")));
            Assert.That(page, Does.StartWith("HTTP/1.1 200 OK"));
            Assert.That(page, Does.Contain("Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'"));
            Assert.That(page, Does.Contain("Referrer-Policy: no-referrer").And.Contain("Cache-Control: no-store").And.Contain("Connection: close"));
            Assert.That(page, Does.Contain("可以關掉這個分頁"));
        }
    }

    [Test]
    public async Task Receiver_AnotherPath_Gets404AndKeepsWaiting()
    {
        // Arrange
        using var receiver = new LoopbackReceiver(State);
        receiver.Start();
        var wait = receiver.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

        // Act
        var notFound = await GetAsync(receiver.Port, $"/other?code=abc&state={State}");
        var favicon = await GetAsync(receiver.Port, "/favicon.ico");
        await GetAsync(receiver.Port, $"/kumi-login?code=real&state={State}");
        var outcome = await wait;

        // Assert
        Assert.That(notFound, Does.StartWith("HTTP/1.1 404"));
        Assert.That(favicon, Does.StartWith("HTTP/1.1 404"));
        Assert.That(outcome.Code, Is.EqualTo("real"));
    }

    [TestCase("")]
    [TestCase("&state=wrong")]
    public async Task Receiver_MissingOrWrongState_ExplainsAndDoesNotAcceptTheCode(string stateQuery)
    {
        // Arrange
        using var receiver = new LoopbackReceiver(State);
        receiver.Start();
        var wait = receiver.WaitAsync(TimeSpan.FromMilliseconds(500), CancellationToken.None);

        // Act
        var page = await GetAsync(receiver.Port, $"/kumi-login?code=abc{stateQuery}");
        var outcome = await wait;

        // Assert
        Assert.That(page, Does.StartWith("HTTP/1.1 400").And.Contain("這是舊的登入頁"));
        Assert.That(outcome.Kind, Is.EqualTo(LoopbackKind.TimedOut));
    }

    [Test]
    public async Task Receiver_AccessDeniedWithTheRightState_ReturnsDenied()
    {
        // Arrange
        using var receiver = new LoopbackReceiver(State);
        receiver.Start();
        var wait = receiver.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

        // Act
        var page = await GetAsync(receiver.Port, $"/kumi-login?error=access_denied&state={State}");
        var outcome = await wait;

        // Assert
        Assert.That(outcome.Kind, Is.EqualTo(LoopbackKind.Denied));
        Assert.That(page, Does.Contain("已拒絕"));
    }

    [Test]
    public async Task Receiver_NotAGetRequest_Gets404()
    {
        // Arrange
        using var receiver = new LoopbackReceiver(State);
        receiver.Start();
        var wait = receiver.WaitAsync(TimeSpan.FromMilliseconds(500), CancellationToken.None);

        // Act
        var page = await SendRawAsync(receiver.Port, $"POST /kumi-login?code=abc&state={State} HTTP/1.1\r\nHost: x\r\n\r\n");
        var outcome = await wait;

        // Assert
        Assert.That(page, Does.StartWith("HTTP/1.1 404"));
        Assert.That(outcome.Kind, Is.EqualTo(LoopbackKind.TimedOut));
    }

    [Test]
    public async Task Receiver_AConnectionThatNeverSendsARequest_DoesNotBlockTheRealOne()
    {
        // Arrange
        using var receiver = new LoopbackReceiver(State);
        receiver.Start();
        using var stalled = new TcpClient();
        await stalled.ConnectAsync("127.0.0.1", receiver.Port);
        var wait = receiver.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

        // Act
        await GetAsync(receiver.Port, $"/kumi-login?code=real&state={State}");
        var outcome = await wait;

        // Assert
        Assert.That(outcome.Code, Is.EqualTo("real"));
    }

    [Test]
    public async Task Receiver_Cancelled_ThrowsInsteadOfReportingATimeout()
    {
        // Arrange
        using var receiver = new LoopbackReceiver(State);
        receiver.Start();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act / Assert
        Assert.ThrowsAsync<TaskCanceledException>(async () => await receiver.WaitAsync(TimeSpan.FromSeconds(30), cancel.Token));
        await Task.CompletedTask;
    }

    [Test]
    public void StoredTokens_Merge_KeepsTheTokenThatWasNotReplacedOnlyOnTheSameServer()
    {
        // Arrange
        var stored = new PmTool.Cli.Settings.StoredConfig { Server = "https://a.example.com", Token = "pmt_cli", AgentToken = "pmt_agent" };
        var agentOnly = new BrowserLoginResult("王", "w@example.com", "範例", null, "pmt_new_agent", false);

        // Act
        var same = StoredTokens.Merge(stored, "https://a.example.com/", agentOnly);
        var other = StoredTokens.Merge(stored, "https://b.example.com", agentOnly);

        // Assert
        Assert.That((same.Token, same.AgentToken), Is.EqualTo(("pmt_cli", "pmt_new_agent")));
        Assert.That((other.Token, other.AgentToken), Is.EqualTo(((string?)null, "pmt_new_agent")));
    }
}
