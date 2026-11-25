using PmTool.Abstractions;

namespace PmTool.Work.Tests.Projects;

/// <summary>專案說明不收私鑰與已知格式的存取權杖；只談到名稱、公開的金鑰與一般文字照常可以存。</summary>
[TestFixture]
public sealed class ProjectDescriptionSecretTests
{
    private const string KumiToken = "pmt_" + "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdE";
    private const string GitHubToken = "ghp_" + "abcdefghijklmnopqrstuvwxyz0123456789";
    // 雲端金鑰的形狀，執行時才組起來：原始碼裡不放長得像真金鑰的字串。
    private const string CloudKey = "AKIA" + "ABCDEFGHIJKLMNOP";

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [TestCase("-----BEGIN\tOPENSSH  PRIVATE KEY-----")]
    [TestCase("權杖 " + KumiToken)]
    [TestCase("token=" + GitHubToken)]
    [TestCase(CloudKey)]
    public async Task SetAsync_PrivateKeyOrAccessToken_FailsWithSecretAndTheNewMessage(string line)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Descriptions.SetAsync(scenario.Admin, "WEB", $"## 主機\n{line}\n", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((result.Failure?.Kind, result.Failure?.Code), Is.EqualTo((FailureKind.Invalid, WorkErrorCodes.ProjectDescriptionSecret)));
            Assert.That(result.Failure!.Details.Single().Message, Does.Contain("私鑰或存取權杖"));
        }
    }

    [Test]
    public async Task SetAsync_TalkAboutTokensWithoutOne_IsAllowed()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Descriptions.SetAsync(scenario.Admin, "WEB", "存取權杖（pmt_ 開頭）放在密碼管理器的「web 部署」項目", CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
    }
}
