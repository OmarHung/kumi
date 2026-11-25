namespace PmTool.Knowledge.Tests.Pages;

/// <summary>頁面內文裡有私鑰或已知格式的存取權杖就不存（只是防呆）。</summary>
[TestFixture]
public sealed class PageBodySecretTests
{
    private KnowledgeTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await KnowledgeTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    private static string Token() => "pmt_" + new string('a', 43);

    [Test]
    public async Task CreateAsync_BodyWithAnAccessToken_IsRefused()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");

        // Act
        var created = await _host.Pages.CreateAsync(org.Admin, "HR", new NewPage(null, "標題", $"token: {Token()}"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageBodySecret));
            Assert.That(created.Failure?.Message, Does.Contain("權杖"));
        }
    }

    [Test]
    public async Task UpdateAsync_BodyWithAPrivateKeyHeaderWithExtraSpaces_IsRefused()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "標題");

        // Act
        var updated = await _host.Pages.UpdateAsync(
            org.Admin, "HR", page.Number, new PageChanges(null, "-----BEGIN  RSA   PRIVATE KEY-----", page.Revision), CancellationToken.None);

        // Assert
        Assert.That(updated.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageBodySecret));
    }
}
