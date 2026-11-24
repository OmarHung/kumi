using PmTool.Abstractions;

namespace PmTool.Ai.Tests.Settings;

/// <summary>Ai:AllowPrivateEndpoints 只放寬組織的連線：個人的相容端點一律檢查位址。</summary>
[TestFixture]
public sealed class PersonalPrivateEndpointTests
{
    private const string PrivateUrl = "http://10.0.0.5:11434/v1";

    private static async Task<AiTestHost> StartAsync()
    {
        var host = await AiTestHost.StartAsync(options: new AiModuleOptions(AllowPrivateEndpoints: true));
        await host.Get<IAiSettingsService>().SaveAsync(
            AiTestData.Admin, new AiSettingsInput(false, null) { AllowPersonalConnections = true }, CancellationToken.None);
        return host;
    }

    [Test]
    public async Task Create_PersonalPrivateAddress_IsRefusedWhileTheOrganizationsIsAllowed()
    {
        // Arrange
        await using var host = await StartAsync();

        // Act
        var personal = await host.Get<IPersonalAiConnectionService>().CreateAsync(
            AiTestData.Member, AiTestData.Compatible(baseUrl: PrivateUrl), CancellationToken.None);
        var organization = await host.Get<IAiSettingsService>().CreateConnectionAsync(
            AiTestData.Admin, AiTestData.Compatible(baseUrl: PrivateUrl), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(personal.Failure?.Details.Single().Code, Is.EqualTo(AiErrorCodes.EndpointNotAllowed));
            Assert.That(personal.Failure?.Details.Single().Message, Does.Contain("請組織管理員接成組織的連線"));
            Assert.That(organization.Succeeded, Is.True);
            Assert.That(host.Gateway.DiscoverCalls.Select(c => c.Personal), Is.EqualTo(new[] { false }));
        }
    }

    [Test]
    public async Task Discover_PersonalPrivateAddress_IsRefusedAndPublicOnesAreMarkedPersonalForTheGateway()
    {
        // Arrange
        await using var host = await StartAsync();
        var personal = host.Get<IPersonalAiConnectionService>();

        // Act
        var blocked = await personal.DiscoverModelsAsync(
            AiTestData.Member, new AiDiscoveryInput(AiProviderKind.OpenaiCompatible, PrivateUrl, null, null), CancellationToken.None);
        var allowed = await personal.DiscoverModelsAsync(
            AiTestData.Member, new AiDiscoveryInput(AiProviderKind.OpenaiCompatible, "https://llm.example.com/v1", null, null), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(blocked.Failure?.Details.Single().Code, Is.EqualTo(AiErrorCodes.EndpointNotAllowed));
            Assert.That(allowed.Succeeded, Is.True);
            Assert.That(host.Gateway.DiscoverCalls.Single().Personal, Is.True);
        }
    }

    [Test]
    public async Task Update_PersonalPrivateAddress_IsRefused()
    {
        // Arrange
        await using var host = await StartAsync();
        var personal = host.Get<IPersonalAiConnectionService>();
        var mine = (await personal.CreateAsync(
            AiTestData.Member, AiTestData.Compatible(baseUrl: "https://llm.example.com/v1"), CancellationToken.None)).Value;

        // Act
        var update = await personal.UpdateAsync(
            AiTestData.Member, mine.Id, AiTestData.Compatible(baseUrl: PrivateUrl), CancellationToken.None);

        // Assert
        Assert.That(update.Failure?.Details.Single().Code, Is.EqualTo(AiErrorCodes.EndpointNotAllowed));
    }
}
