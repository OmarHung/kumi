using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PmTool.Api.Composition;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

/// <summary>背景處理在主機裡跑，可以用設定關掉；沒設定就是開著。</summary>
[TestFixture]
public sealed class NotificationWorkerRegistrationTests
{
    private static int WorkerCount(ApiFactory factory) =>
        factory.Services.GetServices<IHostedService>().OfType<NotificationWorker>().Count();

    [Test]
    public void Worker_ExplicitlyDisabled_IsNotRunning()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());

        // Act & Assert：ApiFactory 預設關掉，測試用假的模組。
        Assert.That(WorkerCount(factory), Is.Zero);
    }

    [Test]
    public void Worker_SettingTrue_RunsAsAHostedService()
    {
        // Arrange
        var settings = new Dictionary<string, string?> { [NotificationWorker.EnabledSetting] = "true" };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { Settings = settings });

        // Act & Assert
        Assert.That(WorkerCount(factory), Is.EqualTo(1));
    }

    [Test]
    public void Worker_SettingNotABoolean_FailsTheStartup()
    {
        // Arrange
        var settings = new Dictionary<string, string?> { [NotificationWorker.EnabledSetting] = "maybe" };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { Settings = settings });

        // Act & Assert：設定錯了要在啟動時就知道。
        Assert.That(() => factory.Services, Throws.InvalidOperationException);
    }
}
