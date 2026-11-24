using PmTool.Cli.Settings;

namespace PmTool.Cli.Tests;

/// <summary>
/// 改名 Kumi 之後舊名稱的相容：環境變數從 PM_* 換成 KUMI_*、設定檔從 pm-tool 資料夾換成 kumi。
/// 已經在用的人（CI 設了 PM_TOKEN、本機登入過）升級之後不必改設定；新舊都有時用新的。
/// </summary>
[TestFixture]
public sealed class LegacyNameTests
{
    private const string OtherServer = "https://other.example.com";

    [Test]
    public async Task Commands_LegacyPmVariables_StillWork()
    {
        // Arrange
        using var cli = new CliHarness();
        cli.Environment["PM_SERVER"] = CliHarness.ServerUrl;
        cli.Environment["PM_TOKEN"] = CliHarness.Token;
        cli.Server.On("GET", "/v1/projects", Array.Empty<object>());

        // Act
        var result = await cli.RunAsync("project", "list");

        // Assert
        var request = cli.Server.Requests.Single();
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That((request.Uri.Host, request.Authorization), Is.EqualTo(("pm.example.com", $"Bearer {CliHarness.Token}")));
    }

    [Test]
    public async Task Commands_NewKumiVariablesWinOverTheLegacyOnes()
    {
        // Arrange
        using var cli = new CliHarness();
        cli.Environment["KUMI_SERVER"] = CliHarness.ServerUrl;
        cli.Environment["KUMI_TOKEN"] = CliHarness.Token;
        cli.Environment["PM_SERVER"] = OtherServer;
        cli.Environment["PM_TOKEN"] = "pmt_legacy_value";
        cli.Server.On("GET", "/v1/projects", Array.Empty<object>());

        // Act
        var result = await cli.RunAsync("project", "list");

        // Assert
        var request = cli.Server.Requests.Single();
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That((request.Uri.Host, request.Authorization), Is.EqualTo(("pm.example.com", $"Bearer {CliHarness.Token}")));
    }

    [Test]
    public async Task Commands_MalformedLegacyToken_NamesTheVariableItCameFrom()
    {
        // Arrange
        using var cli = new CliHarness();
        cli.Environment["KUMI_SERVER"] = CliHarness.ServerUrl;
        cli.Environment["PM_TOKEN"] = "not-a-token";

        // Act
        var result = await cli.RunAsync("project", "list");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(result.Error, Does.Contain("環境變數 PM_TOKEN").And.Not.Contain("not-a-token"));
            Assert.That(cli.Server.Requests, Is.Empty);
        }
    }

    [Test]
    public async Task RemoteHttpServer_LegacyAllowInsecureVariable_StillAllowsIt()
    {
        // Arrange
        using var cli = new CliHarness();
        cli.Environment["KUMI_SERVER"] = "http://pm.example.com";
        cli.Environment["KUMI_TOKEN"] = CliHarness.Token;
        cli.Environment["PM_ALLOW_INSECURE"] = "1";
        cli.Server.On("GET", "/v1/projects", Array.Empty<object>());

        // Act
        var result = await cli.RunAsync("project", "list");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(result.Error, Does.Contain("警告"));
    }

    [Test]
    public void DefaultPath_PrefersTheKumiFolderAndFallsBackToAnExistingPmToolFolder()
    {
        // Arrange
        var root = Path.Combine(Path.GetTempPath(), $"kumi-config-tests-{Guid.NewGuid():N}");
        string? Environment(string name) => name == "XDG_CONFIG_HOME" ? root : null;
        var kumi = Path.Combine(root, "kumi", "config.json");
        var legacy = Path.Combine(root, "pm-tool", "config.json");

        try
        {
            // Act
            var nothingYet = ConfigStore.DefaultPath(Environment);
            WriteFile(legacy);
            var onlyLegacy = ConfigStore.DefaultPath(Environment);
            WriteFile(kumi);
            var both = ConfigStore.DefaultPath(Environment);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(nothingYet, Is.EqualTo(kumi), "新安裝的用新的資料夾");
                Assert.That(onlyLegacy, Is.EqualTo(legacy), "舊版登入過的照樣讀得到，不必重新登入");
                Assert.That(both, Is.EqualTo(kumi), "新舊都有時用新的");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Help_NamesTheKumiCommandAndVariables()
    {
        // Arrange
        using var cli = new CliHarness();

        // Act
        var result = await cli.RunAsync("--help");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Out, Does.StartWith("kumi："));
            Assert.That(result.Out, Does.Contain("KUMI_TOKEN").And.Contain("KUMI_SERVER"));
            Assert.That(result.Out, Does.Not.Contain("pm-tool"));
        }
    }

    private static void WriteFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{}");
    }
}
