using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using PmTool.Api.Cli;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests.Cli;

/// <summary>執行檔目錄：檔名規則、只留最新版、雜湊與大小，以及沒有提供下載的情形。</summary>
[TestFixture]
public sealed class CliPackageCatalogTests
{
    private string _directory = string.Empty;
    private CapturingLoggerProvider _logs = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = ApiFactory.CreateTemporaryDirectory();
        _logs = new CapturingLoggerProvider();
    }

    [TearDown]
    public void TearDown()
    {
        _logs.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public void Catalog_FolderDoesNotExist_ProvidesNothing()
    {
        // Arrange
        var catalog = Create(Path.Combine(_directory, "missing"));

        // Act / Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(catalog.Version, Is.Null);
            Assert.That(catalog.Packages, Is.Empty);
            Assert.That(catalog.Find("macos-arm64"), Is.Null);
        }
    }

    [Test]
    public void Catalog_EmptyFolder_ProvidesNothing()
    {
        // Arrange / Act
        var catalog = Create(_directory);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(catalog.Version, Is.Null);
            Assert.That(catalog.Packages, Is.Empty);
        }
    }

    [Test]
    public void Catalog_AllThreePlatforms_AreListedInFixedOrderWithSizeAndHash()
    {
        // Arrange
        Write("kumi-0.3.0-windows-x64.exe", "win");
        Write("kumi-0.3.0-macos-x64", "intel");
        Write("kumi-0.3.0-macos-arm64", "apple");

        // Act
        var catalog = Create(_directory);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(catalog.Version, Is.EqualTo("0.3.0"));
            Assert.That(catalog.Packages.Select(p => p.Platform), Is.EqualTo(new[] { "macos-arm64", "macos-x64", "windows-x64" }));
            Assert.That(catalog.Packages.Select(p => p.FileName), Is.EqualTo(
                new[] { "kumi-0.3.0-macos-arm64", "kumi-0.3.0-macos-x64", "kumi-0.3.0-windows-x64.exe" }));
            Assert.That(catalog.Find("macos-arm64")!.SizeBytes, Is.EqualTo(5));
            Assert.That(catalog.Find("macos-arm64")!.Sha256, Is.EqualTo(Sha256Of("apple")));
            Assert.That(catalog.Find("windows-x64")!.Sha256, Is.EqualTo(Sha256Of("win")));
            Assert.That(catalog.Find("windows-x64")!.FilePath, Is.EqualTo(Path.Combine(_directory, "kumi-0.3.0-windows-x64.exe")));
        }
    }

    [Test]
    public void Catalog_OnlySomePlatforms_ListsOnlyThose()
    {
        // Arrange
        Write("kumi-1.0.0-macos-arm64", "a");

        // Act
        var catalog = Create(_directory);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(catalog.Packages.Select(p => p.Platform), Is.EqualTo(new[] { "macos-arm64" }));
            Assert.That(catalog.Find("windows-x64"), Is.Null);
        }
    }

    [Test]
    public void Catalog_SeveralVersions_KeepsTheNewestByNumberAndWarnsAboutTheOthers()
    {
        // Arrange：照數字比，0.10.0 比 0.9.0 新（字串比會相反）。
        Write("kumi-0.9.0-macos-arm64", "old");
        Write("kumi-0.10.0-macos-arm64", "new");
        Write("kumi-0.9.0-macos-x64", "old-x64");

        // Act
        var catalog = Create(_directory);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(catalog.Version, Is.EqualTo("0.10.0"));
            Assert.That(catalog.Packages.Select(p => p.FileName), Is.EqualTo(new[] { "kumi-0.10.0-macos-arm64" }));
            Assert.That(WarningLines().Count(line => line.Contains("kumi-0.9.0")), Is.EqualTo(2));
        }
    }

    [TestCase("kumi-0.3.0-windows-x64")]
    [TestCase("kumi-0.3.0-macos-arm64.exe")]
    [TestCase("kumi-0.3-macos-arm64")]
    [TestCase("kumi-0.3.0-linux-x64")]
    [TestCase("Kumi-0.3.0-macos-arm64")]
    [TestCase("kumi-0.3.0-macos-arm64.zip")]
    [TestCase("readme.txt")]
    public void Catalog_FileNameBreaksTheRules_IsSkippedWithAWarning(string fileName)
    {
        // Arrange
        Write(fileName, "x");
        Write("kumi-0.3.0-macos-x64", "ok");

        // Act
        var catalog = Create(_directory);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(catalog.Packages.Select(p => p.Platform), Is.EqualTo(new[] { "macos-x64" }));
            Assert.That(WarningLines().Any(line => line.Contains(fileName)), Is.True);
        }
    }

    [Test]
    public void Catalog_OnlyBadFiles_ProvidesNothing()
    {
        // Arrange
        Write("kumi-0.3.0-windows-x64", "x");

        // Act
        var catalog = Create(_directory);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(catalog.Version, Is.Null);
            Assert.That(catalog.Packages, Is.Empty);
        }
    }

    [Test]
    public void Find_PlatformIsNotAKnownCode_ReturnsNull()
    {
        // Arrange
        Write("kumi-0.3.0-macos-arm64", "a");
        var catalog = Create(_directory);

        // Act / Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(catalog.Find("../kumi-0.3.0-macos-arm64"), Is.Null);
            Assert.That(catalog.Find("MACOS-ARM64"), Is.Null);
            Assert.That(catalog.Find(string.Empty), Is.Null);
        }
    }

    [Test]
    public void Catalog_FilesChangeAfterTheFirstRead_AreNotReadAgain()
    {
        // Arrange
        Write("kumi-0.3.0-macos-arm64", "a");
        var catalog = Create(_directory);
        _ = catalog.Version;

        // Act
        Write("kumi-0.4.0-macos-arm64", "b");

        // Assert
        Assert.That(catalog.Version, Is.EqualTo("0.3.0"));
    }

    private CliPackageCatalog Create(string directory) =>
        new(directory, new Logger<CliPackageCatalog>(new LoggerFactory([_logs])));

    private void Write(string fileName, string content) =>
        File.WriteAllText(Path.Combine(_directory, fileName), content, new UTF8Encoding(false));

    private IEnumerable<string> WarningLines() => _logs.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message);

    internal static string Sha256Of(string content) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
