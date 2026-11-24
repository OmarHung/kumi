using PmTool.Abstractions;
using PmTool.Ai.Providers;

namespace PmTool.Ai.Tests.Settings;

/// <summary>能選的清單怎麼組，與選擇的檢查（組織的、自己的、別人的）。</summary>
[TestFixture]
public sealed class PersonalOptionsAndResolverTests
{
    private static readonly Actor Other = new(AiTestData.OrganizationId, Guid.CreateVersion7(), IsOrganizationAdmin: false);

    private sealed record Setup(AiTestHost Host, AiConnection Org, AiConnection Mine, AiConnection Theirs);

    private static async Task<Setup> StartAsync(bool orgEnabled = true, bool allowPersonal = true)
    {
        var host = await AiTestHost.StartAsync();
        var settings = host.Get<IAiSettingsService>();
        var personal = host.Get<IPersonalAiConnectionService>();
        var org = (await settings.CreateConnectionAsync(AiTestData.Admin, AiTestData.Anthropic("Org", enabled: ["model-a"]), CancellationToken.None)).Value;
        await settings.SaveAsync(
            AiTestData.Admin,
            new AiSettingsInput(false, null) { AllowPersonalConnections = true },
            CancellationToken.None);
        var mine = (await personal.CreateAsync(AiTestData.Member, AiTestData.Anthropic("Mine", enabled: ["model-b"]), CancellationToken.None)).Value;
        var theirs = (await personal.CreateAsync(Other, AiTestData.Anthropic("Theirs", enabled: ["model-a"]), CancellationToken.None)).Value;
        await settings.SaveAsync(
            AiTestData.Admin,
            new AiSettingsInput(orgEnabled, orgEnabled ? new AiSelection(org.Id, "model-a", null) : null) { AllowPersonalConnections = allowPersonal },
            CancellationToken.None);
        return new Setup(host, org, mine, theirs);
    }

    [Test]
    public async Task Options_OrgOnAndPersonalAllowed_ListsOrganizationFirstThenOnlyMine()
    {
        // Arrange
        var setup = await StartAsync();
        await using var _ = setup.Host;

        // Act
        var options = await setup.Host.Get<IAiOptionsService>().GetAsync(AiTestData.Member, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.Available, Is.True);
            Assert.That(options.CanAddPersonal, Is.True);
            Assert.That(options.Connections.Select(c => (c.Name, c.Personal)), Is.EqualTo(new[] { ("Org", false), ("Mine", true) }));
            Assert.That(options.Default?.ConnectionId, Is.EqualTo(setup.Org.Id));
        }
    }

    [Test]
    public async Task Options_OrgOffButPersonalAllowed_OnlyMineAndNoDefault()
    {
        // Arrange
        var setup = await StartAsync(orgEnabled: false);
        await using var _ = setup.Host;

        // Act
        var options = await setup.Host.Get<IAiOptionsService>().GetAsync(AiTestData.Member, CancellationToken.None);
        var noPersonal = await setup.Host.Get<IAiOptionsService>().GetAsync(new Actor(AiTestData.OrganizationId, Guid.CreateVersion7(), false), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.Available, Is.True);
            Assert.That(options.Connections.Select(c => c.Name), Is.EqualTo(new[] { "Mine" }));
            Assert.That(options.Default, Is.Null);
            Assert.That(noPersonal.Available, Is.False);
            Assert.That(noPersonal.CanAddPersonal, Is.True);
        }
    }

    [Test]
    public async Task Options_PersonalNotAllowed_HidesMineButKeepsTheOrganizations()
    {
        // Arrange
        var setup = await StartAsync(allowPersonal: false);
        await using var _ = setup.Host;

        // Act
        var options = await setup.Host.Get<IAiOptionsService>().GetAsync(AiTestData.Member, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.CanAddPersonal, Is.False);
            Assert.That(options.Connections.Select(c => c.Name), Is.EqualTo(new[] { "Org" }));
        }
    }

    [Test]
    public async Task Options_OrgOffAndPersonalNotAllowed_IsUnavailableAndCannotAdd()
    {
        // Arrange
        var setup = await StartAsync(orgEnabled: false, allowPersonal: false);
        await using var _ = setup.Host;

        // Act
        var options = await setup.Host.Get<IAiOptionsService>().GetAsync(AiTestData.Member, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.Available, Is.False);
            Assert.That(options.CanAddPersonal, Is.False);
            Assert.That(options.Connections, Is.Empty);
        }
    }

    [Test]
    public async Task Options_PersonalConnectionWithoutEnabledModels_IsNotListed()
    {
        // Arrange
        var setup = await StartAsync();
        await using var _ = setup.Host;
        await setup.Host.Get<IPersonalAiConnectionService>().UpdateAsync(
            AiTestData.Member, setup.Mine.Id, AiTestData.Anthropic("Mine", apiKey: null, enabled: []), CancellationToken.None);

        // Act
        var options = await setup.Host.Get<IAiOptionsService>().GetAsync(AiTestData.Member, CancellationToken.None);

        // Assert
        Assert.That(options.Connections.Select(c => c.Name), Is.EqualTo(new[] { "Org" }));
    }

    [Test]
    public async Task Resolve_OwnPersonalConnection_ReturnsTheDecryptedEndpoint()
    {
        // Arrange
        var setup = await StartAsync();
        await using var _ = setup.Host;

        // Act
        var resolved = await setup.Host.Get<AiSelectionResolver>().ResolveAsync(
            AiTestData.Member, new AiSelection(setup.Mine.Id, "model-b", AiEffort.Low), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved.Value.Endpoint.ApiKey, Is.EqualTo("sk-ant-key-0001"));
            Assert.That(resolved.Value.ConnectionName, Is.EqualTo("Mine"));
        }
    }

    [Test]
    public async Task Resolve_OtherMembersPersonalConnection_IsSelectionInvalidWithoutRevealingIt()
    {
        // Arrange
        var setup = await StartAsync();
        await using var _ = setup.Host;
        var resolver = setup.Host.Get<AiSelectionResolver>();

        // Act
        var theirs = await resolver.ResolveAsync(AiTestData.Member, new AiSelection(setup.Theirs.Id, "model-a", null), CancellationToken.None);
        var missing = await resolver.ResolveAsync(AiTestData.Member, new AiSelection(Guid.CreateVersion7(), "model-a", null), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(theirs.Failure?.Code, Is.EqualTo(AiErrorCodes.SelectionInvalid));
            Assert.That(theirs.Failure?.Message, Is.EqualTo(missing.Failure?.Message));
        }
    }

    [Test]
    public async Task Resolve_PersonalWhenTheOrganizationNoLongerAllows_IsSelectionInvalid()
    {
        // Arrange
        var setup = await StartAsync(allowPersonal: false);
        await using var _ = setup.Host;

        // Act
        var resolved = await setup.Host.Get<AiSelectionResolver>().ResolveAsync(
            AiTestData.Member, new AiSelection(setup.Mine.Id, "model-b", null), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved.Failure?.Code, Is.EqualTo(AiErrorCodes.SelectionInvalid));
            Assert.That(resolved.Failure?.Message, Does.Contain("不允許"));
        }
    }

    [Test]
    public async Task Resolve_OrganizationAiOff_PersonalStillWorksButTheOrganizationsConnectionIsUnavailable()
    {
        // Arrange
        var setup = await StartAsync(orgEnabled: false);
        await using var _ = setup.Host;
        var resolver = setup.Host.Get<AiSelectionResolver>();

        // Act
        var mine = await resolver.ResolveAsync(AiTestData.Member, new AiSelection(setup.Mine.Id, "model-b", null), CancellationToken.None);
        var org = await resolver.ResolveAsync(AiTestData.Member, new AiSelection(setup.Org.Id, "model-a", null), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mine.Succeeded, Is.True);
            Assert.That(org.Failure?.Code, Is.EqualTo(AiErrorCodes.Unavailable));
        }
    }

    [Test]
    public async Task Resolve_OrganizationAiOffAndPersonalNotAllowed_IsUnavailable()
    {
        // Arrange
        var setup = await StartAsync(orgEnabled: false, allowPersonal: false);
        await using var _ = setup.Host;

        // Act
        var resolved = await setup.Host.Get<AiSelectionResolver>().ResolveAsync(
            AiTestData.Member, new AiSelection(Guid.CreateVersion7(), "x", null), CancellationToken.None);

        // Assert
        Assert.That(resolved.Failure?.Code, Is.EqualTo(AiErrorCodes.Unavailable));
    }

    [Test]
    public async Task Usage_ForAPersonalConnection_IsNotShownOnTheOrganizationsSettings()
    {
        // Arrange
        var setup = await StartAsync();
        await using var _ = setup.Host;

        // Act
        var settings = await setup.Host.Get<IAiSettingsService>().GetAsync(AiTestData.Admin, CancellationToken.None);

        // Assert
        Assert.That(settings.Value.Connections.Select(c => c.Id), Is.EqualTo(new[] { setup.Org.Id }));
    }
}
