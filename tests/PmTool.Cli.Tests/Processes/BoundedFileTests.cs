using PmTool.Cli.Agent;
using PmTool.Cli.Processes;

namespace PmTool.Cli.Tests.Processes;

[TestFixture]
[Platform(Include = "Linux,MacOsX")]
public sealed class BoundedFileTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("kumi-bounded-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    private string Path(string name) => System.IO.Path.Combine(_directory, name);

    [Test]
    public void TryReadBytes_ARegularFile_IsRead() =>
        Assert.That(WriteThenRead("a.json", "{\"x\":1}"), Is.EqualTo("{\"x\":1}"));

    private string? WriteThenRead(string name, string content)
    {
        File.WriteAllText(Path(name), content);
        return BoundedFile.TryReadText(Path(name));
    }

    [Test]
    public void TryReadBytes_ASymlink_IsNotFollowedAndIsLogged()
    {
        // Arrange
        File.WriteAllText(Path("real.json"), "{}");
        File.CreateSymbolicLink(Path("auth.json"), Path("real.json"));
        var logged = new List<string>();

        // Act
        var result = BoundedFile.TryReadBytes(Path("auth.json"), logged.Add);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Null);
            Assert.That(logged, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void TryReadBytes_AFileOverOneMiB_IsRefused()
    {
        // Arrange
        File.WriteAllBytes(Path("big.json"), new byte[BoundedFile.MaxBytes + 1]);

        // Act
        var result = BoundedFile.TryReadBytes(Path("big.json"));

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryReadBytes_AFileExactlyAtTheLimit_IsRead()
    {
        // Arrange
        File.WriteAllBytes(Path("max.json"), new byte[BoundedFile.MaxBytes]);

        // Act
        var result = BoundedFile.TryReadBytes(Path("max.json"));

        // Assert
        Assert.That(result, Has.Length.EqualTo(BoundedFile.MaxBytes));
    }

    [Test]
    public void TryReadBytes_MissingFileOrDirectory_IsNullWithoutThrowing()
    {
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(BoundedFile.TryReadBytes(Path("nope.json")), Is.Null);
            Assert.That(BoundedFile.TryReadBytes(_directory), Is.Null);
        }
    }

    [Test]
    public void CodexModels_WhenTheCacheIsASymlinkOrTooBig_FallsBackToTheDefaultModelWithoutThrowing()
    {
        // Arrange（CODEX_HOME 由環境變數給，不寫死路徑）
        string? Home(string name) => name == "CODEX_HOME" ? _directory : null;
        File.WriteAllText(Path("real.json"), """{"models":[{"slug":"gpt-x","visibility":"list","priority":1}]}""");
        File.CreateSymbolicLink(Path("models_cache.json"), Path("real.json"));
        var logged = new List<string>();

        // Act
        var viaLink = CodexModels.Read(Home, logged.Add);
        File.Delete(Path("models_cache.json"));
        File.WriteAllBytes(Path("models_cache.json"), new byte[BoundedFile.MaxBytes + 1]);
        var tooBig = CodexModels.Read(Home, logged.Add);
        File.Delete(Path("models_cache.json"));
        File.Copy(Path("real.json"), Path("models_cache.json"));
        var regular = CodexModels.Read(Home, logged.Add);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(viaLink, Has.Count.EqualTo(1));
            Assert.That(tooBig, Has.Count.EqualTo(1));
            Assert.That(regular.Select(model => model.Id), Does.Contain("gpt-x"));
            Assert.That(logged, Has.Count.EqualTo(2));
        }
    }
}
