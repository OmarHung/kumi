namespace PmTool.Cli.Tests;

/// <summary>指令列的解析與說明。用法不對是 2，不連線也不讀設定檔。</summary>
[TestFixture]
public sealed class ParsingAndHelpTests
{
    [TestCase()]
    [TestCase("--help")]
    [TestCase("-h")]
    [TestCase("help")]
    public async Task Overview_ListsEveryCommandAndTheExitCodes(params string[] args)
    {
        // Arrange
        using var cli = new CliHarness();

        // Act
        var result = await cli.RunAsync(args);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success));
            foreach (var command in new[]
            {
                "auth login", "auth status", "auth logout", "project list", "item list", "item view", "item create", "item update",
                "item move", "item comment", "blueprint export", "blueprint validate", "blueprint plan", "blueprint apply",
                "changeset list", "changeset show", "changeset apply", "changeset reject",
            })
            {
                Assert.That(result.Out, Does.Contain(command));
            }

            Assert.That(result.Out, Does.Contain("KUMI_TOKEN").And.Contain("3 有需要人決定"));
        }
    }

    [Test]
    public async Task Version_PrintsTheToolName()
    {
        // Arrange
        using var cli = new CliHarness();

        // Act
        var result = await cli.RunAsync("--version");

        // Assert
        Assert.That(result.Out, Does.StartWith("kumi "));
    }

    [Test]
    public async Task GroupWithoutAnAction_ListsThatGroupsCommands()
    {
        // Arrange
        using var cli = new CliHarness();

        // Act
        var result = await cli.RunAsync("item");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success));
        Assert.That(result.Out, Does.Contain("item list").And.Contain("item comment").And.Not.Contain("auth login"));
    }

    [Test]
    public async Task CommandHelp_ShowsUsageAndTheOptionsIncludingTheGlobalOnes()
    {
        // Arrange
        using var cli = new CliHarness();

        // Act
        var viaFlag = await cli.RunAsync("item", "create", "--help");
        var viaShort = await cli.RunAsync("blueprint", "apply", "-h");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(viaFlag.Out, Does.Contain("用法：kumi item create").And.Contain("--set <欄位=值>").And.Contain("--json").And.Contain("--server"));
            Assert.That(viaShort.Out, Does.Contain("--yes").And.Contain("--summary"));
            Assert.That(cli.Server.Requests, Is.Empty);
        }
    }

    [TestCase("nope")]
    [TestCase("item", "nope")]
    public async Task UnknownCommand_IsAUsageError(params string[] args)
    {
        // Arrange
        using var cli = new CliHarness();

        // Act
        var result = await cli.RunAsync(args);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("kumi --help").Or.Contain("kumi item --help"));
    }

    [Test]
    public async Task UnknownOption_IsAUsageErrorThatShowsTheUsage()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();

        // Act
        var longForm = await cli.RunAsync("project", "list", "--nope");
        var shortForm = await cli.RunAsync("project", "list", "-z");

        // Assert
        Assert.That(longForm.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(longForm.Error, Does.Contain("--nope").And.Contain("用法：kumi project list"));
        Assert.That(shortForm.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(cli.Server.Requests, Is.Empty);
    }

    [Test]
    public async Task Options_AcceptEqualsFormAndShortFormAndStopAtDoubleDash()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.On("GET", "/v1/projects/WEB", new { project = new { key = "WEB" }, blueprint = new { } });
        cli.Server.On("GET", "/v1/projects/WEB/items", new { items = Array.Empty<object>(), nextCursor = (string?)null });
        cli.Server.On("POST", "/v1/items/-weird/comments", new { id = "x" });

        // Act
        var equals = await cli.RunAsync("item", "list", "--project=WEB", "--limit=5");
        var shortForm = await cli.RunAsync("item", "list", "-p", "WEB");
        var afterDashes = await cli.RunAsync("item", "comment", "-m", "留言", "--", "-weird");

        // Assert
        Assert.That(equals.ExitCode, Is.EqualTo(ExitCodes.Success), equals.Error);
        Assert.That(cli.Server.To("GET", "/v1/projects/WEB/items")[0].Query("limit"), Is.EqualTo("5"));
        Assert.That(shortForm.ExitCode, Is.EqualTo(ExitCodes.Success), shortForm.Error);
        Assert.That(afterDashes.ExitCode, Is.EqualTo(ExitCodes.Success), afterDashes.Error);
    }

    [TestCase("item", "list", "--project")]
    [TestCase("item", "list", "--project", "A", "--project", "B")]
    [TestCase("item", "list", "--project", "A", "--mine=yes")]
    [TestCase("item", "view")]
    [TestCase("item", "view", "A-1", "A-2")]
    [TestCase("item", "list", "--project", "A", "extra")]
    public async Task WrongArguments_AreAUsageError(params string[] args)
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();

        // Act
        var result = await cli.RunAsync(args);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid), result.Error);
        Assert.That(cli.Server.Requests, Is.Empty);
    }

    [Test]
    public async Task MissingRequiredOption_NamesItAndDoesNotCallTheServer()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();

        // Act
        var result = await cli.RunAsync("item", "list");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("--project"));
        Assert.That(cli.Server.Requests, Is.Empty);
    }
}
