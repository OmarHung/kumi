using PmTool.Cli.Agent;

namespace PmTool.Cli.Tests.Agent;

[TestFixture]
public sealed class MachineIdentityTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("kumi-identity-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [Test]
    public void LoadOrCreateKey_CreatesOnceAndKeepsReturningTheSameKey()
    {
        // Arrange
        var configPath = Path.Combine(_directory, "kumi", "config.json");

        // Act
        var first = MachineIdentity.LoadOrCreateKey(configPath);
        var second = MachineIdentity.LoadOrCreateKey(configPath);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(second, Is.EqualTo(first));
            Assert.That(first, Does.Match("^[A-Za-z0-9-]{8,64}$"));
            Assert.That(File.Exists(Path.Combine(_directory, "kumi", "agent.json")), Is.True, "和 config.json 同一個資料夾");
        }
    }

    [Test]
    [Platform(Include = "Linux,MacOsX")]
    public void LoadOrCreateKey_StoresTheFileReadableOnlyByTheOwner()
    {
        // Act
        MachineIdentity.LoadOrCreateKey(Path.Combine(_directory, "kumi", "config.json"));

        // Assert
        if (!OperatingSystem.IsWindows())
        {
            Assert.That(File.GetUnixFileMode(Path.Combine(_directory, "kumi", "agent.json")), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
        }
    }

    [TestCase("not json")]
    [TestCase("""{"machineKey":"has spaces!"}""")]
    [TestCase("""{"machineKey":"short"}""")]
    public void LoadOrCreateKey_ReplacesAFileItCannotUse(string content)
    {
        // Arrange
        var configPath = Path.Combine(_directory, "config.json");
        File.WriteAllText(Path.Combine(_directory, "agent.json"), content);

        // Act
        var key = MachineIdentity.LoadOrCreateKey(configPath);

        // Assert
        Assert.That(MachineIdentity.LoadOrCreateKey(configPath), Is.EqualTo(key));
        Assert.That(key, Does.Match("^[A-Za-z0-9-]{8,64}$"));
    }

    [Test]
    public void ResolveName_UsesTheRequestedNameWithoutControlCharactersAndWithinTheLimit()
    {
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(MachineIdentity.ResolveName("  小明的\u0007 Mac "), Is.EqualTo("小明的 Mac"));
            Assert.That(MachineIdentity.ResolveName(new string('x', 100)), Has.Length.EqualTo(60));
            Assert.That(MachineIdentity.ResolveName(null), Is.Not.Empty);
            Assert.That(MachineIdentity.ResolveName("\u0001\u0002"), Is.EqualTo("kumi-computer"));
        }
    }
}
