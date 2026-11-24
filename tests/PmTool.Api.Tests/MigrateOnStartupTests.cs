using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PmTool.Api.Endpoints;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

/// <summary>
/// Database:MigrateOnStartup=false：雲端一次起好幾個容器，不讓每個容器各自遷移，改由 migrate 命令先做。
/// 啟動時不跑模組的初始化、不建立第一個擁有者；還有沒套用的遷移時記一筆錯誤，就緒檢查（模組自己看）回 503。
/// </summary>
[TestFixture]
public sealed class MigrateOnStartupTests
{
    private const string StartupCategory = "PmTool.Api.Composition.StartupTasks";

    private static readonly Dictionary<string, string?> NoMigrationOnStartup = new()
    {
        ["Database:MigrateOnStartup"] = "false",
        ["Bootstrap:OrganizationName"] = "Example 組織",
        ["Bootstrap:AdminName"] = "Admin Example",
        ["Bootstrap:AdminEmail"] = "admin@example.com",
        ["Bootstrap:AdminPassword"] = "bootstrap-secret-0b7e",
    };

    [Test]
    public void Startup_MigrateOnStartupFalse_RunsNoInitializerAndCreatesNoOwner()
    {
        // Arrange
        var log = new ConcurrentQueue<string>();
        using var factory = CreateFactory(new CapturingLoggerProvider());
        factory.Modules.Initializers.Add(new RecordingInitializer("identity", log));

        // Act
        factory.CreateClient().Dispose();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(log, Is.Empty);
            Assert.That(factory.Modules.Bootstrapper.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task Startup_MigrateOnStartupFalseWithPendingMigrations_LogsAnErrorAndStillStarts()
    {
        // Arrange：舊的容器還在服務，新的版本先起來等 migrate；起不來的話平台會一直重啟它。
        var logs = new CapturingLoggerProvider();
        using var factory = CreateFactory(logs);
        factory.Modules.MigrationStatuses.Add(FakeMigrationStatus.Pending("20261117121707_AddDataProtectionKeys"));
        factory.Modules.MigrationStatuses.Add(FakeMigrationStatus.Pending("20261120000000_Example"));

        // Act
        using var client = factory.CreateClient();
        using var health = await client.GetAsync("/health");
        var errors = logs.Entries.Where(entry => entry.Category == StartupCategory && entry.Level == LogLevel.Error).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(health.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(errors.Single().Message, Does.Contain("20261117121707_AddDataProtectionKeys").And.Contain("20261120000000_Example"));
            Assert.That(errors.Single().Message, Does.Contain("dotnet PmTool.Api.dll migrate"));
        }
    }

    [Test]
    public void Startup_MigrateOnStartupFalseAndUpToDate_LogsNoError()
    {
        // Arrange
        var logs = new CapturingLoggerProvider();
        using var factory = CreateFactory(logs);
        factory.Modules.MigrationStatuses.Add(FakeMigrationStatus.Pending());

        // Act
        factory.CreateClient().Dispose();

        // Assert
        Assert.That(logs.Entries.Where(entry => entry.Level >= LogLevel.Warning && entry.Category == StartupCategory), Is.Empty);
    }

    [Test]
    public void Startup_MigrationStatusCannotBeRead_WarnsAndStillStarts()
    {
        // Arrange：資料庫還連不上。啟動不失敗，交給就緒檢查。
        var logs = new CapturingLoggerProvider();
        using var factory = CreateFactory(logs);
        factory.Modules.MigrationStatuses.Add(new FakeMigrationStatus(() => throw new InvalidOperationException("connection refused")));

        // Act
        factory.CreateClient().Dispose();
        var warnings = logs.Entries.Where(entry => entry.Category == StartupCategory && entry.Level == LogLevel.Warning).ToList();

        // Assert
        Assert.That(warnings.Select(entry => entry.Exception?.Message), Is.EqualTo(new[] { "connection refused" }));
    }

    [Test]
    public void Startup_MigrationStatusNeverAnswers_GivesUpAndStillStarts()
    {
        // Arrange：連線一直等不到回應。啟動時最多等就緒檢查的期限，不會卡住。
        var never = new TaskCompletionSource<IReadOnlyList<string>>();
        var logs = new CapturingLoggerProvider();
        using var factory = CreateFactory(logs, new ReadinessLimits { Timeout = TimeSpan.FromMilliseconds(100) });
        factory.Modules.MigrationStatuses.Add(new FakeMigrationStatus(() => never.Task));

        // Act：假的狀態連取消都不理，啟動照樣要在期限之後往下走。
        factory.CreateClient().Dispose();
        var warnings = logs.Entries.Where(entry => entry.Category == StartupCategory && entry.Level == LogLevel.Warning);

        // Assert
        Assert.That(warnings.Select(entry => entry.Exception), Has.Exactly(1).InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void Startup_MigrateOnStartupDefault_RunsTheInitializers()
    {
        // Arrange：沒設時照舊，本機的 compose 不用改。
        var log = new ConcurrentQueue<string>();
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Initializers.Add(new RecordingInitializer("identity", log));
        factory.Modules.MigrationStatuses.Add(FakeMigrationStatus.Pending("not-asked"));

        // Act
        factory.CreateClient().Dispose();

        // Assert
        Assert.That(log, Is.EqualTo(new[] { "identity" }));
    }

    private static ApiFactory CreateFactory(CapturingLoggerProvider logs, ReadinessLimits? readinessLimits = null) =>
        new(FakeBlueprintValidator.Passing(), new ApiFactoryOptions
        {
            Settings = NoMigrationOnStartup,
            ReadinessLimits = readinessLimits,
            ConfigureServices = services => services.AddSingleton<ILoggerProvider>(logs),
        });
}
