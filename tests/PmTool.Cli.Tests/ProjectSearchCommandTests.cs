using System.Net;
using System.Text.Json;

namespace PmTool.Cli.Tests;

/// <summary>kumi project search：用代號、名稱或專案說明找專案。</summary>
[TestFixture]
public sealed class ProjectSearchCommandTests
{
    private const string Path = "/v1/search/projects";

    private static CliHarness LoggedIn() => new CliHarness().LoggedInByEnvironment();

    private static object Hit(string key, string name, string? snippet, string? archivedAt = null) =>
        new { project = new { key, name, blueprintVersion = 1, myRole = "admin", archivedAt }, snippet };

    [Test]
    public async Task Search_PrintsATableWithSnippetsAndArchiveMarks()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", Path, new[]
        {
            Hit("WEB", "官網改版", "主機：web01.example.com"),
            Hit("OLD", "舊站", null, archivedAt: "2026-11-19T02:00:00Z"),
        });

        // Act
        var result = await cli.RunAsync("project", "search", "web01");

        // Assert
        var lines = result.Out.ReplaceLineEndings("\n").TrimEnd().Split('\n');
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(lines[0], Does.Contain("代號").And.Contain("名稱").And.Contain("說明裡的"));
            Assert.That(lines[1], Does.Contain("WEB").And.Contain("官網改版").And.Contain("主機：web01.example.com"));
            Assert.That(lines[2], Does.Contain("OLD").And.Contain("舊站（已封存）"));
            Assert.That(lines[2], Does.Not.Contain("null"));
            Assert.That(cli.Server.To("GET", Path).Single().QueryAll("q"), Is.EqualTo(new[] { "web01" }));
            Assert.That(cli.Server.To("GET", Path).Single().QueryAll("limit"), Is.Empty);
        }
    }

    [Test]
    public async Task Search_WithLimit_SendsIt()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", Path, Array.Empty<object>());

        // Act
        var result = await cli.RunAsync("project", "search", "192.0.2.10", "--limit", "20");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(cli.Server.To("GET", Path).Single().QueryAll("limit"), Is.EqualTo(new[] { "20" }));
            Assert.That(cli.Server.To("GET", Path).Single().QueryAll("q"), Is.EqualTo(new[] { "192.0.2.10" }));
        }
    }

    [Test]
    public async Task Search_NoHits_SaysSoWithExitCode0()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", Path, Array.Empty<object>());

        // Act
        var result = await cli.RunAsync("project", "search", "nothing");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Out.Trim(), Is.EqualTo("找不到符合的專案。"));
        }
    }

    [Test]
    public async Task Search_Json_PrintsTheServersArrayAsIs()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", Path, new[] { Hit("WEB", "官網改版", "主機：web01") });

        // Act
        var result = await cli.RunAsync("project", "search", "web01", "--json");

        // Assert
        var hits = JsonDocument.Parse(result.Out).RootElement;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(hits[0].GetProperty("snippet").GetString(), Is.EqualTo("主機：web01"));
            Assert.That(hits[0].GetProperty("project").GetProperty("key").GetString(), Is.EqualTo("WEB"));
        }
    }

    [Test]
    public async Task Search_Json_NoHitsPrintsAnEmptyArray()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", Path, Array.Empty<object>());

        // Act
        var result = await cli.RunAsync("project", "search", "nothing", "--json");

        // Assert
        Assert.That(JsonDocument.Parse(result.Out).RootElement.GetArrayLength(), Is.EqualTo(0));
    }

    [Test]
    public async Task Search_ServerRejectsTheQuery_ExitsWith2AndShowsTheMessage()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.OnError("GET", Path, HttpStatusCode.UnprocessableEntity, "query.invalid", "數量要在 1 到 20 之間。", ("limit", "數量要在 1 到 20 之間。"));

        // Act
        var result = await cli.RunAsync("project", "search", "web01", "--limit", "99");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid), result.Out);
            Assert.That(result.Error, Does.Contain("數量要在 1 到 20 之間。"));
        }
    }

    [Test]
    public async Task Search_WithoutText_ExitsWithUsageError()
    {
        // Arrange
        using var cli = LoggedIn();

        // Act
        var result = await cli.RunAsync("project", "search");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(cli.Server.Requests, Is.Empty);
        }
    }
}
