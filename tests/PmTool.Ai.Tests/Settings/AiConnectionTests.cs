using PmTool.Abstractions;

namespace PmTool.Ai.Tests.Settings;

/// <summary>連線的新增、修改、重新讀取、刪除（internal/HANDOFF-ai-assistant.md 第 4.6 節）。對真的 PostgreSQL。</summary>
[TestFixture]
public sealed class AiConnectionTests
{
    private static async Task<(AiTestHost Host, IAiSettingsService Service)> StartAsync()
    {
        var host = await AiTestHost.StartAsync();
        return (host, host.Get<IAiSettingsService>());
    }

    [Test]
    public async Task Create_ReadsTheCatalogWithTheKeyAndStoresTheEnabledModels()
    {
        // Arrange
        var (host, service) = await StartAsync();
        await using var _ = host;

        // Act
        var created = await service.CreateConnectionAsync(AiTestData.Admin, AiTestData.Anthropic(enabled: ["model-b"]), CancellationToken.None);

        // Assert
        var connection = created.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(connection.Name, Is.EqualTo("Anthropic"));
            Assert.That(connection.HasApiKey, Is.True);
            Assert.That(connection.ApiKeyHint, Is.EqualTo("0001"));
            Assert.That(connection.Models.Select(m => (m.Id, m.Enabled, m.Custom)), Is.EqualTo(new[] { ("model-a", false, false), ("model-b", true, false) }));
            Assert.That(connection.Models[0].Efforts, Is.EqualTo(new[] { AiEffort.Low, AiEffort.High }));
            Assert.That(connection.ModelsCheckedAt, Is.Not.Null);
            Assert.That(host.Gateway.DiscoverCalls.Single().ApiKey, Is.EqualTo("sk-ant-key-0001"));
        }
    }

    [Test]
    public async Task Create_ProviderFails_ReturnsProviderFailedAndStoresNothing()
    {
        // Arrange
        var (host, service) = await StartAsync();
        await using var _ = host;
        host.Gateway.OnDiscover = _ => throw new global::PmTool.Ai.Providers.AiProviderException("金鑰不對，或這把金鑰沒有權限。");

        // Act
        var created = await service.CreateConnectionAsync(AiTestData.Admin, AiTestData.Anthropic(), CancellationToken.None);
        var settings = await service.GetAsync(AiTestData.Admin, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Failure?.Code, Is.EqualTo(AiErrorCodes.ProviderFailed));
            Assert.That(created.Failure?.Kind, Is.EqualTo(FailureKind.Invalid));
            Assert.That(created.Failure?.Message, Is.EqualTo("金鑰不對，或這把金鑰沒有權限。"));
            Assert.That(settings.Value.Connections, Is.Empty);
        }
    }

    [Test]
    public async Task Create_NonAdmin_IsForbiddenAndDoesNotCallTheProvider()
    {
        // Arrange
        var (host, service) = await StartAsync();
        await using var _ = host;

        // Act
        var created = await service.CreateConnectionAsync(AiTestData.Member, AiTestData.Anthropic(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(created.Failure?.Code, Is.EqualTo(AiErrorCodes.Forbidden));
            Assert.That(host.Gateway.DiscoverCalls, Is.Empty);
        }
    }

    [Test]
    public async Task Create_EnabledModelNotInTheCatalogOrCustom_ReportsThePathOfThatModel()
    {
        // Arrange
        var (host, service) = await StartAsync();
        await using var _ = host;

        // Act
        var created = await service.CreateConnectionAsync(
            AiTestData.Admin, AiTestData.Anthropic(enabled: ["model-a", "ghost"]), CancellationToken.None);

        // Assert
        var detail = created.Failure!.Details.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Failure.Code, Is.EqualTo(AiErrorCodes.SettingsInvalid));
            Assert.That(detail.Code, Is.EqualTo(AiErrorCodes.ModelNotAvailable));
            Assert.That(detail.Path, Is.EqualTo("enabledModels[1]"));
        }
    }

    [Test]
    public async Task Create_CustomModel_IsAddedWithEffortsGuessedFromTheName()
    {
        // Arrange
        var (host, service) = await StartAsync();
        await using var _ = host;

        // Act
        var created = await service.CreateConnectionAsync(
            AiTestData.Admin,
            new AiConnectionInput("OpenAI", AiProviderKind.Openai, null, "sk-openai-0001", ["gpt-5.2"], ["gpt-5.2"]),
            CancellationToken.None);

        // Assert
        var custom = created.Value.Models.Single(m => m.Id == "gpt-5.2");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(custom.Custom, Is.True);
            Assert.That(custom.Enabled, Is.True);
            Assert.That(custom.Efforts, Is.EqualTo(new[] { AiEffort.None, AiEffort.Low, AiEffort.Medium, AiEffort.High, AiEffort.Xhigh }));
        }
    }

    [Test]
    public async Task Create_CustomModelAlsoInTheCatalog_CountsAsFromTheCatalog()
    {
        // Arrange
        var (host, service) = await StartAsync();
        await using var _ = host;

        // Act
        var created = await service.CreateConnectionAsync(
            AiTestData.Admin, AiTestData.Anthropic(custom: ["model-a"]), CancellationToken.None);

        // Assert
        Assert.That(created.Value.Models.Single(m => m.Id == "model-a").Custom, Is.False);
    }

    [Test]
    public async Task Create_SameNameInDifferentCase_IsRejected()
    {
        // Arrange
        var (host, service) = await StartAsync();
        await using var _ = host;
        await service.CreateConnectionAsync(AiTestData.Admin, AiTestData.Anthropic("Team Key"), CancellationToken.None);

        // Act
        var second = await service.CreateConnectionAsync(AiTestData.Admin, AiTestData.Anthropic("team key"), CancellationToken.None);

        // Assert
        var detail = second.Failure!.Details.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(detail.Path, Is.EqualTo("name"));
            Assert.That(detail.Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
        }
    }

    [Test]
    public async Task Create_SameNameInAnotherOrganization_IsAllowed()
    {
        // Arrange
        var (host, service) = await StartAsync();
        await using var _ = host;
        await service.CreateConnectionAsync(AiTestData.Admin, AiTestData.Anthropic(), CancellationToken.None);
        var other = new Actor(Guid.CreateVersion7(), Guid.CreateVersion7(), IsOrganizationAdmin: true);

        // Act
        var created = await service.CreateConnectionAsync(other, AiTestData.Anthropic(), CancellationToken.None);

        // Assert
        Assert.That(created.Succeeded, Is.True);
    }

    [Test]
    public async Task Create_AtTheConnectionLimit_FailsWithTheTooManyConnectionsDetailAndSkipsTheProvider()
    {
        // Arrange
        var (host, service) = await StartAsync();
        await using var _ = host;
        for (var i = 0; i < AiRules.MaxConnectionsPerOrganization; i++)
        {
            await service.CreateConnectionAsync(AiTestData.Admin, AiTestData.Anthropic($"Key {i}"), CancellationToken.None);
        }

        var callsBefore = host.Gateway.DiscoverCalls.Count;

        // Act
        var extra = await service.CreateConnectionAsync(AiTestData.Admin, AiTestData.Anthropic("One more"), CancellationToken.None);

        // Assert
        var detail = extra.Failure!.Details.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(detail.Code, Is.EqualTo(AiErrorCodes.TooManyConnections));
            Assert.That(detail.Path, Is.EqualTo(string.Empty));
            Assert.That(host.Gateway.DiscoverCalls, Has.Count.EqualTo(callsBefore));
        }
    }

    [Test]
    public async Task Update_OnlyNameAndEnabledModels_KeepsTheKeyAndDoesNotCallTheProvider()
    {
        // Arrange
        var (host, service) = await StartAsync();
        await using var _ = host;
        var created = (await service.CreateConnectionAsync(AiTestData.Admin, AiTestData.Anthropic(), CancellationToken.None)).Value;

        // Act
        var updated = await service.UpdateConnectionAsync(
            AiTestData.Admin, created.Id, AiTestData.Anthropic("Renamed", apiKey: null, enabled: ["model-a"]), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated.Value.Name, Is.EqualTo("Renamed"));
            Assert.That(updated.Value.HasApiKey, Is.True);
            Assert.That(updated.Value.ApiKeyHint, Is.EqualTo("0001"));
            Assert.That(updated.Value.Models.Single(m => m.Enabled).Id, Is.EqualTo("model-a"));
            Assert.That(host.Gateway.DiscoverCalls, Has.Count.EqualTo(1));
            Assert.That(updated.Value.ModelsCheckedAt, Is.EqualTo(created.ModelsCheckedAt));
        }
    }

    [Test]
    public async Task Update_NewKey_RediscoversWithTheNewKeyAndChangesTheHint()
    {
        // Arrange
        var (host, service) = await StartAsync();
        await using var _ = host;
        var created = (await service.CreateConnectionAsync(AiTestData.Admin, AiTestData.Anthropic(), CancellationToken.None)).Value;
        host.Gateway.OnDiscover = _ => StubAiProviderGateway.Models("model-c");

        // Act
        var updated = await service.UpdateConnectionAsync(
            AiTestData.Admin, created.Id, AiTestData.Anthropic(apiKey: "sk-ant-key-9999"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(host.Gateway.DiscoverCalls.Last().ApiKey, Is.EqualTo("sk-ant-key-9999"));
            Assert.That(updated.Value.ApiKeyHint, Is.EqualTo("9999"));
            Assert.That(updated.Value.Models.Select(m => m.Id), Is.EqualTo(new[] { "model-c" }));
        }
    }

    [Test]
    public async Task Update_KeepKeyButChangeTheBaseUrl_RequiresTheKeyAgain()
    {
        // Arrange
        var (host, service) = await StartAsync();
        await using var _ = host;
        var created = (await service.CreateConnectionAsync(
            AiTestData.Admin, AiTestData.Compatible(apiKey: "local-secret-key"), CancellationToken.None)).Value;

        // Act
        var updated = await service.UpdateConnectionAsync(
            AiTestData.Admin, created.Id, AiTestData.Compatible(baseUrl: "http://evil.example.com/v1", apiKey: null), CancellationToken.None);

        // Assert
        var detail = updated.Failure!.Details.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(detail.Path, Is.EqualTo("apiKey"));
            Assert.That(detail.Code, Is.EqualTo(AiErrorCodes.FieldRequired));
            Assert.That(host.Gateway.DiscoverCalls, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Update_KeepKeyButChangeTheKind_RequiresTheKeyAgain()
    {
        // Arrange
        var (host, service) = await StartAsync();
        await using var _ = host;
        var created = (await service.CreateConnectionAsync(AiTestData.Admin, AiTestData.Anthropic(), CancellationToken.None)).Value;

        // Act
        var updated = await service.UpdateConnectionAsync(
            AiTestData.Admin, created.Id, new AiConnectionInput("Anthropic", AiProviderKind.Openai, null, null, [], []), CancellationToken.None);

        // Assert
        Assert.That(updated.Failure!.Details.Single().Path, Is.EqualTo("apiKey"));
    }

    [Test]
    public async Task Update_EmptyKeyOnACompatibleEndpoint_ClearsTheKey()
    {
        // Arrange
        var (host, service) = await StartAsync();
        await using var _ = host;
        var created = (await service.CreateConnectionAsync(
            AiTestData.Admin, AiTestData.Compatible(apiKey: "local-secret-key"), CancellationToken.None)).Value;

        // Act
        var updated = await service.UpdateConnectionAsync(
            AiTestData.Admin, created.Id, AiTestData.Compatible(apiKey: string.Empty), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated.Value.HasApiKey, Is.False);
            Assert.That(updated.Value.ApiKeyHint, Is.Null);
            Assert.That(host.Gateway.DiscoverCalls.Last().ApiKey, Is.Null);
        }
    }

    [Test]
    public async Task Update_EmptyKeyOnAnthropic_IsFieldRequired()
    {
        // Arrange
        var (host, service) = await StartAsync();
        await using var _ = host;
        var created = (await service.CreateConnectionAsync(AiTestData.Admin, AiTestData.Anthropic(), CancellationToken.None)).Value;

        // Act
        var updated = await service.UpdateConnectionAsync(
            AiTestData.Admin, created.Id, AiTestData.Anthropic(apiKey: string.Empty), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated.Failure!.Details.Single().Code, Is.EqualTo(AiErrorCodes.FieldRequired));
            Assert.That(updated.Failure.Details.Single().Path, Is.EqualTo("apiKey"));
        }
    }

    [Test]
    public async Task Update_UnknownConnection_IsNotFound()
    {
        // Arrange
        var (host, service) = await StartAsync();
        await using var _ = host;

        // Act
        var updated = await service.UpdateConnectionAsync(AiTestData.Admin, Guid.CreateVersion7(), AiTestData.Anthropic(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated.Failure?.Kind, Is.EqualTo(FailureKind.NotFound));
            Assert.That(updated.Failure?.Code, Is.EqualTo(AiErrorCodes.ConnectionNotFound));
        }
    }

    [Test]
    public async Task Update_ConnectionOfAnotherOrganization_IsNotFound()
    {
        // Arrange
        var (host, service) = await StartAsync();
        await using var _ = host;
        var created = (await service.CreateConnectionAsync(AiTestData.Admin, AiTestData.Anthropic(), CancellationToken.None)).Value;
        var other = new Actor(Guid.CreateVersion7(), Guid.CreateVersion7(), IsOrganizationAdmin: true);

        // Act
        var updated = await service.UpdateConnectionAsync(other, created.Id, AiTestData.Anthropic(apiKey: null), CancellationToken.None);

        // Assert
        Assert.That(updated.Failure?.Code, Is.EqualTo(AiErrorCodes.ConnectionNotFound));
    }

    [Test]
    public async Task Update_DisablingTheDefaultModel_IsRejected()
    {
        // Arrange
        var (host, service) = await StartAsync();
        await using var _ = host;
        var created = (await service.CreateConnectionAsync(AiTestData.Admin, AiTestData.Anthropic(enabled: ["model-a"]), CancellationToken.None)).Value;
        await service.SaveAsync(
            AiTestData.Admin, new AiSettingsInput(true, new AiSelection(created.Id, "model-a", AiEffort.High)), CancellationToken.None);

        // Act
        var updated = await service.UpdateConnectionAsync(
            AiTestData.Admin, created.Id, AiTestData.Anthropic(apiKey: null, enabled: ["model-b"]), CancellationToken.None);

        // Assert
        var detail = updated.Failure!.Details.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(detail.Code, Is.EqualTo(AiErrorCodes.ModelNotAvailable));
            Assert.That(detail.Path, Is.EqualTo("enabledModels"));
            Assert.That(detail.Message, Does.Contain("先換預設"));
        }
    }

    [Test]
    public async Task Refresh_KeepsEnabledFlagsMarksVanishedEnabledModelsAsCustomAndDropsVanishedDisabledOnes()
    {
        // Arrange
        var (host, service) = await StartAsync();
        await using var _ = host;
        host.Gateway.OnDiscover = _ => StubAiProviderGateway.Models("keep", "enabled-gone", "disabled-gone");
        var created = (await service.CreateConnectionAsync(
            AiTestData.Admin, AiTestData.Anthropic(enabled: ["keep", "enabled-gone"], custom: ["typed-by-hand"]), CancellationToken.None)).Value;
        host.Gateway.OnDiscover = _ => StubAiProviderGateway.Models("keep", "brand-new");

        // Act
        var refreshed = await service.RefreshModelsAsync(AiTestData.Admin, created.Id, CancellationToken.None);

        // Assert
        var models = refreshed.Value.Models.ToDictionary(m => m.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(models.Keys, Is.EquivalentTo(new[] { "brand-new", "enabled-gone", "keep", "typed-by-hand" }));
            Assert.That(models["keep"], Has.Property("Enabled").True.And.Property("Custom").False);
            Assert.That(models["brand-new"], Has.Property("Enabled").False.And.Property("Custom").False);
            Assert.That(models["enabled-gone"], Has.Property("Enabled").True.And.Property("Custom").True);
            Assert.That(models["typed-by-hand"].Custom, Is.True);
            Assert.That(host.Gateway.DiscoverCalls.Last().ApiKey, Is.EqualTo("sk-ant-key-0001"));
        }
    }

    [Test]
    public async Task Update_NewCatalogWithoutTheDefaultEffort_IsModelNotAvailable()
    {
        // Arrange：預設是 model-a 的「高」，換金鑰重新讀之後 model-a 只剩「低」。
        var (host, service) = await StartAsync();
        await using var _ = host;
        var created = (await service.CreateConnectionAsync(AiTestData.Admin, AiTestData.Anthropic(enabled: ["model-a"]), CancellationToken.None)).Value;
        await service.SaveAsync(
            AiTestData.Admin, new AiSettingsInput(true, new AiSelection(created.Id, "model-a", AiEffort.High)), CancellationToken.None);
        host.Gateway.OnDiscover = _ => [new AiDiscoveredModel("model-a", "A", [AiEffort.Low])];

        // Act
        var updated = await service.UpdateConnectionAsync(
            AiTestData.Admin, created.Id, AiTestData.Anthropic(apiKey: "sk-ant-key-0002", enabled: ["model-a"]), CancellationToken.None);

        // Assert
        var detail = updated.Failure!.Details.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated.Failure.Code, Is.EqualTo(AiErrorCodes.SettingsInvalid));
            Assert.That(detail.Code, Is.EqualTo(AiErrorCodes.ModelNotAvailable));
            Assert.That(detail.Path, Is.EqualTo("enabledModels"));
            Assert.That(detail.Message, Does.Contain("思考深度"));
        }
    }

    [Test]
    public async Task Refresh_NewCatalogWithoutTheDefaultEffort_ClearsTheDefaultEffortButKeepsTheModelAndAiOn()
    {
        // Arrange
        var (host, service) = await StartAsync();
        await using var _ = host;
        var created = (await service.CreateConnectionAsync(AiTestData.Admin, AiTestData.Anthropic(enabled: ["model-a"]), CancellationToken.None)).Value;
        await service.SaveAsync(
            AiTestData.Admin, new AiSettingsInput(true, new AiSelection(created.Id, "model-a", AiEffort.High)), CancellationToken.None);
        host.Gateway.OnDiscover = _ => [new AiDiscoveredModel("model-a", "A", [AiEffort.Low])];

        // Act
        var refreshed = await service.RefreshModelsAsync(AiTestData.Admin, created.Id, CancellationToken.None);
        var settings = (await service.GetAsync(AiTestData.Admin, CancellationToken.None)).Value;

        // Assert：重新讀取不被擋下；預設的模型留著，思考深度改成「預設」（null）。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(refreshed.Succeeded, Is.True, () => refreshed.Failure?.Message ?? string.Empty);
            Assert.That(settings.Enabled, Is.True);
            Assert.That(settings.Default, Is.EqualTo(new AiSelection(created.Id, "model-a", null)));
        }
    }

    [Test]
    public async Task Refresh_NewCatalogStillHasTheDefaultEffort_LeavesTheDefaultAlone()
    {
        // Arrange
        var (host, service) = await StartAsync();
        await using var _ = host;
        var created = (await service.CreateConnectionAsync(AiTestData.Admin, AiTestData.Anthropic(enabled: ["model-a"]), CancellationToken.None)).Value;
        await service.SaveAsync(
            AiTestData.Admin, new AiSettingsInput(true, new AiSelection(created.Id, "model-a", AiEffort.High)), CancellationToken.None);

        // Act
        await service.RefreshModelsAsync(AiTestData.Admin, created.Id, CancellationToken.None);

        // Assert
        var settings = (await service.GetAsync(AiTestData.Admin, CancellationToken.None)).Value;
        Assert.That(settings.Default, Is.EqualTo(new AiSelection(created.Id, "model-a", AiEffort.High)));
    }

    [Test]
    public async Task Refresh_ConnectionChangedWhileReadingTheCatalog_IsAConflict()
    {
        // Arrange：連供應商的那幾秒裡，另一個管理員改了名稱。
        var (host, service) = await StartAsync();
        await using var _ = host;
        var created = (await service.CreateConnectionAsync(AiTestData.Admin, AiTestData.Anthropic(enabled: ["model-a"]), CancellationToken.None)).Value;
        host.Gateway.OnDiscover = _ =>
        {
            ((ManualClock)host.Get<TimeProvider>()).Advance(TimeSpan.FromSeconds(1));
            var other = service.UpdateConnectionAsync(
                AiTestData.Admin, created.Id, AiTestData.Anthropic(name: "改過的名稱", apiKey: null, enabled: ["model-a"]), CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert.That(other.Succeeded, Is.True);
            return StubAiProviderGateway.Models("model-a");
        };

        // Act
        var refreshed = await service.RefreshModelsAsync(AiTestData.Admin, created.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(refreshed.Failure?.Kind, Is.EqualTo(FailureKind.Conflict));
            Assert.That(refreshed.Failure?.Code, Is.EqualTo(AiErrorCodes.SettingsConflict));
            Assert.That(refreshed.Failure?.Message, Does.Contain("剛被別人改過"));
        }
    }

    [Test]
    public async Task Update_ConnectionChangedWhileReadingTheCatalog_IsAConflict()
    {
        // Arrange
        var (host, service) = await StartAsync();
        await using var _ = host;
        var created = (await service.CreateConnectionAsync(AiTestData.Admin, AiTestData.Anthropic(enabled: ["model-a"]), CancellationToken.None)).Value;
        var calls = 0;
        host.Gateway.OnDiscover = _ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                ((ManualClock)host.Get<TimeProvider>()).Advance(TimeSpan.FromSeconds(1));
                service.UpdateConnectionAsync(
                    AiTestData.Admin, created.Id, AiTestData.Anthropic(name: "改過的名稱", apiKey: null, enabled: ["model-a"]), CancellationToken.None)
                    .GetAwaiter().GetResult();
            }

            return StubAiProviderGateway.Models("model-a");
        };

        // Act
        var updated = await service.UpdateConnectionAsync(
            AiTestData.Admin, created.Id, AiTestData.Anthropic(apiKey: "sk-ant-key-0002", enabled: ["model-a"]), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated.Failure?.Kind, Is.EqualTo(FailureKind.Conflict));
            Assert.That(updated.Failure?.Code, Is.EqualTo(AiErrorCodes.SettingsConflict));
        }
    }

    [Test]
    public async Task Refresh_ProviderFails_ChangesNothing()
    {
        // Arrange
        var (host, service) = await StartAsync();
        await using var _ = host;
        var created = (await service.CreateConnectionAsync(AiTestData.Admin, AiTestData.Anthropic(enabled: ["model-a"]), CancellationToken.None)).Value;
        host.Gateway.OnDiscover = _ => throw new global::PmTool.Ai.Providers.AiProviderException("供應商太久沒有回應。");

        // Act
        var refreshed = await service.RefreshModelsAsync(AiTestData.Admin, created.Id, CancellationToken.None);
        var settings = await service.GetAsync(AiTestData.Admin, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(refreshed.Failure?.Code, Is.EqualTo(AiErrorCodes.ProviderFailed));
            Assert.That(settings.Value.Connections.Single().Models.Select(m => m.Id), Is.EqualTo(new[] { "model-a", "model-b" }));
        }
    }

    [Test]
    public async Task Delete_TheDefaultConnection_ClearsTheDefaultAndSwitchesAiOff()
    {
        // Arrange
        var (host, service) = await StartAsync();
        await using var _ = host;
        var created = (await service.CreateConnectionAsync(AiTestData.Admin, AiTestData.Anthropic(enabled: ["model-a"]), CancellationToken.None)).Value;
        await service.SaveAsync(AiTestData.Admin, new AiSettingsInput(true, new AiSelection(created.Id, "model-a", null)), CancellationToken.None);

        // Act
        var deleted = await service.DeleteConnectionAsync(AiTestData.Admin, created.Id, CancellationToken.None);
        var settings = await service.GetAsync(AiTestData.Admin, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted.Succeeded, Is.True);
            Assert.That(settings.Value.Enabled, Is.False);
            Assert.That(settings.Value.Default, Is.Null);
            Assert.That(settings.Value.Connections, Is.Empty);
        }
    }

    [Test]
    public async Task Delete_ANonDefaultConnection_LeavesTheSettingsAlone()
    {
        // Arrange
        var (host, service) = await StartAsync();
        await using var _ = host;
        var keep = (await service.CreateConnectionAsync(AiTestData.Admin, AiTestData.Anthropic("Keep", enabled: ["model-a"]), CancellationToken.None)).Value;
        var drop = (await service.CreateConnectionAsync(AiTestData.Admin, AiTestData.Anthropic("Drop"), CancellationToken.None)).Value;
        await service.SaveAsync(AiTestData.Admin, new AiSettingsInput(true, new AiSelection(keep.Id, "model-a", null)), CancellationToken.None);

        // Act
        await service.DeleteConnectionAsync(AiTestData.Admin, drop.Id, CancellationToken.None);
        var settings = await service.GetAsync(AiTestData.Admin, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(settings.Value.Enabled, Is.True);
            Assert.That(settings.Value.Default?.ConnectionId, Is.EqualTo(keep.Id));
            Assert.That(settings.Value.Connections.Select(c => c.Name), Is.EqualTo(new[] { "Keep" }));
        }
    }

    [Test]
    public async Task Delete_UnknownConnectionOrNonAdmin_Fails()
    {
        // Arrange
        var (host, service) = await StartAsync();
        await using var _ = host;
        var created = (await service.CreateConnectionAsync(AiTestData.Admin, AiTestData.Anthropic(), CancellationToken.None)).Value;

        // Act
        var unknown = await service.DeleteConnectionAsync(AiTestData.Admin, Guid.CreateVersion7(), CancellationToken.None);
        var forbidden = await service.DeleteConnectionAsync(AiTestData.Member, created.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(unknown.Failure?.Code, Is.EqualTo(AiErrorCodes.ConnectionNotFound));
            Assert.That(forbidden.Failure?.Code, Is.EqualTo(AiErrorCodes.Forbidden));
        }
    }
}
