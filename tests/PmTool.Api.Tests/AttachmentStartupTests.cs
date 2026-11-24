using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

/// <summary>附件的儲存設定缺了或不能用時，服務啟動就失敗並說明，不等到第一次上傳。</summary>
[TestFixture]
public sealed class AttachmentStartupTests
{
    [Test]
    public void Startup_WithoutAProvider_FailsAndNamesTheSetting()
    {
        // Arrange
        using var factory = new ApiFactory(
            FakeBlueprintValidator.Passing(),
            new ApiFactoryOptions { Settings = new Dictionary<string, string?> { ["Attachments:Provider"] = string.Empty } });

        // Act
        TestDelegate start = () => factory.CreateClient().Dispose();

        // Assert
        Assert.That(start, Throws.InvalidOperationException.With.Message.Contains("Attachments:Provider"));
    }

    [Test]
    public void Startup_LocalWithoutADirectory_FailsAndNamesTheSetting()
    {
        // Arrange
        using var factory = new ApiFactory(
            FakeBlueprintValidator.Passing(),
            new ApiFactoryOptions { Settings = new Dictionary<string, string?> { ["Attachments:Directory"] = string.Empty } });

        // Act
        TestDelegate start = () => factory.CreateClient().Dispose();

        // Assert
        Assert.That(start, Throws.InvalidOperationException.With.Message.Contains("Attachments:Directory"));
    }

    [Test]
    public void Startup_ADirectoryThatDoesNotExist_FailsAndShowsThePath()
    {
        // Arrange
        var missing = Path.Combine(Path.GetTempPath(), "pmtool-missing", Guid.NewGuid().ToString("N"));
        using var factory = new ApiFactory(
            FakeBlueprintValidator.Passing(),
            new ApiFactoryOptions { Settings = new Dictionary<string, string?> { ["Attachments:Directory"] = missing } });

        // Act
        TestDelegate start = () => factory.CreateClient().Dispose();

        // Assert
        Assert.That(start, Throws.InvalidOperationException.With.Message.Contains("Attachments:Directory").And.Message.Contains(missing));
    }

    [Test]
    public void Startup_AReadOnlyDirectory_FailsBecauseItCannotBeWrittenTo()
    {
        // Arrange
        Assume.That(OperatingSystem.IsWindows(), Is.False);
        Assume.That(Environment.UserName, Is.Not.EqualTo("root"));
        var directory = ApiFactory.CreateTemporaryDirectory();
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }

        try
        {
            using var factory = new ApiFactory(
                FakeBlueprintValidator.Passing(),
                new ApiFactoryOptions { Settings = new Dictionary<string, string?> { ["Attachments:Directory"] = directory } });

            // Act
            TestDelegate start = () => factory.CreateClient().Dispose();

            // Assert
            Assert.That(start, Throws.InvalidOperationException.With.Message.Contains("寫不進去"));
        }
        finally
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void Startup_AnUnknownProvider_FailsAndShowsTheValue()
    {
        // Arrange
        using var factory = new ApiFactory(
            FakeBlueprintValidator.Passing(),
            new ApiFactoryOptions { Settings = new Dictionary<string, string?> { ["Attachments:Provider"] = "s3" } });

        // Act
        TestDelegate start = () => factory.CreateClient().Dispose();

        // Assert
        Assert.That(start, Throws.InvalidOperationException.With.Message.Contains("s3"));
    }

    [Test]
    public void Startup_GcsWithoutABucket_FailsAndNamesTheSetting()
    {
        // Arrange
        using var factory = new ApiFactory(
            FakeBlueprintValidator.Passing(),
            new ApiFactoryOptions { Settings = new Dictionary<string, string?> { ["Attachments:Provider"] = "gcs" } });

        // Act
        TestDelegate start = () => factory.CreateClient().Dispose();

        // Assert
        Assert.That(start, Throws.InvalidOperationException.With.Message.Contains("Attachments:Bucket"));
    }

    [Test]
    public void Startup_ARelativeDirectory_IsResolvedAgainstTheContentRootNotTheWorkingDirectory()
    {
        // Arrange：content root 是 src/PmTool.Api，所以 "../不存在的資料夾" 會變成 src/不存在的資料夾；從測試的工作資料夾算不會是這個路徑。
        using var factory = new ApiFactory(
            FakeBlueprintValidator.Passing(),
            new ApiFactoryOptions { Settings = new Dictionary<string, string?> { ["Attachments:Directory"] = "../pmtool-missing-attachments" } });

        // Act
        TestDelegate start = () => factory.CreateClient().Dispose();

        // Assert
        Assert.That(
            start,
            Throws.InvalidOperationException.With.Message.Contains(Path.Combine("src", "pmtool-missing-attachments")));
    }
}
