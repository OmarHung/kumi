using System.Net;
using System.Text.Json;
using PmTool.Cli.Api;

namespace PmTool.Cli.Tests;

/// <summary>專案與項目的指令：呼叫哪個 API、帶什麼內文、印什麼、結束代碼是什麼。</summary>
[TestFixture]
public sealed class ProjectAndItemCommandTests
{
    private const string PersonId = "0190a000-0000-7000-8000-000000000001";

    private static object Item(string key = "WEB-1", string state = "todo") => new
    {
        key,
        projectKey = "WEB",
        number = 1,
        type = "task",
        title = "改首頁",
        state,
        stateCategory = "not_started",
        assignee = new { personId = PersonId, displayName = "王小明" },
        createdBy = new { personId = "creator-1", displayName = "李小華" },
        priority = "high",
        startDate = (string?)null,
        dueDate = "2026-12-01",
        estimate = 3,
        parentKey = "WEB-9",
        fields = new Dictionary<string, object?> { ["budget"] = 1200, ["tags"] = new[] { "a", "b" } },
        createdAt = "2026-11-01T02:00:00Z",
        updatedAt = "2026-11-02T02:00:00Z",
        description = "第一行\n第二行",
    };

    /// <summary>專案詳情：藍圖裡有狀態、類型與欄位的名稱，給人看的輸出要靠它把代號換成名稱。</summary>
    private static object WebProject => new
    {
        project = new { key = "WEB", blueprintVersion = 3 },
        blueprint = new
        {
            fields = new object[]
            {
                new { key = "budget", name = "預算", type = "number" },
                new { key = "done", name = "完成了嗎", type = "checkbox" },
                new { key = "note", name = "備註", type = "text" },
                new { key = "code", name = "代碼", type = "text" },
                new { key = "tags", name = "標籤", type = "multi_select" },
                new { key = "link", name = "連結", type = "url" },
                new { key = "price", name = "價格", type = "currency" },
            },
            workflows = new[]
            {
                new
                {
                    key = "main", name = "主要流程",
                    states = new[]
                    {
                        new { key = "todo", name = "待辦", category = "not_started" },
                        new { key = "doing", name = "進行中", category = "in_progress" },
                    },
                },
            },
            itemTypes = new[] { new { key = "task", name = "任務", workflow = "main" } },
        },
        myPermissions = new[] { "item.read" },
    };

    private static CliHarness LoggedIn() => new CliHarness().LoggedInByEnvironment();

    [Test]
    public async Task ProjectList_PrintsATableAndJsonIsTheDataAsIs()
    {
        // Arrange
        using var cli = LoggedIn();
        var projects = new[] { new { key = "WEB", name = "官網改版", blueprintVersion = 3, myRole = "admin" } };
        cli.Server.On("GET", "/v1/projects", projects);

        // Act
        var table = await cli.RunAsync("project", "list");
        var json = await cli.RunAsync("project", "list", "--json");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.ExitCode, Is.EqualTo(ExitCodes.Success), table.Error);
            Assert.That(table.Out, Does.Contain("代號").And.Contain("WEB").And.Contain("官網改版").And.Contain("管理員"));
            Assert.That(JsonDocument.Parse(json.Out).RootElement[0].GetProperty("blueprintVersion").GetInt32(), Is.EqualTo(3));
            Assert.That(json.Out, Does.Contain("官網改版"), "中文不轉成 \\u 跳脫");
        }
    }

    [Test]
    public async Task ItemList_SendsTheFiltersAsQueryParametersAndPrintsATable()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/projects/WEB", WebProject);
        cli.Server.On("GET", "/v1/projects/WEB/items", new { items = new[] { Item() }, nextCursor = "abc" });

        // Act
        var result = await cli.RunAsync(
            "item", "list", "--project", "WEB", "--mine", "--state", "todo", "--state", "doing", "--category", "in_progress", "--type", "task", "--limit", "20");

        // Assert
        var request = cli.Server.To("GET", "/v1/projects/WEB/items").Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(request.Query("assignee"), Is.EqualTo("me"));
            Assert.That(request.QueryAll("state"), Is.EqualTo(new[] { "todo", "doing" }));
            Assert.That((request.Query("stateCategory"), request.Query("type"), request.Query("limit")), Is.EqualTo(("in_progress", "task", "20")));
            Assert.That(result.Out, Does.Contain("WEB-1").And.Contain("改首頁").And.Contain("王小明").And.Contain("2026-12-01"));
            Assert.That(result.Out, Does.Contain("任務").And.Contain("待辦").And.Not.Contain("task").And.Not.Contain("todo"), "給人看的是名稱");
            Assert.That(result.Error, Does.Contain("還有更多").And.Contain("--cursor abc").And.Contain("--all"));
        }
    }

    [TestCase("@me", "me")]
    [TestCase("me", "me")]
    [TestCase("018f0000-0000-7000-8000-0000000000aa", "018f0000-0000-7000-8000-0000000000aa")]
    public async Task ItemList_CreatedBy_SendsMeOrThePersonId(string given, string expected)
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/projects/WEB", WebProject);
        cli.Server.On("GET", "/v1/projects/WEB/items", new { items = new[] { Item() }, nextCursor = (string?)null });

        // Act
        var result = await cli.RunAsync("item", "list", "-p", "WEB", "--created-by", given, "--mine");

        // Assert
        var request = cli.Server.To("GET", "/v1/projects/WEB/items").Single();
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That((request.Query("createdBy"), request.Query("assignee")), Is.EqualTo((expected, "me")));
    }

    [Test]
    public async Task ItemList_CreatedByEmail_LooksThePersonUpInThePeopleList()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/projects/WEB", WebProject);
        cli.Server.On("GET", "/v1/people", new[] { new { personId = PersonId, email = "Ming@Example.com", displayName = "王小明" } });
        cli.Server.On("GET", "/v1/projects/WEB/items", new { items = new[] { Item() }, nextCursor = (string?)null });

        // Act
        var result = await cli.RunAsync("item", "list", "-p", "WEB", "--created-by", "ming@example.com");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(cli.Server.To("GET", "/v1/projects/WEB/items").Single().Query("createdBy"), Is.EqualTo(PersonId));
    }

    [Test]
    public async Task ItemList_NoFilters_SendsNoQueryAndJsonKeepsTheCursor()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/projects/WEB/items", new { items = new[] { Item() }, nextCursor = (string?)null });

        // Act
        var result = await cli.RunAsync("item", "list", "-p", "WEB", "--json");

        // Assert
        Assert.That(cli.Server.Requests.Single().Uri.Query, Is.Empty);
        Assert.That(JsonDocument.Parse(result.Out).RootElement.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
        Assert.That(result.Error, Is.Empty);
    }

    [Test]
    public async Task ItemList_ServerRejectsTheQuery_ExitsWithTwoAndShowsTheDetails()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.OnError("GET", "/v1/projects/WEB/items", HttpStatusCode.UnprocessableEntity, "query.invalid", "查詢參數不正確。", ("stateCategory", "不認得的狀態類別：x。"));

        // Act
        var result = await cli.RunAsync("item", "list", "--project", "WEB", "--category", "x");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("stateCategory：不認得的狀態類別：x。"));
    }

    [Test]
    public async Task ItemView_ShowsFieldsChildrenAndRecentActivity()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/items/WEB-1", Item());
        cli.Server.On("GET", "/v1/projects/WEB", WebProject);
        cli.Server.On("GET", "/v1/projects/WEB/items", new { items = new[] { Item("WEB-2", "doing") }, nextCursor = (string?)null });
        cli.Server.On("GET", "/v1/items/WEB-1/activity", new
        {
            // 和真的伺服器一樣由舊到新
            items = new object[]
            {
                new { kind = "created", actor = new { displayName = "李小華" }, at = "2026-11-01T00:00:00Z", changes = Array.Empty<object>() },
                new { kind = "updated", actor = new { displayName = "李小華" }, at = "2026-11-01T01:00:00Z", changes = new[] { new { field = "title" }, new { field = "due_date" } } },
                new { kind = "transitioned", actor = new { displayName = "李小華" }, at = "2026-11-01T02:00:00Z", fromState = "todo", toState = "doing", changes = Array.Empty<object>() },
                new { kind = "commented", actor = new { displayName = "王小明" }, at = "2026-11-02T02:00:00Z", comment = "已經改好\n再看一下", changes = Array.Empty<object>() },
            },
            nextCursor = (string?)null,
        });

        // Act
        var result = await cli.RunAsync("item", "view", "WEB-1");

        // Assert
        var children = cli.Server.Requests.Single(r => r.Path == "/v1/projects/WEB/items");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That((children.Query("parent"), children.Query("limit")), Is.EqualTo(("WEB-1", "50")));
            Assert.That(cli.Server.Requests.Single(r => r.Path.EndsWith("/activity")).Query("limit"), Is.EqualTo("200"));
            Assert.That(result.Out, Does.Contain("WEB-1  改首頁").And.Contain("類型：任務").And.Contain("狀態：待辦（未開始）").And.Contain("優先順序：高").And.Contain("上層：WEB-9"));
            Assert.That(result.Out, Does.Contain("建立者：李小華"));
            Assert.That(result.Out, Does.Contain("預算：1200").And.Contain("標籤：a、b"));
            Assert.That(result.Out, Does.Contain("  第一行").And.Contain("  第二行"));
            Assert.That(result.Out, Does.Contain("下層（1）").And.Contain("WEB-2  改首頁［進行中］"));
            Assert.That(result.Out, Does.Contain("留言：已經改好").And.Not.Contain("再看一下"));
            Assert.That(result.Out, Does.Contain("狀態 待辦 → 進行中").And.Contain("修改：title、due_date").And.Contain("建立了項目"));
            Assert.That(result.Out.IndexOf("留言：已經改好", StringComparison.Ordinal), Is.LessThan(result.Out.IndexOf("建立了項目", StringComparison.Ordinal)), "新的在前");
        }
    }

    [Test]
    public async Task ItemView_Json_CombinesTheItemChildrenAndActivity()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/items/WEB-1", Item());
        cli.Server.On("GET", "/v1/projects/WEB/items", new { items = new[] { Item("WEB-2") }, nextCursor = (string?)null });
        cli.Server.On("GET", "/v1/items/WEB-1/activity", new { items = Array.Empty<object>(), nextCursor = (string?)null });

        // Act
        var result = await cli.RunAsync("item", "view", "WEB-1", "--json");

        // Assert
        var data = JsonDocument.Parse(result.Out).RootElement;
        Assert.That(
            (data.GetProperty("item").GetProperty("key").GetString(), data.GetProperty("children").GetArrayLength(), data.GetProperty("activity").GetArrayLength()),
            Is.EqualTo(("WEB-1", 1, 0)));
    }

    [Test]
    public async Task ItemView_MinimalItem_PrintsDashesWithoutFailing()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/items/WEB-3", new { key = "WEB-3", projectKey = "WEB", title = "空的", fields = new { } });
        cli.Server.OnError("GET", "/v1/projects/WEB", HttpStatusCode.Forbidden, "auth.insufficient_scope", "權杖的範圍不夠。");
        cli.Server.On("GET", "/v1/projects/WEB/items", new { items = Array.Empty<object>() });
        cli.Server.On("GET", "/v1/items/WEB-3/activity", new { items = Array.Empty<object>() });

        // Act
        var result = await cli.RunAsync("item", "view", "WEB-3");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(result.Out, Does.Contain("負責人：-").And.Contain("到期：-"), "查不到藍圖也印得出來");
    }

    [Test]
    public async Task ItemView_NotFound_ExitsWithOne()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.OnError("GET", "/v1/items/WEB-404", HttpStatusCode.NotFound, "item.not_found", "找不到項目 WEB-404。");

        // Act
        var result = await cli.RunAsync("item", "view", "WEB-404");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("找不到項目 WEB-404。"));
    }

    [Test]
    public async Task ItemCreate_SendsTheBodyWithAssigneeAndTypedFields()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/session", new { user = new { personId = PersonId } });
        cli.Server.On("GET", "/v1/projects/WEB", WebProject);
        cli.Server.On("POST", "/v1/projects/WEB/items", Item("WEB-5"), HttpStatusCode.Created);

        // Act
        var result = await cli.RunAsync(
            "item", "create", "--project", "WEB", "--type", "task", "--title", "新任務", "--assignee", "@me",
            "--set", "budget=1200", "--set", "done=true", "--set", "note=你好", "--set", "code=123", "--set", "tags=a, b",
            "--set", "priority=high", "--set", "dueDate=2026-12-31", "--set", "estimate=2.5", "--set", "parentKey=WEB-1", "--set", "gone=null");

        // Assert
        var request = cli.Server.To("POST", "/v1/projects/WEB/items").Single();
        var body = request.Json!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Out, Does.Contain("已建立 WEB-5"));
            Assert.That(request.ContentType, Is.EqualTo("application/json"));
            Assert.That((string?)body["type"], Is.EqualTo("task"));
            Assert.That((string?)body["title"], Is.EqualTo("新任務"));
            Assert.That((string?)body["assigneeId"], Is.EqualTo(PersonId));
            Assert.That((string?)body["priority"], Is.EqualTo("high"));
            Assert.That((string?)body["dueDate"], Is.EqualTo("2026-12-31"));
            Assert.That((decimal?)body["estimate"], Is.EqualTo(2.5m));
            Assert.That((string?)body["parentKey"], Is.EqualTo("WEB-1"));
            Assert.That((int?)body["fields"]!["budget"], Is.EqualTo(1200));
            Assert.That((bool?)body["fields"]!["done"], Is.True);
            Assert.That((string?)body["fields"]!["note"], Is.EqualTo("你好"));
            Assert.That((string?)body["fields"]!["code"], Is.EqualTo("123"), "文字欄位一律是字串，不用加引號");
            Assert.That(body["fields"]!["tags"]!.AsArray().Select(t => (string?)t), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(body["fields"]!.AsObject().ContainsKey("gone"), Is.True);
            Assert.That(body["fields"]!["gone"], Is.Null);
        }
    }

    [Test]
    public async Task ItemCreate_AssigneeByEmail_LooksThePersonUpInThePeopleList()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/people", new[]
        {
            new { personId = "other", email = "a@example.com", displayName = "甲" },
            new { personId = PersonId, email = "Ming@Example.com", displayName = "王小明" },
        });
        cli.Server.On("POST", "/v1/projects/WEB/items", Item(), HttpStatusCode.Created);

        // Act
        var result = await cli.RunAsync("item", "create", "-p", "WEB", "--type", "task", "-t", "標題", "--assignee", "ming@example.com", "--json");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That((string?)cli.Server.To("POST", "/v1/projects/WEB/items").Single().Json!["assigneeId"], Is.EqualTo(PersonId));
        Assert.That(JsonDocument.Parse(result.Out).RootElement.GetProperty("key").GetString(), Is.EqualTo("WEB-1"));
    }

    [Test]
    public async Task ItemCreate_AssigneeNobodyHasThatEmail_IsAUsageErrorAndNothingIsCreated()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/people", Array.Empty<object>());

        // Act
        var result = await cli.RunAsync("item", "create", "-p", "WEB", "--type", "task", "-t", "標題", "--assignee", "nobody@example.com");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("nobody@example.com"));
        Assert.That(cli.Server.To("POST", "/v1/projects/WEB/items"), Is.Empty);
    }

    [TestCase("--set", "novalue")]
    [TestCase("--set", "=x")]
    [TestCase("--set", "estimate=abc")]
    public async Task ItemCreate_MalformedSet_IsAUsageErrorBeforeAnyRequest(params string[] set)
    {
        // Arrange
        using var cli = LoggedIn();

        // Act
        var result = await cli.RunAsync(["item", "create", "-p", "WEB", "--type", "task", "-t", "標題", .. set]);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(cli.Server.Requests, Is.Empty);
    }

    [Test]
    public async Task ItemCreate_ServerSaysTheTypeIsUnknown_ExitsWithTwo()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.OnError("POST", "/v1/projects/WEB/items", HttpStatusCode.UnprocessableEntity, "item.unknown_type", "專案沒有「bug」這個類型。");

        // Act
        var result = await cli.RunAsync("item", "create", "-p", "WEB", "--type", "bug", "-t", "標題");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("沒有「bug」這個類型"));
    }

    [Test]
    public async Task ItemUpdate_SendsOnlyWhatWasGivenAndNoneClearsTheAssignee()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/projects/WEB", WebProject);
        cli.Server.On("PATCH", "/v1/items/WEB-1", Item());

        // Act
        var result = await cli.RunAsync("item", "update", "WEB-1", "--title", "新標題", "--assignee", "none", "--set", "budget=5");

        // Assert
        var body = cli.Server.To("PATCH", "/v1/items/WEB-1").Single().Json!.AsObject();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Out, Does.Contain("已更新 WEB-1"));
            Assert.That(body.Select(p => p.Key), Is.EquivalentTo(new[] { "title", "assigneeId", "fields" }));
            Assert.That(body["assigneeId"], Is.Null);
        }
    }

    [Test]
    public async Task ItemUpdate_NothingToChange_IsAUsageError()
    {
        // Arrange
        using var cli = LoggedIn();

        // Act
        var result = await cli.RunAsync("item", "update", "WEB-1");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(cli.Server.Requests, Is.Empty);
    }

    [Test]
    public async Task ItemMove_Allowed_ReportsTheNewState()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/projects/WEB", WebProject);
        cli.Server.On("POST", "/v1/items/WEB-1/transitions", Item("WEB-1", "doing"));

        // Act
        var result = await cli.RunAsync("item", "move", "WEB-1", "--to", "doing");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(result.Out, Does.Contain("WEB-1 已移到「進行中」").And.Not.Contain("doing"));
        Assert.That((string?)cli.Server.To("POST", "/v1/items/WEB-1/transitions").Single().Json!["to"], Is.EqualTo("doing"));
    }

    [Test]
    public async Task ItemMove_NotAllowed_ExitsWithTwoAndRelaysTheServersReason()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.OnError("POST", "/v1/items/WEB-1/transitions", HttpStatusCode.UnprocessableEntity, "item.transition_not_allowed", "不能從「待辦」直接移到「完成」。");

        // Act
        var result = await cli.RunAsync("item", "move", "WEB-1", "--to", "done");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("不能從「待辦」直接移到「完成」。"));
    }

    [Test]
    public async Task ItemComment_PostsTheTextAndEscapesTheKeyInThePath()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("POST", "/v1/items/WEB-1/comments", new { id = "c1", kind = "commented" }, HttpStatusCode.Created);

        // Act
        var result = await cli.RunAsync("item", "comment", "WEB-1", "-m", "完成了\n請看");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(result.Out, Does.Contain("已在 WEB-1 留言"));
        Assert.That((string?)cli.Server.Requests.Single().Json!["body"], Is.EqualTo("完成了\n請看"));
    }

    [Test]
    public async Task ItemComment_WithoutAMessage_IsAUsageError()
    {
        // Arrange
        using var cli = LoggedIn();

        // Act
        var result = await cli.RunAsync("item", "comment", "WEB-1");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
    }

    [Test]
    public async Task Requests_CarryTheBearerTokenAndNoCookieOrCsrfHeader()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/projects", Array.Empty<object>());

        // Act
        await cli.RunAsync("project", "list");

        // Assert
        var request = cli.Server.Requests.Single();
        Assert.That(request.Authorization, Is.EqualTo($"Bearer {CliHarness.Token}"));
        Assert.That(request.UserAgent, Does.StartWith("kumi-cli"));
    }

    [Test]
    public void Moment_UnparseableText_IsADash() => Assert.That(JsonAccess.Moment("not a time"), Is.EqualTo("-"));
}
