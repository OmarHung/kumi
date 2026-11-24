using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PmTool.Api.Live;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests.Live;

/// <summary>即時更新在主機裡跑，可以用設定整個關掉；沒設定就是開著。</summary>
[TestFixture]
public sealed class LiveRegistrationTests
{
    private static ApiFactory Factory(params (string Key, string? Value)[] settings) => new(
        FakeBlueprintValidator.Passing(),
        new ApiFactoryOptions { Settings = settings.ToDictionary(pair => pair.Key, pair => pair.Value) });

    private static IReadOnlyList<Type> HostedLiveServices(ApiFactory factory) =>
        [.. factory.Services.GetServices<IHostedService>().Select(service => service.GetType()).Where(type => type.Namespace == typeof(LiveHub).Namespace)];

    [Test]
    public async Task Disabled_HasNoEndpointAndNoBackgroundWork()
    {
        // Arrange
        using var factory = Factory((LiveRegistration.EnabledSetting, "false"));
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(LiveRegistration.Route + "/negotiate", content: null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(HostedLiveServices(factory), Is.Empty);
        Assert.That(factory.Services.GetService<LiveConnections>(), Is.Null);
    }

    [Test]
    public void NotSet_RunsTheUpdateWorkerAndTheRefresherAsHostedServices()
    {
        // Arrange：空白的設定值就是沒設，用預設的（開著）。
        using var factory = Factory((LiveRegistration.EnabledSetting, " "), (LiveUpdateWorker.EnabledSetting, " "));

        // Act & Assert
        Assert.That(HostedLiveServices(factory), Is.EquivalentTo(new[] { typeof(LiveUpdateWorker), typeof(LiveConnectionRefresher) }));
    }

    [Test]
    public void WorkerSettingFalse_KeepsTheHubButNotTheUpdateWorker()
    {
        // Arrange
        using var factory = Factory((LiveRegistration.EnabledSetting, "true"), (LiveUpdateWorker.EnabledSetting, "false"));

        // Act & Assert
        Assert.That(HostedLiveServices(factory), Is.EquivalentTo(new[] { typeof(LiveConnectionRefresher) }));
        Assert.That(factory.Services.GetService<LiveConnections>(), Is.Not.Null);
    }

    [Test]
    public void SettingNotABoolean_FailsTheStartup()
    {
        // Arrange
        using var factory = Factory((LiveRegistration.EnabledSetting, "maybe"));

        // Act & Assert
        Assert.That(() => factory.Services, Throws.InvalidOperationException);
    }
}
