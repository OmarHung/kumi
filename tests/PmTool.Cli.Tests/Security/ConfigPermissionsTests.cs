using System.Runtime.Versioning;
using PmTool.Cli.Settings;

namespace PmTool.Cli.Tests.Security;

[TestFixture]
[Platform(Include = "Linux,MacOsX")]
[UnsupportedOSPlatform("windows")]
public sealed class ConfigPermissionsTests
{
    private static string Directory(CliHarness cli) => Path.GetDirectoryName(cli.ConfigPath)!;

    [Test]
    public async Task AnyCommand_WithAWorldReadableConfig_TightensItAndWarns()
    {
        // Arrange
        using var cli = new CliHarness();
        new ConfigStore(cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = CliHarness.Token });
        File.SetUnixFileMode(cli.ConfigPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        File.SetUnixFileMode(Directory(cli), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);

        // Act
        var result = await cli.RunAsync("auth", "logout", "--json");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(File.GetUnixFileMode(cli.ConfigPath), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            Assert.That(File.GetUnixFileMode(Directory(cli)), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));
            Assert.That(result.Error, Does.Contain("警告").And.Contain("600").And.Contain("700"));
            Assert.That(result.Error, Does.Not.Contain(CliHarness.Token));
        }
    }

    [Test]
    public async Task AnyCommand_WithAnOwnerOnlyConfig_StaysSilent()
    {
        // Arrange
        using var cli = new CliHarness();
        new ConfigStore(cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = CliHarness.Token });

        // Act
        var result = await cli.RunAsync("auth", "logout", "--json");

        // Assert
        Assert.That(result.Error, Is.Empty);
    }

    [Test]
    public void Repair_AFolderThatIsNotKumisOwn_IsLeftAlone()
    {
        // Arrange
        using var cli = new CliHarness();
        var shared = System.IO.Directory.CreateDirectory(cli.PathOf("shared")).FullName;
        File.SetUnixFileMode(shared, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        var config = Path.Combine(shared, "config.json");
        File.WriteAllText(config, "{}");
        File.SetUnixFileMode(config, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);

        // Act
        var warnings = ConfigPermissions.Repair(config);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(File.GetUnixFileMode(config), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            Assert.That(File.GetUnixFileMode(shared).HasFlag(UnixFileMode.OtherRead), Is.True, "不是 kumi 自己的資料夾不動");
        }
    }

    [Test]
    public void Repair_NoConfigFile_DoesNothing()
    {
        // Arrange
        using var cli = new CliHarness();

        // Act & Assert
        Assert.That(ConfigPermissions.Repair(cli.ConfigPath), Is.Empty);
    }

    [Test]
    public void AgentLock_CreatesItsFilesReadableOnlyByTheOwner()
    {
        // Arrange
        using var cli = new CliHarness();
        new ConfigStore(cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = CliHarness.Token });

        // Act
        using var held = PmTool.Cli.Agent.AgentLock.TryAcquire(cli.ConfigPath, 4242, background: false, "x").Lock;

        // Assert
        var ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(held, Is.Not.Null);
            Assert.That(File.GetUnixFileMode(Path.Combine(Directory(cli), "agent.lock")), Is.EqualTo(ownerOnly));
            Assert.That(File.GetUnixFileMode(Path.Combine(Directory(cli), "agent.lock.json")), Is.EqualTo(ownerOnly));
        }
    }

    [Test]
    public async Task AnyCommand_WithOpenSiblingFiles_TightensThemToo()
    {
        // Arrange
        using var cli = new CliHarness();
        new ConfigStore(cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = CliHarness.Token });
        var open = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        string[] names = ["agent-usage.json", "agent.json", "agent.lock.json"];
        foreach (var name in names)
        {
            var path = Path.Combine(Directory(cli), name);
            await File.WriteAllTextAsync(path, "{}");
            File.SetUnixFileMode(path, open);
        }

        // Act
        await cli.RunAsync("auth", "logout", "--json");

        // Assert
        Assert.That(
            names.Select(name => File.GetUnixFileMode(Path.Combine(Directory(cli), name))),
            Is.All.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
    }
}
