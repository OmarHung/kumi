using PmTool.Abstractions;

namespace PmTool.Ai.Tests.Settings;

/// <summary>自己的 AI 連線（internal/HANDOFF-ai-floating.md 第 4.1 節）：權限、隔離、名稱與上限。對真的 PostgreSQL。</summary>
[TestFixture]
public sealed class PersonalConnectionTests
{
    private static readonly Actor Other = new(AiTestData.OrganizationId, Guid.CreateVersion7(), IsOrganizationAdmin: false);

    private static async Task<AiTestHost> StartAsync(bool allowPersonal = true)
    {
        var host = await AiTestHost.StartAsync();
        await host.Get<IAiSettingsService>().SaveAsync(
            AiTestData.Admin, new AiSettingsInput(false, null) { AllowPersonalConnections = allowPersonal }, CancellationToken.None);
        return host;
    }

    [Test]
    public async Task Create_WhenAllowed_StoresAnEncryptedKeyAndListsItForTheOwnerOnly()
    {
        // Arrange
        await using var host = await StartAsync();
        var personal = host.Get<IPersonalAiConnectionService>();

        // Act
        var created = await personal.CreateAsync(AiTestData.Member, AiTestData.Anthropic(enabled: ["model-a"]), CancellationToken.None);
        var mine = await personal.ListAsync(AiTestData.Member, CancellationToken.None);
        var theirs = await personal.ListAsync(Other, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Succeeded, Is.True);
            Assert.That(created.Value.HasApiKey, Is.True);
            Assert.That(created.Value.ApiKeyHint, Is.EqualTo("0001"));
            Assert.That(mine.Select(c => c.Id), Is.EqualTo(new[] { created.Value.Id }));
            Assert.That(theirs, Is.Empty);
            Assert.That(host.Gateway.DiscoverCalls.Single().ApiKey, Is.EqualTo("sk-ant-key-0001"));
        }
    }

    [Test]
    public async Task Create_WhenTheOrganizationDoesNotAllow_IsForbiddenWithoutCallingTheProvider()
    {
        // Arrange
        await using var host = await StartAsync(allowPersonal: false);

        // Act
        var created = await host.Get<IPersonalAiConnectionService>().CreateAsync(AiTestData.Member, AiTestData.Anthropic(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(created.Failure?.Code, Is.EqualTo(AiErrorCodes.PersonalDisabled));
            Assert.That(host.Gateway.DiscoverCalls, Is.Empty);
        }
    }

    [Test]
    public async Task Create_OrganizationNeverConfigured_IsForbidden()
    {
        // Arrange
        await using var host = await AiTestHost.StartAsync();

        // Act
        var created = await host.Get<IPersonalAiConnectionService>().CreateAsync(AiTestData.Member, AiTestData.Anthropic(), CancellationToken.None);

        // Assert
        Assert.That(created.Failure?.Code, Is.EqualTo(AiErrorCodes.PersonalDisabled));
    }

    [Test]
    public async Task PersonalAndOrganizationConnections_AreInvisibleToEachOthersServices()
    {
        // Arrange
        await using var host = await StartAsync();
        var settings = host.Get<IAiSettingsService>();
        var personal = host.Get<IPersonalAiConnectionService>();
        var org = (await settings.CreateConnectionAsync(AiTestData.Admin, AiTestData.Anthropic("Org"), CancellationToken.None)).Value;
        var mine = (await personal.CreateAsync(AiTestData.Member, AiTestData.Anthropic("Mine"), CancellationToken.None)).Value;

        // Act
        var orgView = await settings.GetAsync(AiTestData.Admin, CancellationToken.None);
        var memberView = await personal.ListAsync(AiTestData.Member, CancellationToken.None);
        var updatePersonalViaOrg = await settings.UpdateConnectionAsync(AiTestData.Admin, mine.Id, AiTestData.Anthropic("Hijack"), CancellationToken.None);
        var refreshPersonalViaOrg = await settings.RefreshModelsAsync(AiTestData.Admin, mine.Id, CancellationToken.None);
        var deletePersonalViaOrg = await settings.DeleteConnectionAsync(AiTestData.Admin, mine.Id, CancellationToken.None);
        var discoverViaOrg = await settings.DiscoverModelsAsync(
            AiTestData.Admin, new AiDiscoveryInput(AiProviderKind.Anthropic, null, null, mine.Id), CancellationToken.None);
        var updateOrgViaPersonal = await personal.UpdateAsync(AiTestData.Member, org.Id, AiTestData.Anthropic("Hijack"), CancellationToken.None);
        var deleteOrgViaPersonal = await personal.DeleteAsync(AiTestData.Member, org.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(orgView.Value.Connections.Select(c => c.Name), Is.EqualTo(new[] { "Org" }));
            Assert.That(memberView.Select(c => c.Name), Is.EqualTo(new[] { "Mine" }));
            Assert.That(
                new[] { updatePersonalViaOrg.Failure?.Code, refreshPersonalViaOrg.Failure?.Code, deletePersonalViaOrg.Failure?.Code, discoverViaOrg.Failure?.Code, updateOrgViaPersonal.Failure?.Code, deleteOrgViaPersonal.Failure?.Code },
                Is.All.EqualTo(AiErrorCodes.ConnectionNotFound));
        }
    }

    [Test]
    public async Task OtherMembersConnection_IsNotFoundForEveryOperation()
    {
        // Arrange
        await using var host = await StartAsync();
        var personal = host.Get<IPersonalAiConnectionService>();
        var theirs = (await personal.CreateAsync(Other, AiTestData.Anthropic(), CancellationToken.None)).Value;

        // Act
        var update = await personal.UpdateAsync(AiTestData.Member, theirs.Id, AiTestData.Anthropic("X"), CancellationToken.None);
        var refresh = await personal.RefreshModelsAsync(AiTestData.Member, theirs.Id, CancellationToken.None);
        var delete = await personal.DeleteAsync(AiTestData.Member, theirs.Id, CancellationToken.None);
        var discover = await personal.DiscoverModelsAsync(
            AiTestData.Member, new AiDiscoveryInput(AiProviderKind.Anthropic, null, null, theirs.Id), CancellationToken.None);

        // Assert
        Assert.That(
            new[] { update.Failure?.Code, refresh.Failure?.Code, delete.Failure?.Code, discover.Failure?.Code },
            Is.All.EqualTo(AiErrorCodes.ConnectionNotFound));
    }

    [Test]
    public async Task Name_IsUniqueOnlyAmongTheSamePeoplesConnections()
    {
        // Arrange
        await using var host = await StartAsync();
        var settings = host.Get<IAiSettingsService>();
        var personal = host.Get<IPersonalAiConnectionService>();
        await settings.CreateConnectionAsync(AiTestData.Admin, AiTestData.Anthropic("Shared"), CancellationToken.None);

        // Act
        var mine = await personal.CreateAsync(AiTestData.Member, AiTestData.Anthropic("Shared"), CancellationToken.None);
        var theirs = await personal.CreateAsync(Other, AiTestData.Anthropic("SHARED"), CancellationToken.None);
        var duplicate = await personal.CreateAsync(AiTestData.Member, AiTestData.Anthropic("shared"), CancellationToken.None);
        var orgDuplicate = await settings.CreateConnectionAsync(AiTestData.Admin, AiTestData.Anthropic("shared"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mine.Succeeded, Is.True);
            Assert.That(theirs.Succeeded, Is.True);
            Assert.That(duplicate.Failure?.Details.Single().Path, Is.EqualTo("name"));
            Assert.That(orgDuplicate.Failure?.Details.Single().Path, Is.EqualTo("name"));
        }
    }

    [Test]
    public async Task Create_AtThePersonalLimit_FailsWithTooManyConnectionsButOthersAreUnaffected()
    {
        // Arrange
        await using var host = await StartAsync();
        var personal = host.Get<IPersonalAiConnectionService>();
        for (var i = 0; i < AiRules.MaxPersonalConnections; i++)
        {
            await personal.CreateAsync(AiTestData.Member, AiTestData.Anthropic($"c{i}"), CancellationToken.None);
        }

        // Act
        var over = await personal.CreateAsync(AiTestData.Member, AiTestData.Anthropic("extra"), CancellationToken.None);
        var other = await personal.CreateAsync(Other, AiTestData.Anthropic("extra"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(over.Failure?.Code, Is.EqualTo(AiErrorCodes.SettingsInvalid));
            Assert.That(over.Failure?.Details.Single().Code, Is.EqualTo(AiErrorCodes.TooManyConnections));
            Assert.That(over.Failure?.Message, Does.Contain($"{AiRules.MaxPersonalConnections}"));
            Assert.That(other.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task WhenTheOrganizationTurnsItOff_ListAndDeleteStillWorkButChangesAreRefused()
    {
        // Arrange
        await using var host = await StartAsync();
        var personal = host.Get<IPersonalAiConnectionService>();
        var mine = (await personal.CreateAsync(AiTestData.Member, AiTestData.Anthropic(), CancellationToken.None)).Value;
        await host.Get<IAiSettingsService>().SaveAsync(AiTestData.Admin, new AiSettingsInput(false, null), CancellationToken.None);

        // Act
        var listed = await personal.ListAsync(AiTestData.Member, CancellationToken.None);
        var update = await personal.UpdateAsync(AiTestData.Member, mine.Id, AiTestData.Anthropic("Renamed"), CancellationToken.None);
        var refresh = await personal.RefreshModelsAsync(AiTestData.Member, mine.Id, CancellationToken.None);
        var discover = await personal.DiscoverModelsAsync(
            AiTestData.Member, new AiDiscoveryInput(AiProviderKind.Anthropic, null, "sk-ant-key-0001", null), CancellationToken.None);
        var deleted = await personal.DeleteAsync(AiTestData.Member, mine.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(listed, Has.Count.EqualTo(1));
            Assert.That(
                new[] { update.Failure?.Code, refresh.Failure?.Code, discover.Failure?.Code }, Is.All.EqualTo(AiErrorCodes.PersonalDisabled));
            Assert.That(deleted.Succeeded, Is.True);
            Assert.That(await personal.ListAsync(AiTestData.Member, CancellationToken.None), Is.Empty);
        }
    }

    [Test]
    public async Task UpdateRefreshAndDiscover_WorkOnOwnConnectionAndKeepTheKey()
    {
        // Arrange
        await using var host = await StartAsync();
        var personal = host.Get<IPersonalAiConnectionService>();
        var mine = (await personal.CreateAsync(AiTestData.Member, AiTestData.Anthropic(enabled: ["model-a"]), CancellationToken.None)).Value;
        host.Gateway.OnDiscover = _ => StubAiProviderGateway.Models("model-a", "model-c");

        // Act
        var renamed = await personal.UpdateAsync(
            AiTestData.Member, mine.Id, AiTestData.Anthropic("Renamed", apiKey: null, enabled: ["model-a"]), CancellationToken.None);
        var refreshed = await personal.RefreshModelsAsync(AiTestData.Member, mine.Id, CancellationToken.None);
        var discovered = await personal.DiscoverModelsAsync(
            AiTestData.Member, new AiDiscoveryInput(AiProviderKind.Anthropic, null, null, mine.Id), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(renamed.Value.Name, Is.EqualTo("Renamed"));
            Assert.That(refreshed.Value.Models.Select(m => m.Id), Is.EqualTo(new[] { "model-a", "model-c" }));
            Assert.That(discovered.Value.Select(m => m.Id), Is.EqualTo(new[] { "model-a", "model-c" }));
            Assert.That(host.Gateway.DiscoverCalls.Last().ApiKey, Is.EqualTo("sk-ant-key-0001"));
        }
    }

    [Test]
    public async Task OrganizationSettings_SavesAndReadsTheAllowFlagAndIgnoresPersonalConnections()
    {
        // Arrange
        await using var host = await StartAsync();
        await host.Get<IPersonalAiConnectionService>().CreateAsync(AiTestData.Member, AiTestData.Anthropic(), CancellationToken.None);

        // Act
        var settings = await host.Get<IAiSettingsService>().GetAsync(AiTestData.Admin, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(settings.Value.AllowPersonalConnections, Is.True);
            Assert.That(settings.Value.Connections, Is.Empty);
        }
    }

    [Test]
    public async Task Create_OpenRouterManualModelWithAHash_IsFieldInvalidButOtherKindsAreNotAffected()
    {
        // Arrange
        await using var host = await StartAsync();
        var personal = host.Get<IPersonalAiConnectionService>();

        // Act
        var refused = await personal.CreateAsync(
            AiTestData.Member,
            new AiConnectionInput("OR", AiProviderKind.Openrouter, null, "sk-or-v1-key-0001", [], ["vendor/model#x"]),
            CancellationToken.None);
        var fine = await personal.CreateAsync(
            AiTestData.Member, AiTestData.Anthropic("A", custom: ["model#x"]), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(refused.Failure?.Details.Single().Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
            Assert.That(refused.Failure?.Details.Single().Path, Is.EqualTo("customModels[0]"));
            Assert.That(fine.Succeeded, Is.True);
        }
    }
}
