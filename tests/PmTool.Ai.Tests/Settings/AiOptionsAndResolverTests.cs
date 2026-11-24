using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Providers;

namespace PmTool.Ai.Tests.Settings;

/// <summary>成員能選的 AI，與助理送訊息前檢查選擇的 <see cref="AiSelectionResolver"/>。</summary>
[TestFixture]
public sealed class AiOptionsAndResolverTests
{
    private static async Task<(AiTestHost Host, AiConnection Connection)> StartConfiguredAsync(bool enabled = true)
    {
        var host = await AiTestHost.StartAsync();
        var service = host.Get<IAiSettingsService>();
        var connection = (await service.CreateConnectionAsync(
            AiTestData.Admin, AiTestData.Anthropic(enabled: ["model-a"]), CancellationToken.None)).Value;
        await service.SaveAsync(
            AiTestData.Admin, new AiSettingsInput(enabled, new AiSelection(connection.Id, "model-a", AiEffort.High)), CancellationToken.None);
        return (host, connection);
    }

    [Test]
    public async Task Options_NotConfigured_IsUnavailable()
    {
        // Arrange
        await using var host = await AiTestHost.StartAsync();

        // Act
        var options = await host.Get<IAiOptionsService>().GetAsync(AiTestData.Member, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.Available, Is.False);
            Assert.That(options.Connections, Is.Empty);
            Assert.That(options.Default, Is.Null);
        }
    }

    [Test]
    public async Task Options_Disabled_IsUnavailableEvenWithConnections()
    {
        // Arrange
        var (host, _) = await StartConfiguredAsync(enabled: false);
        await using var _host = host;

        // Act
        var options = await host.Get<IAiOptionsService>().GetAsync(AiTestData.Member, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.Available, Is.False);
            Assert.That(options.Connections, Is.Empty);
            Assert.That(options.Default, Is.Null);
        }
    }

    [Test]
    public async Task Options_AnyMember_SeesOnlyEnabledModelsOfConnectionsThatHaveSome()
    {
        // Arrange
        var (host, connection) = await StartConfiguredAsync();
        await using var _ = host;
        await host.Get<IAiSettingsService>().CreateConnectionAsync(AiTestData.Admin, AiTestData.Anthropic("No models enabled"), CancellationToken.None);

        // Act
        var options = await host.Get<IAiOptionsService>().GetAsync(AiTestData.Member, CancellationToken.None);

        // Assert
        var option = options.Connections.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.Available, Is.True);
            Assert.That(option.Id, Is.EqualTo(connection.Id));
            Assert.That(option.Kind, Is.EqualTo(AiProviderKind.Anthropic));
            Assert.That(option.Models.Single().Id, Is.EqualTo("model-a"));
            Assert.That(option.Models.Single().Efforts, Is.EqualTo(new[] { AiEffort.Low, AiEffort.High }));
            Assert.That(option.Models.Single().DefaultEffort, Is.Null, "文件沒寫的模型不猜");
            Assert.That(options.Default, Is.EqualTo(new AiSelection(connection.Id, "model-a", AiEffort.High)));
        }
    }

    [Test]
    public async Task Resolve_ValidSelection_ReturnsTheDecryptedEndpointAndTheModel()
    {
        // Arrange
        var (host, connection) = await StartConfiguredAsync();
        await using var _ = host;

        // Act
        var resolved = await host.Get<AiSelectionResolver>().ResolveAsync(
            AiTestData.Member, new AiSelection(connection.Id, "model-a", AiEffort.Low), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved.Value.Endpoint, Is.EqualTo(new AiEndpoint(AiProviderKind.Anthropic, null, "sk-ant-key-0001")));
            Assert.That(resolved.Value.ConnectionName, Is.EqualTo("Anthropic"));
            Assert.That(resolved.Value.Model.Id, Is.EqualTo("model-a"));
            Assert.That(resolved.Value.Endpoint.ToString(), Does.Not.Contain("sk-ant-key-0001"));
        }
    }

    [Test]
    public async Task Resolve_NullEffort_IsAlwaysFine()
    {
        // Arrange
        var (host, connection) = await StartConfiguredAsync();
        await using var _ = host;

        // Act
        var resolved = await host.Get<AiSelectionResolver>().ResolveAsync(
            AiTestData.Member, new AiSelection(connection.Id, "model-a", null), CancellationToken.None);

        // Assert
        Assert.That(resolved.Succeeded, Is.True);
    }

    [Test]
    public async Task Resolve_AiTurnedOff_IsUnavailable()
    {
        // Arrange
        var (host, connection) = await StartConfiguredAsync(enabled: false);
        await using var _ = host;

        // Act
        var resolved = await host.Get<AiSelectionResolver>().ResolveAsync(
            AiTestData.Member, new AiSelection(connection.Id, "model-a", null), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved.Failure?.Code, Is.EqualTo(AiErrorCodes.Unavailable));
            Assert.That(resolved.Failure?.Kind, Is.EqualTo(FailureKind.Invalid));
        }
    }

    [Test]
    public async Task Resolve_UnknownConnectionDisabledModelOrUnsupportedEffort_AreSelectionInvalidWithAMessage()
    {
        // Arrange
        var (host, connection) = await StartConfiguredAsync();
        await using var _ = host;
        var resolver = host.Get<AiSelectionResolver>();

        // Act
        var connectionGone = await resolver.ResolveAsync(AiTestData.Member, new AiSelection(Guid.CreateVersion7(), "model-a", null), CancellationToken.None);
        var modelOff = await resolver.ResolveAsync(AiTestData.Member, new AiSelection(connection.Id, "model-b", null), CancellationToken.None);
        var effortOff = await resolver.ResolveAsync(AiTestData.Member, new AiSelection(connection.Id, "model-a", AiEffort.Max), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(connectionGone.Failure?.Code, Is.EqualTo(AiErrorCodes.SelectionInvalid));
            Assert.That(modelOff.Failure?.Code, Is.EqualTo(AiErrorCodes.SelectionInvalid));
            Assert.That(modelOff.Failure?.Message, Does.Contain("model-b"));
            Assert.That(effortOff.Failure?.Code, Is.EqualTo(AiErrorCodes.SelectionInvalid));
            Assert.That(effortOff.Failure?.Message, Does.Contain("max"));
        }
    }

    [Test]
    public async Task Resolve_ConnectionOfAnotherOrganization_IsSelectionInvalid()
    {
        // Arrange
        var (host, connection) = await StartConfiguredAsync();
        await using var _ = host;
        var stranger = new Actor(Guid.CreateVersion7(), Guid.CreateVersion7(), IsOrganizationAdmin: false);

        // Act
        var resolved = await host.Get<AiSelectionResolver>().ResolveAsync(stranger, new AiSelection(connection.Id, "model-a", null), CancellationToken.None);

        // Assert
        Assert.That(resolved.Failure?.Code, Is.AnyOf(AiErrorCodes.Unavailable, AiErrorCodes.SelectionInvalid));
    }

    [Test]
    public async Task Resolve_KeyThatCannotBeDecrypted_AsksTheAdminToEnterItAgain()
    {
        // Arrange
        var (host, connection) = await StartConfiguredAsync();
        await using var _ = host;
        await using (var db = await host.CreateDbContextAsync())
        {
            await db.Connections.Where(c => c.Id == connection.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(c => c.ProtectedApiKey, "not-a-protected-payload"));
        }

        // Act
        var resolved = await host.Get<AiSelectionResolver>().ResolveAsync(
            AiTestData.Member, new AiSelection(connection.Id, "model-a", null), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved.Failure?.Code, Is.EqualTo(AiErrorCodes.SelectionInvalid));
            Assert.That(resolved.Failure?.Message, Does.Contain("重新輸入"));
        }
    }
}
