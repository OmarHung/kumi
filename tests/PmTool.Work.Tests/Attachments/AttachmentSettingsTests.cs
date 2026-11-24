using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Blueprint;
using PmTool.Work.Attachments;

namespace PmTool.Work.Tests.Attachments;

/// <summary>附件的儲存設定：缺了或不認得時說得出缺什麼，啟動檢查擋得下來。</summary>
[TestFixture]
public sealed class AttachmentSettingsTests
{
    [Test]
    public void Create_WithoutAProvider_FailsAndNamesTheSetting()
    {
        // Act / Assert
        var failure = Assert.Throws<InvalidOperationException>(() => AttachmentStores.Create(new AttachmentSettings(null)));
        Assert.That(failure.Message, Does.Contain("Attachments:Provider"));
    }

    [Test]
    public void Create_WithAnUnknownProvider_FailsAndShowsTheValue()
    {
        // Act / Assert
        var failure = Assert.Throws<InvalidOperationException>(() => AttachmentStores.Create(new AttachmentSettings("s3")));
        Assert.That(failure.Message, Does.Contain("Attachments:Provider").And.Contain("s3"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Create_LocalWithoutADirectory_FailsAndNamesTheSetting(string? directory)
    {
        // Act / Assert
        var failure = Assert.Throws<InvalidOperationException>(() => AttachmentStores.Create(new AttachmentSettings("local", directory)));
        Assert.That(failure.Message, Does.Contain("Attachments:Directory"));
    }

    [TestCase(null)]
    [TestCase("  ")]
    public void Create_GcsWithoutABucket_FailsAndNamesTheSetting(string? bucket)
    {
        // Act / Assert
        var failure = Assert.Throws<InvalidOperationException>(() => AttachmentStores.Create(new AttachmentSettings("gcs", Bucket: bucket)));
        Assert.That(failure.Message, Does.Contain("Attachments:Bucket"));
    }

    [Test]
    public void Create_ProviderNameIsNotCaseSensitive()
    {
        // Act
        var store = AttachmentStores.Create(new AttachmentSettings(" Local ", Path.GetTempPath()));

        // Assert
        Assert.That(store, Is.TypeOf<LocalAttachmentStore>());
    }

    [Test]
    public async Task VerifyAttachmentStorageAsync_WithoutSettings_FailsOnlyWhenAskedSoTheMigrateCommandStillRuns()
    {
        // Arrange：沒給附件設定也能建出整個容器（migrate 命令不碰附件）。
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IBlueprintValidator>(BlueprintValidator.CreateDefault());
        services.AddSingleton<IBlueprintPlanner, BlueprintPlanner>();
        services.AddSingleton<IOrganizationClock, FixedOrganizationClock>();
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
        services.AddWorkModule("Host=db.invalid;Database=unused;Username=unused", new AttachmentSettings("local", directory));
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
