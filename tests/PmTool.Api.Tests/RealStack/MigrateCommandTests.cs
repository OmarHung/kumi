using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 雲端的部署方式：服務設 Database:MigrateOnStartup=false、只拿到執行的角色；部署時先跑
/// <c>dotnet PmTool.Api.dll migrate</c>（擁有者的連線），成功了才更新服務。
/// 命令直接呼叫 Program 的進入點，參數和容器裡的命令列一樣，設定用 --鍵=值 帶進去。
/// </summary>
[TestFixture]
[NonParallelizable] // 換掉整個程序共用的 Console.Out 來讀記錄：同時跑的別的測試的輸出會混進來。
public sealed class MigrateCommandTests
{
    private const string StartupCategory = "PmTool.Api.Composition.StartupTasks";

    /// <summary>命令開了連接埠、一直跑下去的話，測試在這之後失敗，而不是卡住。</summary>
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(2);

    private static readonly Dictionary<string, string?> Service = new() { ["Database:MigrateOnStartup"] = "false" };

    [Test]
    public async Task Migrate_ThenTheServiceAsTheRuntimeRole_IsReadyAndWorks()
    {
        // Arrange：服務只有執行的角色，連遷移用的連線都沒有。
        var owner = await RealStackDatabase.CreateDatabaseAsync();
        var (role, runtime) = await RealStackDatabase.CreateRuntimeRoleAsync(owner);

        // Act
        var exitCode = await RunMigrateAsync(
            $"--ConnectionStrings:Database={runtime}",
            $"--ConnectionStrings:Migrations={owner}",
            $"--Database:RuntimeRole={role}",
            $"--Bootstrap:OrganizationName={RealStackFactory.OrganizationName}",
            $"--Bootstrap:AdminName={RealStackFactory.OwnerName}",
            $"--Bootstrap:AdminEmail={RealStackFactory.OwnerEmail}",
            $"--Bootstrap:AdminPassword={RealStackFactory.OwnerPassword}");
        using var service = new RealStackFactory(runtime, new RealStackOptions { Settings = Service, UseEphemeralDataProtection = false });
        using var client = service.CreateApiClient();
        var ready = await RealStackSteps.StatusOfAsync(client, "/health/ready");
        await RealStackSteps.SignInAsync(client, RealStackFactory.OwnerEmail, RealStackFactory.OwnerPassword);
        await RealStackSteps.CreateProjectAsync(client, "WEB", "simple_tasks");
        var item = await RealStackSteps.CreateItemAsync(client, "WEB", new { type = "task", title = "範例任務" });

        // Assert：建立第一個擁有者、存金鑰、開專案與項目，全部用執行的角色。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(ready, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(item["key"]?.GetValue<string>(), Is.EqualTo("WEB-1"));
        }
    }

    [Test]
    public async Task Service_BeforeMigrate_IsNotReadyAndLogsWhatToDo()
    {
        // Arrange
        var owner = await RealStackDatabase.CreateDatabaseAsync();
        var (_, runtime) = await RealStackDatabase.CreateRuntimeRoleAsync(owner);
        var logs = new CapturingLoggerProvider();
        var options = new RealStackOptions
        {
            Settings = Service,
            ConfigureServices = services => services.AddSingleton<ILoggerProvider>(logs),
        };

        // Act
        using var service = new RealStackFactory(runtime, options);
        using var client = service.CreateClient();
        var ready = await RealStackSteps.StatusOfAsync(client, "/health/ready");
        var alive = await RealStackSteps.StatusOfAsync(client, "/health");
        var errors = logs.Entries.Where(entry => entry.Category == StartupCategory && entry.Level == LogLevel.Error);

        // Assert：活著（平台不必重啟它），但不接流量，記錄說得出要先跑 migrate。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ready, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(alive, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(errors.Select(entry => entry.Message), Has.Exactly(1).Contains("migrate"));
        }
    }

    [Test]
    public async Task Migrate_DatabaseUnreachable_ExitsWithFailureAndLogsTheError()
    {
        // Arrange
        using var output = new StringWriter();
        var original = Console.Out;

        // Act
        int exitCode;
        Console.SetOut(output);
        try
        {
            exitCode = await RunMigrateAsync("--ConnectionStrings:Database=Host=127.0.0.1;Port=1;Database=none;Username=none;Timeout=2");
        }
        finally
        {
            Console.SetOut(original);
        }

        // Assert：記錄是正式環境的 JSON，非 ASCII 的字會被跳脫，所以看類別與等級。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Not.Zero);
            Assert.That(output.ToString(), Does.Contain("\"LogLevel\":\"Error\"").And.Contain("PmTool.Api.Composition.MigrateCommand"));
        }
    }

    [Test]
    public async Task Migrate_RunTwice_SucceedsBothTimes()
    {
        // Arrange：每次部署都會跑，已經是最新時再跑也不能失敗。
        var owner = await RealStackDatabase.CreateDatabaseAsync();
        var (role, runtime) = await RealStackDatabase.CreateRuntimeRoleAsync(owner);
        string[] settings = [$"--ConnectionStrings:Database={runtime}", $"--ConnectionStrings:Migrations={owner}", $"--Database:RuntimeRole={role}"];

        // Act
        var first = await RunMigrateAsync(settings);
        var second = await RunMigrateAsync(settings);

        // Assert
        Assert.That((first, second), Is.EqualTo((0, 0)));
    }

    /// <summary>和容器裡的 <c>dotnet PmTool.Api.dll migrate --鍵=值</c> 一樣，回傳結束代碼。</summary>
    private static async Task<int> RunMigrateAsync(params string[] settings)
    {
        var entryPoint = typeof(Program).Assembly.EntryPoint ?? throw new AssertionException("PmTool.Api 沒有進入點");
        string[] args = ["migrate", "--environment=Production", .. settings];
        return await Task.Run(() => (int)entryPoint.Invoke(null, [args])!).WaitAsync(CommandTimeout);
    }
}
