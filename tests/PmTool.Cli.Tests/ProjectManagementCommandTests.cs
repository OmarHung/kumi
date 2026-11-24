using System.Net;
using System.Text.Json;

namespace PmTool.Cli.Tests;

/// <summary>專案的改名與封存：送哪個端點、印什麼、失敗時的結束代碼。</summary>
[TestFixture]
public sealed class ProjectManagementCommandTests
{
    private static CliHarness LoggedIn() => new CliHarness().LoggedInByEnvironment();

    private static object Project(string name = "官網改版", string? archivedAt = null) =>
        new { key = "WEB", name, blueprintVersion = 3, myRole = "admin", archivedAt };

    [Test]
    public async Task Rename_PutsTheNameAndPrintsTheServersNewName()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("PUT", "/v1/projects/WEB/name", Project("官網改版（第二期）"));

        // Act
        var result = await cli.RunAsync("project", "rename", "WEB", "--name", "官網改版（第二期）");

        // Assert
        var request = cli.Server.To("PUT", "/v1/projects/WEB/name").Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(request.Json!["name"]!.GetValue<string>(), Is.EqualTo("官網改版（第二期）"));
            Assert.That(request.ContentType, Is.EqualTo("application/json"));
            Assert.That(result.Out.Trim(), Is.EqualTo("已把 WEB 改名為「官網改版（第二期）」。"));
        }
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task Rename_BlankName_ExitsWithUsageErrorAndSendsNothing(string name)
    {
        // Arrange
        using var cli = LoggedIn();

        // Act
        var result = await cli.RunAsync("project", "rename", "WEB", "--name", name);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("--name"));
        Assert.That(cli.Server.Requests, Is.Empty);
    }

    [Test]
    public async Task Rename_MissingName_ExitsWithUsageErrorAndSendsNothing()
    {
        // Arrange
        using var cli = LoggedIn();

        // Act
        var result = await cli.RunAsync("project", "rename", "WEB");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("--name"));
        Assert.That(cli.Server.Requests, Is.Empty);
    }

    [Test]
    public async Task Rename_ServerRejectsTheName_ExitsWith2AndShowsThePath()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.OnError("PUT", "/v1/projects/WEB/name", HttpStatusCode.UnprocessableEntity, "validation.failed", "名稱不可以太長。", ("name", "最多 100 個字"));

        // Act
        var result = await cli.RunAsync("project", "rename", "WEB", "--name", "x");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("名稱不可以太長。").And.Contain("name").And.Contain("最多 100 個字"));
    }

    [Test]
    public async Task Rename_JsonPrintsTheServersProjectAsIs()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("PUT", "/v1/projects/WEB/name", Project("新名稱"));

        // Act
        var result = await cli.RunAsync("project", "rename", "WEB", "--name", "新名稱", "--json");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(JsonDocument.Parse(result.Out).RootElement.GetProperty("name").GetString(), Is.EqualTo("新名稱"));
    }

    [Test]
    public async Task Archive_PostsWithoutABodyAndPrintsAMessage()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("POST", "/v1/projects/WEB/archive", Project(archivedAt: "2026-11-19T02:00:00Z"));

        // Act
        var result = await cli.RunAsync("project", "archive", "WEB");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(cli.Server.To("POST", "/v1/projects/WEB/archive").Single().Body, Is.Empty);
            Assert.That(result.Out.Trim(), Is.EqualTo("已封存 WEB。"));
        }
    }

    [Test]
    public async Task Unarchive_PostsToUnarchiveAndPrintsAMessage()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("POST", "/v1/projects/WEB/unarchive", Project());

        // Act
        var result = await cli.RunAsync("project", "unarchive", "WEB");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(cli.Server.To("POST", "/v1/projects/WEB/unarchive"), Has.Count.EqualTo(1));
            Assert.That(result.Out.Trim(), Is.EqualTo("已取消封存 WEB。"));
        }
    }

    [TestCase("archive")]
    [TestCase("unarchive")]
    public async Task ArchiveAndUnarchive_JsonPrintsTheServersProjectAsIs(string verb)
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("POST", $"/v1/projects/WEB/{verb}", Project(archivedAt: "2026-11-19T02:00:00Z"));

        // Act
        var result = await cli.RunAsync("project", verb, "WEB", "--json");

        // Assert
        var root = JsonDocument.Parse(result.Out).RootElement;
        Assert.That(root.GetProperty("archivedAt").GetString(), Is.EqualTo("2026-11-19T02:00:00Z"));
        Assert.That(result.Out, Does.Not.Contain("已封存"));
    }

    [TestCase("archive")]
    [TestCase("unarchive")]
    public async Task ArchiveAndUnarchive_WithoutAKey_ExitsWithUsageError(string verb)
    {
        // Arrange
        using var cli = LoggedIn();

        // Act
        var result = await cli.RunAsync("project", verb);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(cli.Server.Requests, Is.Empty);
    }

    [TestCase(HttpStatusCode.Forbidden, "auth.forbidden", "你沒有封存這個專案的權限。")]
    [TestCase(HttpStatusCode.NotFound, "project.not_found", "找不到這個專案。")]
    [TestCase(HttpStatusCode.Conflict, "project.archived", "專案已封存，不能再修改。")]
    public async Task Archive_ServerFailures_ExitWith1AndPrintTheServersMessage(HttpStatusCode status, string code, string message)
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.OnError("POST", "/v1/projects/WEB/archive", status, code, message);

        // Act
        var result = await cli.RunAsync("project", "archive", "WEB");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain(message));
        Assert.That(result.Out, Is.Empty);
    }

    [Test]
    public async Task Rename_ArchivedProject_ExitsWith1AndPrintsTheServersMessage()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.OnError("PUT", "/v1/projects/WEB/name", HttpStatusCode.Conflict, "project.archived", "專案已封存，不能再修改。");

        // Act
        var result = await cli.RunAsync("project", "rename", "WEB", "--name", "新名稱");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("專案已封存，不能再修改。"));
    }

    [Test]
    public async Task ProjectList_MarksArchivedProjectsAndLeavesOthersAlone()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/projects", new object[]
        {
            Project(),
            new { key = "OLD", name = "舊專案", blueprintVersion = 1, myRole = "viewer", archivedAt = "2026-11-01T02:00:00Z" },
        });

        // Act
        var table = await cli.RunAsync("project", "list");
        var json = await cli.RunAsync("project", "list", "--json");

        // Assert
        var lines = table.Out.Split('\n');
        using (Assert.EnterMultipleScope())
        {
            Assert.That(lines.Single(line => line.Contains("WEB")), Does.Not.Contain("已封存"));
            Assert.That(lines.Single(line => line.Contains("OLD")), Does.Contain("舊專案").And.Contain("已封存"));
            Assert.That(JsonDocument.Parse(json.Out).RootElement[1].GetProperty("archivedAt").GetString(), Is.EqualTo("2026-11-01T02:00:00Z"));
        }
    }

    [Test]
    public async Task Help_ListsTheNewProjectCommands()
    {
        // Arrange
        using var cli = LoggedIn();

        // Act
        var result = await cli.RunAsync("--help");

        // Assert
        Assert.That(result.Out, Does.Contain("project rename").And.Contain("project archive").And.Contain("project unarchive"));
    }
}
