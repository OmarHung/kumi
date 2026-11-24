namespace PmTool.Cli.Tests;

/// <summary>kumi licenses：執行檔自己帶著授權與第三方聲明。單一執行檔下載時只有一個檔案，沒辦法另外附文件。</summary>
[TestFixture]
public sealed class LicensesCommandTests
{
    [Test]
    public async Task Licenses_PrintsTheKumiLicenseTheNoticeAndTheThirdPartyNotices()
    {
        // Arrange
        using var cli = new CliHarness();

        // Act
        var result = await cli.RunAsync("licenses");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success));
            Assert.That(result.Error, Is.Empty);
            Assert.That(result.Out, Does.Contain("# Kumi License"), "LICENSE 的第一部分");
            Assert.That(result.Out, Does.Contain("TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION"), "LICENSE 的第二部分（Apache 2.0）");
            Assert.That(result.Out, Does.Contain("This NOTICE file is provided for attribution only."), "NOTICE");
            Assert.That(result.Out, Does.Contain("THIRD-PARTY NOTICES"), "第三方聲明");
        }
    }

    [Test]
    public async Task Licenses_ListsThePackagesTheExecutableItselfUses()
    {
        // Arrange
        using var cli = new CliHarness();

        // Act
        var result = await cli.RunAsync("licenses");

        // Assert
        Assert.That(result.Out, Does.Contain("- YamlDotNet ").And.Contain("- AngleSharp ").And.Contain("- .NET Runtime "));
    }

    [Test]
    public async Task Licenses_WorksWithoutLoggingInOrReachingTheServer()
    {
        // Arrange：沒有登入、沒有設伺服器；假伺服器收到任何請求都會留下紀錄
        using var cli = new CliHarness();

        // Act
        var result = await cli.RunAsync("licenses");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success));
            Assert.That(cli.Server.Requests, Is.Empty);
        }
    }

    [Test]
    public async Task Overview_ListsTheLicensesCommand()
    {
        // Arrange
        using var cli = new CliHarness();

        // Act
        var result = await cli.RunAsync("--help");

        // Assert
        Assert.That(result.Out, Does.Contain("licenses"));
    }
}
