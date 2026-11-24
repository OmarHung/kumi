using System.Net;
using PmTool.Cli.Settings;

namespace PmTool.Cli.Tests;

/// <summary>設定檔的權限、環境變數的優先順序，以及權杖只給當初登入的伺服器。</summary>
[TestFixture]
public sealed class ConfigAndCredentialsTests
{
    [Test]
    public void Save_OnUnix_CreatesTheFileReadableOnlyByTheOwnerInAPrivateDirectory()
    {
        // Arrange
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Windows 沒有 Unix 的檔案權限。");
            return;
        }

        using var cli = new CliHarness();
        var store = new ConfigStore(cli.ConfigPath);

        // Act
        store.Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = CliHarness.Token });
        store.Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = "pmt_second" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.GetUnixFileMode(cli.ConfigPath), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            Assert.That(
                File.GetUnixFileMode(Path.GetDirectoryName(cli.ConfigPath)!),
                Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));
            Assert.That(store.Load().Token, Is.EqualTo("pmt_second"));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(cli.ConfigPath)!), Has.Length.EqualTo(1), "暫存檔不留下");
        }
    }

    [Test]
    public void Load_NoFile_IsEmpty()
    {
        // Arrange
        using var cli = new CliHarness();

        // Act
        var loaded = new ConfigStore(cli.ConfigPath).Load();

        // Assert
        Assert.That((loaded.Server, loaded.Token), Is.EqualTo(((string?)null, (string?)null)));
    }

    [Test]
    public async Task Load_CorruptFile_ExplainsHowToRecoverWithoutPrintingItsContent()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        Directory.CreateDirectory(Path.GetDirectoryName(cli.ConfigPath)!);
        await File.WriteAllTextAsync(cli.ConfigPath, "{ pmt_secret_text");

        // Act
        var result = await cli.RunAsync("auth", "logout");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("設定檔讀不懂").And.Contain("kumi auth login"));
        Assert.That(result.Error, Does.Not.Contain("pmt_secret_text"));
    }

    [Test]
    public void Save_DirectoryCannotBeCreated_FailsWithAMessage()
    {
        // Arrange：資料夾的位置被一個檔案擋住。
        using var cli = new CliHarness();
        var blocker = cli.PathOf("blocker");
        File.WriteAllText(blocker, "x");
        var store = new ConfigStore(Path.Combine(blocker, "pm-tool", "config.json"));

        // Act
        var failure = Assert.Throws<CliException>(() => store.Save(new StoredConfig { Token = "pmt_x" }));

        // Assert
        Assert.That(failure!.ExitCode, Is.EqualTo(ExitCodes.Error));
    }

    [Test]
    public void DefaultPath_UsesXdgConfigHomeWhenSetAndTheHomeConfigFolderOtherwise()
    {
        // Act
        var withXdg = ConfigStore.DefaultPath(name => name == "XDG_CONFIG_HOME" ? "/custom/config" : null);
        var without = ConfigStore.DefaultPath(_ => null);

        // Assert
        Assert.That(withXdg, Is.EqualTo(Path.Combine("/custom/config", "kumi", "config.json")));
        Assert.That(without, Does.EndWith(Path.Combine(".config", "kumi", "config.json")));
    }

    [Test]
    public async Task Commands_EnvironmentVariablesWinOverTheConfigFile()
    {
        // Arrange
        using var cli = new CliHarness();
        new ConfigStore(cli.ConfigPath).Save(new StoredConfig { Server = "https://stored.example.com", Token = "pmt_stored" });
        cli.Environment["KUMI_SERVER"] = CliHarness.ServerUrl;
        cli.Environment["KUMI_TOKEN"] = CliHarness.Token;
        cli.Server.On("GET", "/v1/projects", Array.Empty<object>());

        // Act
        var result = await cli.RunAsync("project", "list");

        // Assert
        var request = cli.Server.Requests.Single();
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That((request.Uri.Host, request.Authorization), Is.EqualTo(("pm.example.com", $"Bearer {CliHarness.Token}")));
    }

    [Test]
    public async Task Commands_ServerOptionWinsOverTheEnvironmentVariable()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.On("GET", "/v1/projects", Array.Empty<object>());

        // Act
        await cli.RunAsync("project", "list", "--server", "https://other.example.com/");

        // Assert
        Assert.That(cli.Server.Requests.Single().Uri.Host, Is.EqualTo("other.example.com"));
    }

    [Test]
    public async Task Commands_StoredTokenIsNeverSentToADifferentServer()
    {
        // Arrange
        using var cli = new CliHarness();
        new ConfigStore(cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = CliHarness.Token });

        // Act
        var result = await cli.RunAsync("project", "list", "--server", "https://evil.example.org");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(cli.Server.Requests, Is.Empty);
        Assert.That(result.Error, Does.Contain("不會送給").And.Contain("https://evil.example.org"));
    }

    [Test]
    public async Task Commands_StoredTokenIsUsedForTheSameServerEvenWithDifferentCaseAndASlash()
    {
        // Arrange
        using var cli = new CliHarness();
        new ConfigStore(cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = CliHarness.Token });
        cli.Server.On("GET", "/v1/projects", Array.Empty<object>());

        // Act
        var result = await cli.RunAsync("project", "list", "--server", "HTTPS://PM.example.com/");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(cli.Server.Requests.Single().Authorization, Is.EqualTo($"Bearer {CliHarness.Token}"));
    }

    [Test]
    public async Task Commands_EnvironmentTokenCanBeUsedWithAnyServer()
    {
        // Arrange
        using var cli = new CliHarness();
        cli.Environment["KUMI_TOKEN"] = CliHarness.Token;
        cli.Server.On("GET", "/v1/projects", Array.Empty<object>());

        // Act
        var result = await cli.RunAsync("project", "list", "--server", "https://ci.example.com");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
    }

    [Test]
    public async Task Commands_NoServerAtAll_AsksToLogInOrSetTheEnvironment()
    {
        // Arrange
        using var cli = new CliHarness();

        // Act
        var result = await cli.RunAsync("project", "list");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("kumi auth login").And.Contain("KUMI_SERVER"));
    }

    [Test]
    public async Task Commands_ServerWithoutAToken_AsksToLogIn()
    {
        // Arrange
        using var cli = new CliHarness();
        cli.Environment["KUMI_SERVER"] = CliHarness.ServerUrl;

        // Act
        var result = await cli.RunAsync("project", "list");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("還沒登入").And.Contain("KUMI_TOKEN"));
    }

    [TestCase("ftp://pm.example.com")]
    [TestCase("pm.example.com")]
    [TestCase("not a url")]
    public async Task Commands_ServerThatIsNotAnHttpUrl_IsAUsageError(string server)
    {
        // Arrange
        using var cli = new CliHarness();
        cli.Environment["KUMI_TOKEN"] = CliHarness.Token;

        // Act
        var result = await cli.RunAsync("project", "list", "--server", server);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
    }

    [Test]
    public async Task Commands_ServerUnreachable_ExitsWithOne()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.Failure = new HttpRequestException("Connection refused");

        // Act
        var result = await cli.RunAsync("project", "list");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("連不上伺服器").And.Contain(CliHarness.ServerUrl));
    }

    [Test]
    public async Task Commands_ServerTimesOut_ExitsWithOne()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.Failure = new TaskCanceledException("timeout");

        // Act
        var result = await cli.RunAsync("project", "list");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("沒有在時限內回應"));
    }

    [TestCase(HttpStatusCode.Unauthorized, ExitCodes.Error)]
    [TestCase(HttpStatusCode.Forbidden, ExitCodes.Error)]
    [TestCase(HttpStatusCode.NotFound, ExitCodes.Error)]
    [TestCase(HttpStatusCode.Conflict, ExitCodes.Error)]
    [TestCase(HttpStatusCode.InternalServerError, ExitCodes.Error)]
    [TestCase(HttpStatusCode.UnprocessableEntity, ExitCodes.Invalid)]
    [TestCase(HttpStatusCode.BadRequest, ExitCodes.Invalid)]
    [TestCase(HttpStatusCode.UnsupportedMediaType, ExitCodes.Invalid)]
    public async Task Commands_ErrorStatus_IsMappedToTheExitCode(HttpStatusCode status, int expected)
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.OnError("GET", "/v1/projects", status, "some.code", "伺服器說的原因。", ("title", "細項說明。"), ("", "沒有路徑的細項。"));

        // Act
        var result = await cli.RunAsync("project", "list");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(expected));
        Assert.That(result.Error, Does.Contain("title：細項說明。").And.Contain("  沒有路徑的細項。"));
        if (status != HttpStatusCode.Unauthorized)
        {
            Assert.That(result.Error, Does.Contain("錯誤：伺服器說的原因。"));
        }
    }

    [Test]
    public async Task Commands_InsufficientScope_ShowsTheServersMessageAboutTheScope()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.OnError("GET", "/v1/projects", HttpStatusCode.Forbidden, "auth.insufficient_scope", "這把權杖需要 items:read 範圍。");

        // Act
        var result = await cli.RunAsync("project", "list");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("items:read"));
    }

    [Test]
    public async Task Commands_ResponseThatIsNotAnApiEnvelope_IsReportedByStatus()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.On("GET", "/v1/projects", _ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>bad gateway</html>") });

        // Act
        var result = await cli.RunAsync("project", "list");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("HTTP 502"));
    }

    [Test]
    public void StoredConfig_ToString_DoesNotShowTheTokens()
    {
        // Arrange
        var config = new StoredConfig { Server = "https://pm.example.com", Token = "pmt_secret_cli", AgentToken = "pmt_secret_agent" };

        // Act
        var text = config.ToString();

        // Assert
        Assert.That(text, Does.Contain("pm.example.com").And.Not.Contain("pmt_secret"));
    }
}
