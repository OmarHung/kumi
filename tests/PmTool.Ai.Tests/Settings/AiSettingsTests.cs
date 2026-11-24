using PmTool.Abstractions;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.Tests.Settings;

/// <summary>開關、預設、用量、讀取模型（internal/HANDOFF-ai-assistant.md 第 4.6 節）。</summary>
[TestFixture]
public sealed class AiSettingsTests
{
    private static async Task<(AiTestHost Host, IAiSettingsService Service, AiConnection Connection)> StartWithConnectionAsync()
    {
        var host = await AiTestHost.StartAsync();
        var service = host.Get<IAiSettingsService>();
        var connection = (await service.CreateConnectionAsync(
            AiTestData.Admin, AiTestData.Anthropic(enabled: ["model-a"]), CancellationToken.None)).Value;
        return (host, service, connection);
    }

    [Test]
    public async Task Get_NeverConfigured_IsDisabledWithNoConnectionsAndNoDefault()
    {
        // Arrange
        await using var host = await AiTestHost.StartAsync();

        // Act
        var settings = await host.Get<IAiSettingsService>().GetAsync(AiTestData.Admin, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(settings.Value.Enabled, Is.False);
            Assert.That(settings.Value.Default, Is.Null);
            Assert.That(settings.Value.Connections, Is.Empty);
            Assert.That(settings.Value.UpdatedAt, Is.Null);
        }
    }

    [Test]
    public async Task Get_And_Save_NonAdmin_AreForbidden()
    {
        // Arrange
        await using var host = await AiTestHost.StartAsync();
        var service = host.Get<IAiSettingsService>();

        // Act
        var get = await service.GetAsync(AiTestData.Member, CancellationToken.None);
        var save = await service.SaveAsync(AiTestData.Member, new AiSettingsInput(false, null), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(get.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(get.Failure?.Code, Is.EqualTo(AiErrorCodes.Forbidden));
            Assert.That(save.Failure?.Code, Is.EqualTo(AiErrorCodes.Forbidden));
        }
    }

    [Test]
    public async Task Save_ValidDefault_IsReadBackWithTheUpdater()
    {
        // Arrange
        var (host, service, connection) = await StartWithConnectionAsync();
        await using var _ = host;

        // Act
        var saved = await service.SaveAsync(
            AiTestData.Admin, new AiSettingsInput(true, new AiSelection(connection.Id, "model-a", AiEffort.High)), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved.Value.Enabled, Is.True);
            Assert.That(saved.Value.Default, Is.EqualTo(new AiSelection(connection.Id, "model-a", AiEffort.High)));
            Assert.That(saved.Value.UpdatedBy, Is.EqualTo(AiTestData.Admin.PersonId));
            Assert.That(saved.Value.UpdatedAt, Is.Not.Null);
        }
    }

    [Test]
    public async Task Save_EnabledWithoutDefault_RequiresTheDefault()
    {
        // Arrange
        var (host, service, _) = await StartWithConnectionAsync();
        await using var _host = host;

        // Act
        var saved = await service.SaveAsync(AiTestData.Admin, new AiSettingsInput(true, null), CancellationToken.None);

        // Assert
        var detail = saved.Failure!.Details.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved.Failure.Code, Is.EqualTo(AiErrorCodes.SettingsInvalid));
            Assert.That(detail.Code, Is.EqualTo(AiErrorCodes.FieldRequired));
            Assert.That(detail.Path, Is.EqualTo("default"));
        }
    }

    [Test]
    public async Task Save_DisabledKeepsTheDefaultForNextTime()
    {
        // Arrange
        var (host, service, connection) = await StartWithConnectionAsync();
        await using var _ = host;

        // Act
        var saved = await service.SaveAsync(
            AiTestData.Admin, new AiSettingsInput(false, new AiSelection(connection.Id, "model-a", null)), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved.Value.Enabled, Is.False);
            Assert.That(saved.Value.Default?.Model, Is.EqualTo("model-a"));
        }
    }

    [Test]
    public async Task Save_BadDefault_ReportsEachFieldPath()
    {
        // Arrange
        var (host, service, connection) = await StartWithConnectionAsync();
        await using var _ = host;

        // Act
        var unknownConnection = await service.SaveAsync(
            AiTestData.Admin, new AiSettingsInput(true, new AiSelection(Guid.CreateVersion7(), "model-a", null)), CancellationToken.None);
        var notEnabledModel = await service.SaveAsync(
            AiTestData.Admin, new AiSettingsInput(true, new AiSelection(connection.Id, "model-b", null)), CancellationToken.None);
        var badEffort = await service.SaveAsync(
            AiTestData.Admin, new AiSettingsInput(true, new AiSelection(connection.Id, "model-a", AiEffort.Max)), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(unknownConnection.Failure!.Details.Single().Path, Is.EqualTo("default.connectionId"));
            Assert.That(notEnabledModel.Failure!.Details.Single().Path, Is.EqualTo("default.model"));
            Assert.That(badEffort.Failure!.Details.Single().Path, Is.EqualTo("default.effort"));
            Assert.That(badEffort.Failure.Details.Single().Code, Is.EqualTo(AiErrorCodes.ModelNotAvailable));
        }
    }

    [Test]
    public async Task Get_ConnectionsAreSortedByNameIgnoringCase()
    {
        // Arrange
        await using var host = await AiTestHost.StartAsync();
        var service = host.Get<IAiSettingsService>();
        foreach (var name in new[] { "zeta", "Alpha", "beta" })
        {
            await service.CreateConnectionAsync(AiTestData.Admin, AiTestData.Anthropic(name), CancellationToken.None);
        }

        // Act
        var settings = await service.GetAsync(AiTestData.Admin, CancellationToken.None);

        // Assert
        Assert.That(settings.Value.Connections.Select(c => c.Name), Is.EqualTo(new[] { "Alpha", "beta", "zeta" }));
    }

    [Test]
    public async Task Get_Usage_SumsThisMonthsAssistantRepliesOfEachConnectionInTheOrganizationTimeZone()
    {
        // Arrange
        var (host, service, connection) = await StartWithConnectionAsync();
        await using var _ = host;
        var conversationId = Guid.CreateVersion7();
        await using (var db = await host.CreateDbContextAsync())
        {
            db.Conversations.Add(new AiConversationRow
            {
                Id = conversationId, OrganizationId = AiTestData.OrganizationId, ProjectId = Guid.CreateVersion7(),
                PersonId = AiTestData.Admin.PersonId, MessageCount = 0, CreatedAt = host.Get<TimeProvider>().GetUtcNow(),
                UpdatedAt = host.Get<TimeProvider>().GetUtcNow(),
            });
            await db.SaveChangesAsync();

            // 時鐘是 2026-11-18；台北時間的 11 月從 10-31 16:00 UTC 開始。
            db.Messages.AddRange(
                Reply(conversationId, 1, connection.Id, new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero), 100, 10),
                Reply(conversationId, 2, connection.Id, new DateTimeOffset(2026, 10, 31, 16, 0, 0, TimeSpan.Zero), 200, 20),
                Reply(conversationId, 3, connection.Id, new DateTimeOffset(2026, 10, 31, 15, 59, 0, TimeSpan.Zero), 9999, 9999),
                Reply(conversationId, 4, connection.Id, new DateTimeOffset(2026, 11, 30, 15, 59, 0, TimeSpan.Zero), 300, 30),
                Reply(conversationId, 5, connection.Id, new DateTimeOffset(2026, 11, 30, 16, 0, 0, TimeSpan.Zero), 9999, 9999),
                Reply(conversationId, 6, Guid.CreateVersion7(), new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero), 7777, 7777));
            await db.SaveChangesAsync();
        }

        // Act
        var settings = await service.GetAsync(AiTestData.Admin, CancellationToken.None);

        // Assert
        Assert.That(settings.Value.Connections.Single().Usage, Is.EqualTo(new AiUsage("2026-11", 600, 60, 3)));
    }

    [Test]
    public async Task Get_UsageWithoutReplies_IsZero()
    {
        // Arrange
        var (host, service, _) = await StartWithConnectionAsync();
        await using var _host = host;

        // Act
        var settings = await service.GetAsync(AiTestData.Admin, CancellationToken.None);

        // Assert
        Assert.That(settings.Value.Connections.Single().Usage, Is.EqualTo(new AiUsage("2026-11", 0, 0, 0)));
    }

    [Test]
    public async Task DiscoverModels_WithTheGivenKey_ReturnsTheCatalogWithoutSavingAnything()
    {
        // Arrange
        await using var host = await AiTestHost.StartAsync();
        var service = host.Get<IAiSettingsService>();

        // Act
        var models = await service.DiscoverModelsAsync(
            AiTestData.Admin, new AiDiscoveryInput(AiProviderKind.Openai, null, "sk-openai-0001", null), CancellationToken.None);
        var settings = await service.GetAsync(AiTestData.Admin, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(models.Value.Select(m => m.Id), Is.EqualTo(new[] { "model-a", "model-b" }));
            Assert.That(settings.Value.Connections, Is.Empty);
            Assert.That(host.Gateway.DiscoverCalls.Single().ApiKey, Is.EqualTo("sk-openai-0001"));
        }
    }

    [Test]
    public async Task DiscoverModels_NoKeyButAConnectionId_UsesTheStoredKey()
    {
        // Arrange
        var (host, service, connection) = await StartWithConnectionAsync();
        await using var _ = host;

        // Act
        var models = await service.DiscoverModelsAsync(
            AiTestData.Admin, new AiDiscoveryInput(AiProviderKind.Anthropic, null, null, connection.Id), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(models.Succeeded, Is.True);
            Assert.That(host.Gateway.DiscoverCalls.Last().ApiKey, Is.EqualTo("sk-ant-key-0001"));
        }
    }

    [Test]
    public async Task DiscoverModels_StoredKeyWithAnotherBaseUrl_IsRefusedSoTheKeyCannotBeSentElsewhere()
    {
        // Arrange
        await using var host = await AiTestHost.StartAsync();
        var service = host.Get<IAiSettingsService>();
        var connection = (await service.CreateConnectionAsync(
            AiTestData.Admin, AiTestData.Compatible(apiKey: "local-secret-key"), CancellationToken.None)).Value;

        // Act
        var models = await service.DiscoverModelsAsync(
            AiTestData.Admin,
            new AiDiscoveryInput(AiProviderKind.OpenaiCompatible, "http://evil.example.com/v1", null, connection.Id),
            CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(models.Failure!.Details.Single().Path, Is.EqualTo("apiKey"));
            Assert.That(host.Gateway.DiscoverCalls, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task DiscoverModels_UnknownConnectionId_IsNotFound()
    {
        // Arrange
        await using var host = await AiTestHost.StartAsync();

        // Act
        var models = await host.Get<IAiSettingsService>().DiscoverModelsAsync(
            AiTestData.Admin, new AiDiscoveryInput(AiProviderKind.Anthropic, null, null, Guid.CreateVersion7()), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(models.Failure?.Kind, Is.EqualTo(FailureKind.NotFound));
            Assert.That(models.Failure?.Code, Is.EqualTo(AiErrorCodes.ConnectionNotFound));
        }
    }

    [Test]
    public async Task DiscoverModels_ProviderFails_ReturnsProviderFailed()
    {
        // Arrange
        await using var host = await AiTestHost.StartAsync();
        host.Gateway.OnDiscover = _ => throw new global::PmTool.Ai.Providers.AiProviderException("連不上 ollama.example.com。");

        // Act
        var models = await host.Get<IAiSettingsService>().DiscoverModelsAsync(
            AiTestData.Admin, new AiDiscoveryInput(AiProviderKind.OpenaiCompatible, "http://ollama.example.com/v1", null, null), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(models.Failure?.Code, Is.EqualTo(AiErrorCodes.ProviderFailed));
            Assert.That(models.Failure?.Message, Is.EqualTo("連不上 ollama.example.com。"));
        }
    }

    private static AiMessageRow Reply(Guid conversationId, int sequence, Guid connectionId, DateTimeOffset at, long input, long output) => new()
    {
        Id = Guid.CreateVersion7(), ConversationId = conversationId, OrganizationId = AiTestData.OrganizationId, Sequence = sequence,
        Role = "assistant", Parts = "[]", Transcript = "[]", Status = "complete", ConnectionId = connectionId, ConnectionName = "x",
        Model = "model-a", InputTokens = input, OutputTokens = output, CreatedAt = at,
    };
}
