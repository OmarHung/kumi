using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Blueprint;
using PmTool.Storage;

namespace PmTool.Work.Tests.Attachments;

/// <summary>啟動時的附件儲存檢查：沒設定時只在被問到才失敗（migrate 命令照跑），設定好了就過。</summary>
[TestFixture]
public sealed class FileStoreSettingsTests
{
    [Test]
    public async Task VerifyAttachmentStorageAsync_WithoutSettings_FailsOnlyWhenAskedSoTheMigrateCommandStillRuns()
    {
        // Arrange：沒給附件設定也能建出整個容器（migrate 命令不碰附件）。
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IBlueprintValidator>(BlueprintValidator.CreateDefault());
        services.AddSingleton<IBlueprintPlanner, BlueprintPlanner>();
        services.AddSingleton<IOrganizationClock, FixedOrganizationClock>();
        services.AddSingleton<IOrganizationMembers, FakeOrganizationMembers>();
        services.AddWorkModule(new DatabaseConnections("Host=db.invalid;Database=unused;Username=unused"));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        // Act / Assert
        var failure = Assert.ThrowsAsync<InvalidOperationException>(() => provider.VerifyAttachmentStorageAsync(CancellationToken.None));
        Assert.That(failure.Message, Does.Contain("Attachments:Provider"));
    }

    [Test]
    public async Task VerifyAttachmentStorageAsync_WithAWritableDirectory_Passes()
    {
        // Arrange
        var directory = Path.Combine(Path.GetTempPath(), "pmtool-settings-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IBlueprintValidator>(BlueprintValidator.CreateDefault());
        services.AddSingleton<IBlueprintPlanner, BlueprintPlanner>();
        services.AddSingleton<IOrganizationClock, FixedOrganizationClock>();
        services.AddSingleton<IOrganizationMembers, FakeOrganizationMembers>();
        services.AddWorkModule("Host=db.invalid;Database=unused;Username=unused", new FileStoreSettings("local", directory));
        await using var provider = services.BuildServiceProvider();

        try
        {
            // Act / Assert
            Assert.That(async () => await provider.VerifyAttachmentStorageAsync(CancellationToken.None), Throws.Nothing);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
