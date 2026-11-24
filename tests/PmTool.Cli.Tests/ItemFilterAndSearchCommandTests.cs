using System.Net;
using System.Text.Json;

namespace PmTool.Cli.Tests;

/// <summary>kumi item list 的優先度與到期日選項，以及 kumi search。</summary>
[TestFixture]
public sealed class ItemFilterAndSearchCommandTests
{
    private static object Item(string key, string projectKey, string title, string state = "todo") => new
    {
        key,
        projectKey,
        number = 1,
        type = "task",
        title,
        state,
        stateCategory = "not_started",
        assignee = (object?)null,
        priority = "none",
        startDate = (string?)null,
        dueDate = (string?)null,
        estimate = (int?)null,
        parentKey = (string?)null,
        fields = new Dictionary<string, object?>(),
        createdAt = "2026-11-01T02:00:00Z",
        updatedAt = "2026-11-02T02:00:00Z",
    };

    private static object Blueprint(string todoName) => new
    {
        project = new { key = "X" },
        blueprint = new
        {
            fields = Array.Empty<object>(),
            workflows = new[]
            {
                new { key = "main", name = "流程", states = new[] { new { key = "todo", name = todoName, category = "not_started" } } },
            },
            itemTypes = new[] { new { key = "task", name = "任務", workflow = "main" } },
        },
        myPermissions = new[] { "item.read" },
    };

    private static CliHarness LoggedIn() => new CliHarness().LoggedInByEnvironment();

    [Test]
    public async Task ItemList_PriorityAndDueOptions_AreSentAsQueryParameters()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/projects/WEB/items", new { items = Array.Empty<object>(), nextCursor = (string?)null });
        cli.Server.On("GET", "/v1/projects/WEB", Blueprint("待辦"));

        // Act
        var result = await cli.RunAsync(
            "item", "list", "-p", "WEB", "--priority", "high", "--priority", "urgent", "--due-from", "2026-11-16", "--due-to", "2026-11-22");

        // Assert
        var request = cli.Server.To("GET", "/v1/projects/WEB/items").Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(request.QueryAll("priority"), Is.EqualTo(new[] { "high", "urgent" }));
            Assert.That((request.Query("dueFrom"), request.Query("dueTo")), Is.EqualTo(("2026-11-16", "2026-11-22")));
            Assert.That(request.Query("noDue"), Is.Null);
        }
    }

    [Test]
    public async Task Search_TextStartingWithADash_CanBeGivenAfterDoubleDash()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/search/items", new { items = Array.Empty<object>() });

        // Act
        var result = await cli.RunAsync("search", "--limit", "5", "--", "-x");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        var request = cli.Server.To("GET", "/v1/search/items").Single();
        Assert.That(request.Query("q"), Is.EqualTo("-x"));
        Assert.That(request.Query("limit"), Is.EqualTo("5"));
    }

    [Test]
    public async Task ItemList_NoDue_SendsNoDueTrue()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/projects/WEB/items", new { items = Array.Empty<object>(), nextCursor = (string?)null });
        cli.Server.On("GET", "/v1/projects/WEB", Blueprint("待辦"));

        // Act
        var result = await cli.RunAsync("item", "list", "-p", "WEB", "--no-due");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(cli.Server.To("GET", "/v1/projects/WEB/items").Single().Query("noDue"), Is.EqualTo("true"));
    }

    [Test]
    public async Task ItemList_DatesRange_IsSentAsDatesFromAndDatesTo()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/projects/WEB/items", new { items = Array.Empty<object>(), nextCursor = (string?)null });
        cli.Server.On("GET", "/v1/projects/WEB", Blueprint("待辦"));

        // Act
        var result = await cli.RunAsync("item", "list", "-p", "WEB", "--dates-from", "2026-11-09", "--dates-to", "2026-12-20");

        // Assert
        var request = cli.Server.To("GET", "/v1/projects/WEB/items").Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That((request.Query("datesFrom"), request.Query("datesTo")), Is.EqualTo(("2026-11-09", "2026-12-20")));
            Assert.That(request.Query("dueFrom"), Is.Null);
        }
    }

    [TestCase("--dates-from")]
    [TestCase("--dates-to")]
    public async Task ItemList_NoDueTogetherWithADatesOption_IsAUsageErrorWithoutCallingTheServer(string option)
    {
        // Arrange
        using var cli = LoggedIn();

        // Act
        var result = await cli.RunAsync("item", "list", "-p", "WEB", "--no-due", option, "2026-11-16");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("--no-due"));
        Assert.That(cli.Server.Requests, Is.Empty);
    }

    [Test]
    public async Task ItemList_NoDueTogetherWithARange_IsAUsageErrorWithoutCallingTheServer()
    {
        // Arrange
        using var cli = LoggedIn();

        // Act
        var result = await cli.RunAsync("item", "list", "-p", "WEB", "--no-due", "--due-from", "2026-11-16");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("--no-due"));
        Assert.That(cli.Server.Requests, Is.Empty);
    }

    [Test]
    public async Task ItemList_ServerRejectsADate_ExitsWithTwoAndShowsTheDetails()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.OnError(
            "GET", "/v1/projects/WEB/items", HttpStatusCode.UnprocessableEntity, "query.invalid", "查詢參數不正確。", ("dueFrom", "日期要寫成 YYYY-MM-DD。"));

        // Act
        var result = await cli.RunAsync("item", "list", "-p", "WEB", "--due-from", "tomorrow");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("dueFrom：日期要寫成 YYYY-MM-DD。"));
    }

    [Test]
    public async Task Search_SendsTheTextAndLimitAndPrintsKeyTitleStateAndProject()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/search/items", new { items = new[] { Item("WEB-1", "WEB", "會員登入"), Item("APP-3", "APP", "登入頁", "todo") } });
        cli.Server.On("GET", "/v1/projects", new[]
        {
            new { key = "WEB", name = "官網改版", blueprintVersion = 1, myRole = "admin" },
            new { key = "APP", name = "行動應用", blueprintVersion = 1, myRole = "admin" },
        });
        cli.Server.On("GET", "/v1/projects/WEB", Blueprint("待辦"));
        cli.Server.On("GET", "/v1/projects/APP", Blueprint("準備中"));

        // Act
        var result = await cli.RunAsync("search", "登入", "--limit", "5");

        // Assert
        var request = cli.Server.To("GET", "/v1/search/items").Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That((request.Query("q"), request.Query("limit")), Is.EqualTo(("登入", "5")));
            Assert.That(result.Out, Does.Contain("編號").And.Contain("標題").And.Contain("狀態").And.Contain("專案"));
            Assert.That(result.Out, Does.Contain("WEB-1").And.Contain("會員登入").And.Contain("待辦").And.Contain("官網改版"));
            Assert.That(result.Out, Does.Contain("APP-3").And.Contain("準備中").And.Contain("行動應用"));
        }
    }

    [Test]
    public async Task Search_Json_IsTheServerDataAsIsAndNeedsNoLookups()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/search/items", new { items = new[] { Item("WEB-1", "WEB", "會員登入") } });

        // Act
        var result = await cli.RunAsync("search", "登入", "--json");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonDocument.Parse(result.Out).RootElement.GetProperty("items")[0].GetProperty("key").GetString(), Is.EqualTo("WEB-1"));
            Assert.That(cli.Server.To("GET", "/v1/search/items").Single().Query("limit"), Is.Null, "沒給 --limit 就用伺服器的預設");
            Assert.That(cli.Server.Requests, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Search_NothingFound_SaysSoAndSucceeds()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/search/items", new { items = Array.Empty<object>() });

        // Act
        var result = await cli.RunAsync("search", "不存在");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(result.Out, Does.Contain("沒有找到"));
    }

    [Test]
    public async Task Search_LookupsFail_StillPrintsKeysAndRawNames()
    {
        // Arrange：專案清單與藍圖取不到時，退回印代號，和其他指令一樣。
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/search/items", new { items = new[] { Item("WEB-1", "WEB", "會員登入") } });
        cli.Server.OnError("GET", "/v1/projects", HttpStatusCode.InternalServerError, "server.error", "壞了");
        cli.Server.OnError("GET", "/v1/projects/WEB", HttpStatusCode.InternalServerError, "server.error", "壞了");

        // Act
        var result = await cli.RunAsync("search", "登入");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(result.Out, Does.Contain("WEB-1").And.Contain("會員登入").And.Contain("todo"));
    }

    [Test]
    public async Task Search_ServerRejectsTheText_ExitsWithTwoAndShowsTheDetails()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.OnError("GET", "/v1/search/items", HttpStatusCode.UnprocessableEntity, "query.invalid", "查詢參數不正確。", ("q", "搜尋的文字太長。"));

        // Act
        var result = await cli.RunAsync("search", "太長");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("q：搜尋的文字太長。"));
    }

    [Test]
    public async Task Search_WithoutTextOrWithTwoTexts_IsAUsageError()
    {
        // Arrange
        using var cli = LoggedIn();

        // Act
        var none = await cli.RunAsync("search");
        var two = await cli.RunAsync("search", "a", "b");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(none.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(two.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(cli.Server.Requests, Is.Empty);
        }
    }

    [Test]
    public async Task Search_Help_ShowsUsageAndIsListedInTheOverview()
    {
        // Arrange
        using var cli = LoggedIn();

        // Act
        var help = await cli.RunAsync("search", "--help");
        var overview = await cli.RunAsync("--help");

        // Assert
        Assert.That(help.ExitCode, Is.EqualTo(ExitCodes.Success));
        Assert.That(help.Out, Does.Contain("kumi search").And.Contain("--limit"));
        Assert.That(overview.Out, Does.Contain("search"));
    }
}
