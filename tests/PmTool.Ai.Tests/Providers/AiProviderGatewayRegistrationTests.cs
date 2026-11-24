using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai.Providers;

namespace PmTool.Ai.Tests.Providers;

/// <summary>沒有人先放假的 gateway 時，模組註冊的是真的那個（測試的主機一律先放假的，這裡不用主機）。</summary>
[TestFixture]
public sealed class AiProviderGatewayRegistrationTests
{
    [Test]
    public void AddAiModule_WithoutAFakeGateway_RegistersTheRealOneAsASingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddAiModule("Host=localhost;Database=unused;Username=unused");
        using var provider = services.BuildServiceProvider();

        // Act
        var first = provider.GetRequiredService<IAiProviderGateway>();
        var second = provider.GetRequiredService<IAiProviderGateway>();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.TypeOf<AiProviderGateway>());
            Assert.That(second, Is.SameAs(first));
        }
    }

    [Test]
    public void AddAiModule_AFakeGatewayRegisteredFirst_IsKept()
    {
        // Arrange
        var fake = new Settings.StubAiProviderGateway();
        var services = new ServiceCollection();
        services.AddSingleton<IAiProviderGateway>(fake);
        services.AddAiModule("Host=localhost;Database=unused;Username=unused");
        using var provider = services.BuildServiceProvider();

        // Act & Assert
        Assert.That(provider.GetRequiredService<IAiProviderGateway>(), Is.SameAs(fake));
    }
}
