using System.Net;
using System.Text.Json;

namespace PmTool.Cli.Tests;

/// <summary>留言的列表、編輯、刪除與復原。</summary>
[TestFixture]
public sealed class CommentEditCommandTests
{
    private const string Top = "11111111-1111-1111-1111-111111111111";
    private const string Reply = "22222222-2222-2222-2222-222222222222";
    private const string Gone = "33333333-3333-3333-3333-333333333333";
    private const string GoneBy = "44444444-4444-4444-4444-444444444444";

    private static CliHarness LoggedIn() => new CliHarness().LoggedInByEnvironment();

    private static object Comment(string id, string who, string at, string? text, string? replyTo = null,
        string? editedAt = null, string? deletedAt = null, string? deletedBy = null) => new
        {
            id,
            kind = "commented",
            actor = new { displayName = who },
            at,
            comment = text,
            replyToId = replyTo,
            editedAt,
            deletedAt,
            deletedBy = deletedBy is null ? null : new { id = "p1", displayName = deletedBy },
            changes = Array.Empty<object>(),
        };

    private static object[] Thread() =>
    [
        Comment(Top, "王小明", "2026-11-02T02:00:00Z", "第一則\n第二行", editedAt: "2026-11-02T03:00:00Z"),
        Comment(Reply, "李小華", "2026-11-02T04:00:00Z", "回覆內容", replyTo: Top),
        Comment(Gone, "王小明", "2026-11-02T05:00:00Z", null, deletedAt: "2026-11-02T06:00:00Z"),
        Comment(GoneBy, "李小華", "2026-11-02T07:00:00Z", null, deletedAt: "2026-11-02T08:00:00Z", deletedBy: "管理者"),
    ];

    [Test]
    public async Task Comments_ListsFullIdsAuthorsMarksAndIndentsReplies()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/items/WEB-1/activity", new { items = Thread(), nextCursor = (string?)null });

        // Act
        var result = await cli.RunAsync("item", "comments", "WEB-1");

        // Assert
        var request = cli.Server.Requests.Single();
        var lines = result.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(request.Query("kind"), Is.EqualTo("commented"));
            Assert.That(lines[0], Does.Contain(Top).And.Contain("王小明").And.Contain("（已編輯）").And.Contain("第一則").And.Not.Contain("第二行"));
            Assert.That(lines[1], Does.StartWith("  ").And.Contain(Reply).And.Contain("李小華").And.Contain("回覆內容").And.Not.Contain("（已編輯）"));
            Assert.That(lines[2], Does.Contain(Gone).And.Contain("（已刪除）"));
            Assert.That(lines[3], Does.Contain(GoneBy).And.Contain("（已由 管理者 刪除）"));
        }
    }

    [Test]
    public async Task Comments_ReadsEveryPage()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/items/WEB-1/activity", request => request.Query("cursor") switch
        {
            null => FakeServer.Envelope(HttpStatusCode.OK, new { items = new[] { Comment(Top, "甲", "2026-11-02T02:00:00Z", "一") }, nextCursor = "p2" }),
            _ => FakeServer.Envelope(HttpStatusCode.OK, new { items = new[] { Comment(Reply, "乙", "2026-11-02T03:00:00Z", "二") }, nextCursor = (string?)null }),
        });

        // Act
        var result = await cli.RunAsync("item", "comments", "WEB-1");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cli.Server.Requests, Has.Count.EqualTo(2));
            Assert.That(result.Out, Does.Contain(Top).And.Contain(Reply));
        }
    }

    [Test]
    public async Task Comments_Json_WrapsTheServerActivitiesUnchanged()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/items/WEB-1/activity", new { items = Thread(), nextCursor = (string?)null });

        // Act
        var result = await cli.RunAsync("item", "comments", "WEB-1", "--json");

        // Assert
        var items = JsonDocument.Parse(result.Out).RootElement.GetProperty("items");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items.GetArrayLength(), Is.EqualTo(4));
            Assert.That(items[0].GetProperty("editedAt").GetString(), Is.EqualTo("2026-11-02T03:00:00Z"));
            Assert.That(items[3].GetProperty("deletedBy").GetProperty("displayName").GetString(), Is.EqualTo("管理者"));
        }
    }

    [Test]
    public async Task CommentEdit_PatchesTheBodyAndReports()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("PATCH", $"/v1/items/WEB-1/comments/{Top}", Comment(Top, "王小明", "2026-11-02T02:00:00Z", "新內容"));

        // Act
        var result = await cli.RunAsync("item", "comment-edit", "WEB-1", Top, "-m", "新內容");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Out, Does.Contain("已更新 WEB-1 的留言。"));
            Assert.That((string?)cli.Server.Requests.Single().Json!["body"], Is.EqualTo("新內容"));
        }
    }

    [Test]
    public async Task CommentEdit_Json_PrintsTheActivityItselfLikeItemComment()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("PATCH", $"/v1/items/WEB-1/comments/{Top}", Comment(Top, "王小明", "2026-11-02T02:00:00Z", "新內容"));

        // Act
        var result = await cli.RunAsync("item", "comment-edit", "WEB-1", Top, "-m", "新內容", "--json");

        // Assert
        Assert.That(JsonDocument.Parse(result.Out).RootElement.GetProperty("id").GetString(), Is.EqualTo(Top));
    }

    [Test]
    public async Task CommentEdit_WithoutAMessage_IsAUsageError()
    {
        // Arrange
        using var cli = LoggedIn();

        // Act
        var result = await cli.RunAsync("item", "comment-edit", "WEB-1", Top);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
    }

    [Test]
    public async Task CommentEdit_NotTheAuthor_ShowsTheServerMessageAndFails()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.OnError("PATCH", $"/v1/items/WEB-1/comments/{Top}", HttpStatusCode.Forbidden, "comment.not_author", "只能編輯自己的留言。");

        // Act
        var result = await cli.RunAsync("item", "comment-edit", "WEB-1", Top, "-m", "x");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("只能編輯自己的留言。"));
        }
    }

    [Test]
    public async Task CommentDelete_SendsDeleteAndPointsToRestore()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("DELETE", $"/v1/items/WEB-1/comments/{Top}", Comment(Top, "王小明", "2026-11-02T02:00:00Z", null, deletedAt: "2026-11-02T06:00:00Z"));

        // Act
        var result = await cli.RunAsync("item", "comment-delete", "WEB-1", Top);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Out, Does.Contain("已刪除 WEB-1 的一則留言（可以用 kumi item comment-restore 復原）。"));
        }
    }

    [Test]
    public async Task CommentRestore_PostsRestoreAndReports()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("POST", $"/v1/items/WEB-1/comments/{Top}/restore", Comment(Top, "王小明", "2026-11-02T02:00:00Z", "回來了"));

        // Act
        var result = await cli.RunAsync("item", "comment-restore", "WEB-1", Top, "--json");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(JsonDocument.Parse(result.Out).RootElement.GetProperty("comment").GetString(), Is.EqualTo("回來了"));
        }
    }

    [Test]
    public async Task CommentRestore_Text_Reports()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("POST", $"/v1/items/WEB-1/comments/{Top}/restore", Comment(Top, "王小明", "2026-11-02T02:00:00Z", "回來了"));

        // Act
        var result = await cli.RunAsync("item", "comment-restore", "WEB-1", Top);

        // Assert
        Assert.That(result.Out, Does.Contain("已復原 WEB-1 的一則留言。"));
    }

    [Test]
    public async Task CommentIdWithSlashes_IsEscapedInThePath()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.OnError("DELETE", "/v1/items/WEB-1/comments/a%2Fb", HttpStatusCode.NotFound, "comment.not_found", "找不到這則留言。");

        // Act
        var result = await cli.RunAsync("item", "comment-delete", "WEB-1", "a/b");

        // Assert
        Assert.That(result.Error, Does.Contain("找不到這則留言。"));
    }

    [Test]
    public async Task ItemView_MarksEditedAndDeletedComments()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/items/WEB-1", new { key = "WEB-1", projectKey = "WEB", type = "task", title = "t", state = "todo", stateCategory = "not_started" });
        cli.Server.On("GET", "/v1/projects/WEB", new { project = new { key = "WEB", blueprintVersion = 1 }, blueprint = new { fields = Array.Empty<object>(), workflows = Array.Empty<object>() } });
        cli.Server.On("GET", "/v1/projects/WEB/items", new { items = Array.Empty<object>(), nextCursor = (string?)null });
        cli.Server.On("GET", "/v1/items/WEB-1/activity", new { items = Thread(), nextCursor = (string?)null });

        // Act
        var result = await cli.RunAsync("item", "view", "WEB-1");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Out, Does.Contain("留言（已編輯）：第一則"));
            Assert.That(result.Out, Does.Contain("留言已刪除").And.Not.Contain("留言：null"));
            Assert.That(result.Out, Does.Contain("留言：回覆內容"));
        }
    }
}
