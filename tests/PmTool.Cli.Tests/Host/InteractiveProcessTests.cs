using PmTool.Cli.Agent;
using PmTool.Cli.Host;
using PmTool.Cli.Tests.Agent;

namespace PmTool.Cli.Tests.Host;

[TestFixture]
[Platform(Include = "Linux,MacOsX")]
[NonParallelizable]
public sealed class InteractiveProcessTests
{
    private const string SecretName = "KUMI_TEST_INTERACTIVE_SECRET";
    private FakeEngineScripts _scripts = null!;

    [SetUp]
    public void SetUp()
    {
        _scripts = new FakeEngineScripts();
        Environment.SetEnvironmentVariable(SecretName, "secret");
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(SecretName, null);
        _scripts.Dispose();
    }

    private string Recorder(string name) => _scripts.Script(name, $$"""
        pwd > "{{_scripts.Out}}/cwd.txt"
        ls -ld . | cut -c1-10 > "{{_scripts.Out}}/cwd-mode.txt"
        ls -A | wc -l | tr -d ' ' > "{{_scripts.Out}}/cwd-count.txt"
        env | sort > "{{_scripts.Out}}/env.txt"
        exit 3
        """);

    [Test]
    public async Task Run_UsesAFreshPrivateEmptyDirectoryAndDeletesItAfterwards()
    {
        // Arrange
        var script = Recorder("engine");

        // Act
        var exit = await InteractiveProcess.RunAsync(script, ["mcp", "login", "kumi"], CancellationToken.None);

        // Assert
        var directory = _scripts.Read("cwd.txt").Trim();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exit, Is.EqualTo(3));
            Assert.That(_scripts.Read("cwd-mode.txt").Trim(), Is.EqualTo("drwx------"));
            Assert.That(_scripts.Read("cwd-count.txt").Trim(), Is.EqualTo("0"), "共用暫存資料夾裡別人放的設定檔影響不到");
            Assert.That(Path.GetFileName(directory), Does.StartWith("kumi-interactive-"));
            Assert.That(Directory.Exists(directory), Is.False, "用完就刪");
        }
    }

    [Test]
    public async Task Run_GivesTheEngineOnlyAllowedVariables()
    {
        // Arrange
        var script = Recorder("engine");

        // Act
        await InteractiveProcess.RunAsync(script, [], CancellationToken.None);

        // Assert
        var names = _scripts.Read("env.txt").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split('=')[0]).ToHashSet();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(names, Does.Not.Contain(SecretName));
            Assert.That(names, Does.Contain("PATH").And.Contain("HOME"));
        }
    }

    [Test]
    public async Task Run_WhenTheEngineCannotStart_ReturnsNullAndStillCleansUp()
    {
        // Act
        var exit = await InteractiveProcess.RunAsync(Path.Combine(_scripts.Directory, "missing"), [], CancellationToken.None);

        // Assert
        Assert.That(exit, Is.Null);
    }
}
