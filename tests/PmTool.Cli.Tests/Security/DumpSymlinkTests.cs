using PmTool.Cli.Import.Monday;

namespace PmTool.Cli.Tests.Security;

[TestFixture]
[Platform(Include = "Linux,MacOsX")]
public sealed class DumpSymlinkTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp() => _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"dump-links-{Guid.NewGuid():N}")).FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    private string NewDump() => Directory.CreateDirectory(Path.Combine(_root, "dump")).FullName;

    private string Outside(string content)
    {
        var path = Path.Combine(Directory.CreateDirectory(Path.Combine(_root, "outside")).FullName, "secret.txt");
        File.WriteAllText(path, content);
        return path;
    }

    [Test]
    public void IsInsideDump_ARegularFile_IsTrue()
    {
        // Arrange
        var dump = NewDump();
        var file = MondayDumpLayout.AssetPath(dump, "a1", "photo.png");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "x");

        // Act & Assert
        Assert.That(MondayDumpLayout.IsInsideDump(dump, file), Is.True);
    }

    [Test]
    public void IsInsideDump_AFileThatIsASymbolicLinkToOutside_IsFalse()
    {
        // Arrange
        var dump = NewDump();
        var file = MondayDumpLayout.AssetPath(dump, "a1", "photo.png");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.CreateSymbolicLink(file, Outside("機密"));

        // Act & Assert
        Assert.That(MondayDumpLayout.IsInsideDump(dump, file), Is.False);
    }

    [Test]
    public void IsInsideDump_AFolderOnThePathThatIsASymbolicLink_IsFalse()
    {
        // Arrange
        var dump = NewDump();
        Outside("機密");
        var linked = Path.Combine(MondayDumpLayout.FilesPath(dump), "a1");
        Directory.CreateDirectory(MondayDumpLayout.FilesPath(dump));
        Directory.CreateSymbolicLink(linked, Path.Combine(_root, "outside"));
        File.Copy(Path.Combine(_root, "outside", "secret.txt"), Path.Combine(_root, "outside", "photo.png"));

        // Act & Assert
        Assert.That(MondayDumpLayout.IsInsideDump(dump, Path.Combine(linked, "photo.png")), Is.False);
    }

    [Test]
    public void IsInsideDump_APathOutsideTheDump_IsFalse()
    {
        // Arrange
        var dump = NewDump();

        // Act & Assert
        Assert.That(MondayDumpLayout.IsInsideDump(dump, Outside("x")), Is.False);
    }
}
