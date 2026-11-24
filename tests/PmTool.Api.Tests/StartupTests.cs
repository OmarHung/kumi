using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>開始接受請求之前要做完的事：各模組的初始化（遷移資料表），然後建立第一個擁有者。</summary>
[TestFixture]
public sealed class StartupTests
{
    private const string BootstrapPassword = "bootstrap-secret-4f1a";

    private static readonly Dictionary<string, string?> FirstOwnerSettings = new()
    {
        ["Bootstrap:OrganizationName"] = "Example 組織",
        ["Bootstrap:AdminName"] = "Admin Example",
        ["Bootstrap:AdminEmail"] = "admin@example.com",
        ["Bootstrap:AdminPassword"] = BootstrapPassword,
    };

    [Test]
    public async Task Startup_RunsEveryInitializerInRegistrationOrderBeforeServing()
    {
        // Arrange
        var log = new ConcurrentQueue<string>();
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Initializers.Add(new RecordingInitializer("identity", log));
        factory.Modules.Initializers.Add(new RecordingInitializer("work", log));

        // Act
        using var client = factory.CreateClient();
        var ranBeforeFirstRequest = log.ToArray();
        using var response = await client.GetAsync("/health");

        // Assert
        Assert.That(ranBeforeFirstRequest, Is.EqualTo(new[] { "identity", "work" }));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public void Startup_AnInitializerFails_StartupFails()
    {
        // Arrange
        var log = new ConcurrentQueue<string>();
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Initializers.Add(new RecordingInitializer("identity", log, new InvalidOperationException("migration-failed-91c2")));
        factory.Modules.Initializers.Add(new RecordingInitializer("work", log));

        // Act
        TestDelegate start = () => factory.CreateClient().Dispose();

        // Assert
        Assert.That(start, Throws.InvalidOperationException.With.Message.Contains("migration-failed-91c2"));
        Assert.That(log, Is.EqualTo(new[] { "identity" }), "失敗之後不再往下做");
    }

    [Test]
    public void Startup_ScopedInitializer_IsResolvedInAScopeThatIsDisposedAfterwards()
    {
        // Arrange：模組的初始化多半要用 DbContext，是 scoped 的；開發環境會檢查不能從根容器解析 scoped 服務。
        var tracker = new DisposalTracker();
        var options = new ApiFactoryOptions
        {
            Environment = "Development",
            ConfigureServices = services =>
            {
                services.AddSingleton(tracker);
                services.AddScoped<IModuleInitializer, ScopedInitializer>();
            },
        };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), options);

        // Act
        factory.CreateClient().Dispose();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tracker.Initialized, Is.EqualTo(1));
            Assert.That(tracker.Disposed, Is.EqualTo(1));
        }
    }

    [Test]
    public void Startup_FirstOwnerConfigured_EnsuresItAfterTheInitializers()
    {
        // Arrange
        var log = new ConcurrentQueue<string>();
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { Settings = FirstOwnerSettings });
        factory.Modules.Initializers.Add(new RecordingInitializer("identity", log));
        factory.Modules.Bootstrapper.Log = log;

        // Act
        factory.CreateClient().Dispose();

        // Assert
        Assert.That(factory.Modules.Bootstrapper.Calls, Is.EqualTo(new[]
        {
            new FirstOwner("Example 組織", "Admin Example", "admin@example.com", BootstrapPassword),
        }));
        Assert.That(log, Is.EqualTo(new[] { "identity", "bootstrap" }), "資料表要先遷移好才能建立帳號");
    }

    [TestCase("Bootstrap:OrganizationName")]
    [TestCase("Bootstrap:AdminName")]
    [TestCase("Bootstrap:AdminEmail")]
    [TestCase("Bootstrap:AdminPassword")]
    public void Startup_FirstOwnerMissingOneValue_DoesNotCreateIt(string missing)
    {
        // Arrange
        var settings = new Dictionary<string, string?>(FirstOwnerSettings) { [missing] = "  " };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { Settings = settings });

        // Act
        factory.CreateClient().Dispose();

        // Assert
        Assert.That(factory.Modules.Bootstrapper.Calls, Is.Empty);
    }

    [Test]
    public void Startup_NoFirstOwnerConfigured_DoesNotCreateIt()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());

        // Act
        factory.CreateClient().Dispose();

        // Assert
        Assert.That(factory.Modules.Bootstrapper.Calls, Is.Empty);
    }

    [Test]
    public void Startup_BootstrapperRejectsTheSettings_StartupFails()
    {
        // Arrange：例如密碼太短。那是部署設定寫錯了，讓它起不來，不要帶著沒有人能登入的狀態上線。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { Settings = FirstOwnerSettings });
        factory.Modules.Bootstrapper.Failure = new ArgumentException("password-too-short-77b0");

        // Act
        TestDelegate start = () => factory.CreateClient().Dispose();

        // Assert
        Assert.That(start, Throws.ArgumentException.With.Message.Contains("password-too-short-77b0"));
    }

    [Test]
    public void Startup_FirstOwnerConfiguredButNoIdentityModule_StartupFails()
    {
        // Arrange：設定了卻沒有人能建立帳號，默默略過的話，部署的人會以為已經可以登入。
        var options = new ApiFactoryOptions
        {
            Settings = FirstOwnerSettings,
            ConfigureServices = services => services.RemoveAll<IIdentityBootstrapper>(),
        };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), options);

        // Act
        TestDelegate start = () => factory.CreateClient().Dispose();

        // Assert
        Assert.That(start, Throws.InvalidOperationException);
    }

    [Test]
    public void Startup_FirstOwnerConfigured_NeverWritesThePasswordToTheLog()
    {
        // Arrange
        var logs = new CapturingLoggerProvider();
        var options = new ApiFactoryOptions
        {
            Settings = FirstOwnerSettings,
            ConfigureServices = services => services.AddSingleton<ILoggerProvider>(logs),
        };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), options);

        // Act
        factory.CreateClient().Dispose();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(logs.Lines, Has.Some.Contains("擁有者"), "要記下有沒有建立");
            Assert.That(logs.Lines, Has.None.Contains(BootstrapPassword));
        }
    }

    private sealed class DisposalTracker
    {
        private int _initialized;
        private int _disposed;

        public int Initialized => Volatile.Read(ref _initialized);

        public int Disposed => Volatile.Read(ref _disposed);

        public void MarkInitialized() => Interlocked.Increment(ref _initialized);

        public void MarkDisposed() => Interlocked.Increment(ref _disposed);
    }

    private sealed class ScopedInitializer(DisposalTracker tracker) : IModuleInitializer, IAsyncDisposable
    {
        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            tracker.MarkInitialized();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            tracker.MarkDisposed();
            return ValueTask.CompletedTask;
        }
    }
}
