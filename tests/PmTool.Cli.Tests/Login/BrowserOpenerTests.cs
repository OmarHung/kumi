using PmTool.Cli.Login;
using PmTool.Cli.Processes;

namespace PmTool.Cli.Tests.Login;

[TestFixture]
internal sealed class BrowserOpenerTests
{
    private static string? Find(string name) => name switch
    {
        "rundll32" => @"C:\Windows\System32\rundll32.exe",
        "open" => "/usr/bin/open",
        "xdg-open" => "/usr/bin/xdg-open",
        _ => null,
    };

    [TestCase("file:///etc/passwd")]
    [TestCase(@"\\server\share\x.exe")]
    [TestCase("-o-something")]
    [TestCase("javascript:alert(1)")]
    [TestCase("ftp://example.com/x")]
    [TestCase("")]
    public void BrowserStartInfo_ForAUrlThatIsNotHttpOrHttps_StartsNothingOnEveryPlatform(string url)
    {
        foreach (var platform in Enum.GetValues<SystemPlatform>())
        {
            Assert.That(LoginEnvironment.BrowserStartInfo(url, platform, Find), Is.Null, platform.ToString());
        }
    }

    [Test]
    public void BrowserStartInfo_OnWindows_UsesRundll32WithTheUrlAsItsOwnArgumentWithoutAShell()
    {
        var info = LoginEnvironment.BrowserStartInfo("https://pm.example.com/cli/authorize?x=1", SystemPlatform.Windows, Find);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(info, Is.Not.Null);
            Assert.That(info!.FileName, Is.EqualTo(@"C:\Windows\System32\rundll32.exe"));
            Assert.That(info.UseShellExecute, Is.False);
            Assert.That(info.ArgumentList, Is.EqualTo(new[] { "url.dll,FileProtocolHandler", "https://pm.example.com/cli/authorize?x=1" }));
        }
    }

    [TestCase(SystemPlatform.MacOS, "/usr/bin/open")]
    [TestCase(SystemPlatform.Linux, "/usr/bin/xdg-open")]
    public void BrowserStartInfo_OnUnix_UsesTheSystemOpenerWithTheUrlAsTheOnlyArgument(SystemPlatform platform, string opener)
    {
        var info = LoginEnvironment.BrowserStartInfo("http://127.0.0.1:1/x", platform, Find);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(info!.FileName, Is.EqualTo(opener));
            Assert.That(info.ArgumentList, Is.EqualTo(new[] { "http://127.0.0.1:1/x" }));
        }
    }

    [Test]
    public void BrowserStartInfo_WhenTheOpenerIsMissing_ReturnsNull()
    {
        Assert.That(LoginEnvironment.BrowserStartInfo("https://pm.example.com/", SystemPlatform.Linux, _ => null), Is.Null);
    }
}
