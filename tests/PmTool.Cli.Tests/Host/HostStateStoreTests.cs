using PmTool.Cli.Host;

namespace PmTool.Cli.Tests.Host;

[TestFixture]
public sealed class HostStateStoreTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Path.Combine(Path.GetTempPath(), $"kumi-host-state-{Guid.NewGuid():N}", "nested");

    [TearDown]
    public void TearDown()
    {
        var root = Path.GetDirectoryName(_directory)!;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Load_WithoutAFile_IsAnEmptyUnpairedState()
    {
        // Act
        var state = new HostStateStore(_directory).Load();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(state.IsPaired, Is.False);
            Assert.That(state.Logins, Is.Empty);
        }
    }

    [Test]
    public void Update_WritesTheStateAndItRoundTripsWithTheLogins()
    {
        // Arrange
        var store = new HostStateStore(_directory);
        var loginId = Guid.NewGuid();

        // Act
        store.Update(state => (state with { Server = "https://pm.example.com", AgentId = Guid.NewGuid(), Credential = "kha_x" })
            .WithLogin("claude_code", new HostLoginRecord(loginId, "ABC")));
        var loaded = new HostStateStore(_directory).Load();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(loaded.IsPaired, Is.True);
            Assert.That(loaded.Logins["claude_code"], Is.EqualTo(new HostLoginRecord(loginId, "ABC")));
        }
    }

    [Test]
    [Platform(Include = "Linux,MacOsX")]
    public void Update_KeepsTheFileOwnerOnlyAndTheFolderOwnerOnly()
    {
        // Act
        var store = new HostStateStore(_directory);
        store.Update(state => state with { Credential = "kha_x" });

        // Assert
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.GetUnixFileMode(store.Path), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            Assert.That(
                File.GetUnixFileMode(_directory), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));
        }
    }

    [Test]
    public void EnsureMachineKey_IsGeneratedOnceAndKeptWhenPairingChanges()
    {
        // Arrange
        var store = new HostStateStore(_directory);

        // Act
        var first = store.EnsureMachineKey();
        store.Update(state => state with { Credential = "kha_new" });
        var second = store.EnsureMachineKey();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Does.Match("^[A-Za-z0-9-]{8,64}$"));
            Assert.That(second, Is.EqualTo(first));
        }
    }

    [Test]
    public void Load_WhenTheFileIsBroken_SaysSoWithoutPrintingItsContent()
    {
        // Arrange
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, HostStateStore.FileName), "{ kha_secret_in_broken_json");

        // Act
        var exception = Assert.Throws<CliException>(() => new HostStateStore(_directory).Load())!;

        // Assert
        Assert.That(exception.Message, Does.Contain("讀不懂").And.Not.Contain("kha_secret"));
    }

    [Test]
    public void ToString_HidesTheCredential()
    {
        // Act
        var text = new HostState { Server = "https://pm.example.com", Credential = "kha_super_secret" }.ToString();

        // Assert
        Assert.That(text, Does.Not.Contain("kha_super_secret"));
    }

    [Test]
    public void FromEnvironment_UsesTheConfiguredFolderElseTheDefaultUnderTheHome()
    {
        // Act
        var configured = HostStateStore.FromEnvironment(name => name == HostStateStore.DirectoryVariable ? _directory : null);
        var fallback = HostStateStore.FromEnvironment(_ => null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(configured.Path, Is.EqualTo(Path.Combine(_directory, "host-agent.json")));
            Assert.That(fallback.Path, Does.EndWith(Path.Combine(".local", "state", "kumi-host", "host-agent.json")));
        }
    }
}
