namespace PmTool.Storage.Tests;

/// <summary>儲存設定：缺了或不認得時說得出缺什麼。</summary>
[TestFixture]
public sealed class FileStoresTests
{
    [Test]
    public void Create_WithoutAProvider_FailsAndNamesTheSetting()
    {
        // Act / Assert
        var failure = Assert.Throws<InvalidOperationException>(() => FileStores.Create(new FileStoreSettings(null)));
        Assert.That(failure.Message, Does.Contain("Attachments:Provider"));
    }

    [Test]
    public void Create_WithAnUnknownProvider_FailsAndShowsTheValue()
    {
        // Act / Assert
        var failure = Assert.Throws<InvalidOperationException>(() => FileStores.Create(new FileStoreSettings("s3")));
        Assert.That(failure.Message, Does.Contain("Attachments:Provider").And.Contain("s3"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Create_LocalWithoutADirectory_FailsAndNamesTheSetting(string? directory)
    {
        // Act / Assert
        var failure = Assert.Throws<InvalidOperationException>(() => FileStores.Create(new FileStoreSettings("local", directory)));
        Assert.That(failure.Message, Does.Contain("Attachments:Directory"));
    }

    [TestCase(null)]
    [TestCase("  ")]
    public void Create_GcsWithoutABucket_FailsAndNamesTheSetting(string? bucket)
    {
        // Act / Assert
        var failure = Assert.Throws<InvalidOperationException>(() => FileStores.Create(new FileStoreSettings("gcs", Bucket: bucket)));
        Assert.That(failure.Message, Does.Contain("Attachments:Bucket"));
    }

    [Test]
    public void Create_ProviderNameIsNotCaseSensitive()
    {
        // Act
        var store = FileStores.Create(new FileStoreSettings(" Local ", Path.GetTempPath()));

        // Assert
        Assert.That(store, Is.TypeOf<LocalFileStore>());
    }
}
