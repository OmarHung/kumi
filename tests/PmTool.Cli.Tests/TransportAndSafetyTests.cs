using System.Net;
using PmTool.Cli.Api;
using PmTool.Cli.Output;
using PmTool.Cli.Settings;

namespace PmTool.Cli.Tests;

/// <summary>連線的安全（http、轉址、帳密、權杖的樣子）與輸出的安全（控制字元、未預期的例外）。</summary>
[TestFixture]
public sealed class TransportAndSafetyTests
{
    private const string RemoteHttp = "http://pm.example.com";

    private static CliHarness LoggedInTo(string server)
    {
        var cli = new CliHarness();
        cli.Environment["KUMI_SERVER"] = server;
        cli.Environment["KUMI_TOKEN"] = CliHarness.Token;
        return cli;
    }

    // ---- http 的伺服器 ----

    [Test]
    public async Task RemoteHttpServer_IsRefusedBeforeAnythingIsSent()
    {
        // Arrange
        using var cli = LoggedInTo(RemoteHttp);

        // Act
        var result = await cli.RunAsync("project", "list");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(result.Error, Does.Contain("不是 https").And.Contain("--insecure").And.Contain("KUMI_ALLOW_INSECURE"));
            Assert.That(cli.Server.Requests, Is.Empty, "權杖一個字也不能送出去");
        }
    }

    [TestCase("--insecure", false)]
    [TestCase("", true)]
    public async Task RemoteHttpServer_WhenAllowed_WarnsOnStandardErrorEvenWithJson(string flag, bool useEnvironment)
    {
        // Arrange
        using var cli = LoggedInTo(RemoteHttp);
        if (useEnvironment)
        {
            cli.Environment["KUMI_ALLOW_INSECURE"] = "1";
        }

        cli.Server.On("GET", "/v1/projects", Array.Empty<object>());
        string[] args = flag.Length == 0 ? ["project", "list", "--json"] : ["project", "list", "--json", flag];

        // Act
        var result = await cli.RunAsync(args);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Error, Does.Contain("警告").And.Contain("明文"));
            Assert.That(result.Out.Trim(), Is.EqualTo("[]"), "標準輸出還是乾淨的 JSON");
            Assert.That(cli.Server.Requests, Has.Count.EqualTo(1));
        }
    }

    [TestCase("http://localhost:8080")]
    [TestCase("http://127.0.0.1:8080")]
    [TestCase("http://[::1]:8080")]
    public async Task LocalHttpServer_IsFineAndDoesNotWarn(string server)
    {
        // Arrange
        using var cli = LoggedInTo(server);
        cli.Server.On("GET", "/v1/projects", Array.Empty<object>());

        // Act
        var result = await cli.RunAsync("project", "list");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(result.Error, Is.Empty);
    }

    [Test]
    public async Task Login_RemoteHttpWithoutTheFlag_DoesNotReadTheTokenOrSaveAnything()
    {
        // Arrange
        using var cli = new CliHarness { StandardInput = CliHarness.Token };

        // Act
        var result = await cli.RunAsync("auth", "login", "--server", RemoteHttp, "--with-token");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(cli.Server.Requests, Is.Empty);
        Assert.That(File.Exists(cli.ConfigPath), Is.False);
    }

    [Test]
    public async Task Login_ServerAnswersWithSomethingThatIsNotPmTool_SavesNothing()
    {
        // Arrange
        using var cli = new CliHarness { StandardInput = CliHarness.Token };
        cli.Server.On("GET", "/v1/session", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>hi</html>") });

        // Act
        var result = await cli.RunAsync("auth", "login", "--server", CliHarness.ServerUrl, "--with-token");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("不像 Kumi"));
        Assert.That(File.Exists(cli.ConfigPath), Is.False);
    }

    // ---- 網址帶帳密 ----

    [TestCase("--server")]
    [TestCase("KUMI_SERVER")]
    public async Task ServerUrlWithCredentials_IsRefusedAndTheSecretIsNotEchoed(string how)
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        const string url = "https://admin:hunter2@pm.example.com";
        string[] args = how == "--server" ? ["project", "list", "--server", url] : ["project", "list"];
        if (how == "KUMI_SERVER")
        {
            cli.Environment["KUMI_SERVER"] = url;
        }

        // Act
        var result = await cli.RunAsync(args);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("帳號與密碼").And.Not.Contain("hunter2"));
        Assert.That(cli.Server.Requests, Is.Empty);
    }

    [Test]
    public async Task MalformedServerUrlThatContainsAnAtSign_DoesNotEchoItEither()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();

        // Act
        var result = await cli.RunAsync("project", "list", "--server", "ftp://admin:hunter2@pm.example.com");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Not.Contain("hunter2"));
    }

    // ---- 轉址 ----

    [TestCase(HttpStatusCode.MovedPermanently, "https://new.example.com/v1/projects?secret=1", "https://new.example.com/v1/projects")]
    [TestCase(HttpStatusCode.Found, "/elsewhere/v1/projects", "https://pm.example.com/elsewhere/v1/projects")]
    public async Task Redirect_IsExplainedAndNotTreatedAsABadToken(HttpStatusCode status, string location, string shown)
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.On("GET", "/v1/projects", _ =>
        {
            var response = new HttpResponseMessage(status);
            response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
            return response;
        });

        // Act
        var result = await cli.RunAsync("project", "list");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain($"轉址到 {shown}").And.Contain("請改用那個網址"));
            Assert.That(result.Error, Does.Not.Contain("權杖無效").And.Not.Contain("secret=1"));
        }
    }

    [Test]
    public async Task Redirect_WithoutALocation_SaysSo()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.On("GET", "/v1/projects", _ => new HttpResponseMessage(HttpStatusCode.TemporaryRedirect));

        // Act
        var result = await cli.RunAsync("project", "list");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("沒有說轉去哪裡"));
    }

    [Test]
    public void RealHandler_DoesNotFollowRedirects() => Assert.That(HttpSettings.CreateHandler().AllowAutoRedirect, Is.False);

    // ---- 2xx 不是信封 ----

    [Test]
    public async Task SuccessWithoutAnEnvelope_IsASuccess()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.On("POST", "/v1/items/WEB-1/comments", _ => new HttpResponseMessage(HttpStatusCode.NoContent));

        // Act
        var result = await cli.RunAsync("item", "comment", "WEB-1", "-m", "好");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(result.Out, Does.Contain("已在 WEB-1 留言"));
    }

    [Test]
    public async Task ErrorStatusWithoutAnEnvelope_IsStillAnError()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.On("GET", "/v1/projects", _ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>bad</html>") });

        // Act
        var result = await cli.RunAsync("project", "list");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("HTTP 502"));
    }

    // ---- KUMI_TOKEN 的樣子 ----

    [TestCase("not-a-token")]
    [TestCase("pmt_has space")]
    public async Task TokenInTheEnvironmentThatDoesNotLookLikeOne_FailsBeforeSending(string token)
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Environment["KUMI_TOKEN"] = token;

        // Act
        var result = await cli.RunAsync("project", "list");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("KUMI_TOKEN").And.Contain("pmt_").And.Not.Contain(token));
        Assert.That(cli.Server.Requests, Is.Empty);
    }

    [Test]
    public async Task TokenInTheConfigFileThatDoesNotLookLikeOne_FailsBeforeSending()
    {
        // Arrange
        using var cli = new CliHarness();
        Directory.CreateDirectory(Path.GetDirectoryName(cli.ConfigPath)!);
        await File.WriteAllTextAsync(cli.ConfigPath, $"{{\"server\":\"{CliHarness.ServerUrl}\",\"token\":\"garbage\"}}");

        // Act
        var result = await cli.RunAsync("project", "list");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(cli.Server.Requests, Is.Empty);
    }

    // ---- 輸出的安全 ----

    [Test]
    public async Task ControlCharactersFromTheServer_AreFilteredFromTextOutputAndErrors()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.On("GET", "/v1/projects", new[]
        {
            new { key = "WEB", name = "官網\u001b[31m紅\u001b]0;pwned\u0007\u009b2J", blueprintVersion = 1, myRole = "admin" },
        });
        cli.Server.OnError("GET", "/v1/items/WEB-1", HttpStatusCode.NotFound, "item.not_found", "找不到\u001b[2J項目", ("path\u001b", "細項\u0085內容"));

        // Act
        var table = await cli.RunAsync("project", "list");
        var error = await cli.RunAsync("item", "view", "WEB-1");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Out, Does.Contain("官網[31m紅]0;pwned2J"));
            Assert.That(table.Out.Any(c => c is '\u001b' or '\u0007' or '\u009b'), Is.False);
            Assert.That(error.Error, Does.Contain("找不到[2J項目").And.Contain("細項內容"));
            Assert.That(error.Error.Any(c => c is '\u001b' or '\u0085'), Is.False);
        }
    }

    [Test]
    public async Task JsonOutput_KeepsTheRawDataEscapedByJson()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.On("GET", "/v1/projects", new[] { new { key = "WEB", name = "a\u001bb" } });

        // Act
        var result = await cli.RunAsync("project", "list", "--json");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Out, Does.Contain("a\\u001Bb"));
            Assert.That(result.Out.Contains('\u001b'), Is.False);
        }
    }

    [Test]
    public void SafeTextWriter_KeepsNewlinesAndTabsAndDropsOtherControls()
    {
        // Arrange
        var inner = new StringWriter();
        var writer = new SafeTextWriter(inner);

        // Act
        writer.Write("a\tb\r\n");
        writer.Write('\u001b');
        writer.Write('x');
        writer.Write(['\u0000', 'y', '\u007f'], 0, 3);
        writer.Write("z\u0090".AsSpan());
        writer.WriteLine("w\u001b");
        writer.WriteLineAsync('\u0001').GetAwaiter().GetResult();
        writer.WriteAsync('\u0002').GetAwaiter().GetResult();
        writer.WriteAsync("k\u0003").GetAwaiter().GetResult();
        writer.WriteAsync(['\u0004', 'm'], 0, 2).GetAwaiter().GetResult();
        writer.WriteAsync("n\u0005".AsMemory()).GetAwaiter().GetResult();
        writer.WriteLineAsync(['\u0006', 'o'], 0, 2).GetAwaiter().GetResult();
        writer.WriteLineAsync("p\u0007".AsMemory()).GetAwaiter().GetResult();
        writer.WriteLineAsync().GetAwaiter().GetResult();
        writer.Flush();
        writer.FlushAsync().GetAwaiter().GetResult();

        // Assert
        Assert.That(inner.ToString().Replace("\r\n", "\n").Replace(Environment.NewLine, "\n"), Is.EqualTo("a\tb\nxyzw\n\nkmno\np\n\n"));
        Assert.That(writer.Encoding, Is.SameAs(inner.Encoding));
    }

    [Test]
    public async Task UnexpectedException_PrintsOneShortLineWithoutTheMessageOrStack()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.Failure = new InvalidOperationException("內部細節 pmt_secret_value");

        // Act
        var result = await cli.RunAsync("project", "list");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("InvalidOperationException"));
            Assert.That(result.Error, Does.Not.Contain("pmt_secret_value").And.Not.Contain("內部細節").And.Not.Contain(" at "));
            Assert.That(result.Error.TrimEnd().Split('\n'), Has.Length.EqualTo(1));
        }
    }

    [Test]
    public async Task ErrorDetailThatRepeatsTheMainMessage_IsPrintedOnce()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.OnError(
            "POST", "/v1/items/WEB-1/transitions", HttpStatusCode.UnprocessableEntity, "item.unknown_state", "這個專案沒有「nope」這個狀態。",
            ("to", "這個專案沒有「nope」這個狀態。"), ("to", "另一個細項。"));

        // Act
        var result = await cli.RunAsync("item", "move", "WEB-1", "--to", "nope");

        // Assert
        Assert.That(result.Error.Split("這個專案沒有「nope」這個狀態。").Length - 1, Is.EqualTo(1));
        Assert.That(result.Error, Does.Contain("to：另一個細項。"));
    }

    // ---- 設定檔 ----

    [Test]
    public async Task FailedConfigWrite_LeavesNoTemporaryFileBehind()
    {
        // Arrange
        using var cli = new CliHarness { StandardInput = CliHarness.Token };
        cli.Server.On("GET", "/v1/session", new { user = new { displayName = "王", email = "w@example.com" } });
        Directory.CreateDirectory(cli.ConfigPath);

        // Act
        var result = await cli.RunAsync("auth", "login", "--server", CliHarness.ServerUrl, "--with-token");

        // Assert
        var directory = Path.GetDirectoryName(cli.ConfigPath)!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("寫不進設定檔"));
            Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
        }
    }

    [Test]
    public void Login_WithoutATerminal_HintsAtWithToken()
    {
        // Arrange
        using var cli = new CliHarness { IsInteractive = false };

        // Act
        var result = cli.RunAsync("auth", "login", "--server", CliHarness.ServerUrl).GetAwaiter().GetResult();

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("--with-token"));
    }
}
