using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PmTool.Cli.Tests;

/// <summary>項目清單的翻頁、詳情被截斷的提示，以及 --set 依欄位型別轉值。</summary>
[TestFixture]
public sealed class ItemPagingAndSettingsTests
{
    private static object Row(string key) => new { key, projectKey = "WEB", type = "task", title = $"標題 {key}", state = "todo", stateCategory = "not_started" };

    private static object Project(params (string Key, string Type)[] fields) => new
    {
        project = new { key = "WEB", blueprintVersion = 1 },
        blueprint = new
        {
            fields = fields.Select(field => new { key = field.Key, name = field.Key, type = field.Type }).ToArray(),
            workflows = Array.Empty<object>(),
            itemTypes = Array.Empty<object>(),
        },
        myPermissions = new[] { "item.read" },
    };

    private static CliHarness LoggedIn() => new CliHarness().LoggedInByEnvironment();

    // ---- 翻頁 ----

    [Test]
    public async Task List_Cursor_IsSentAndTheHintTellsHowToContinue()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/projects/WEB", Project());
        cli.Server.On("GET", "/v1/projects/WEB/items", new { items = new[] { Row("WEB-3") }, nextCursor = "next-2" });

        // Act
        var result = await cli.RunAsync("item", "list", "-p", "WEB", "--cursor", "page-2");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cli.Server.To("GET", "/v1/projects/WEB/items").Single().Query("cursor"), Is.EqualTo("page-2"));
            Assert.That(result.Error, Does.Contain("--cursor next-2"));
        }
    }

    [Test]
    public async Task List_All_FollowsEveryPageAndJsonMergesThem()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/projects/WEB/items", request => request.Query("cursor") switch
        {
            null => FakeServer.Envelope(HttpStatusCode.OK, new { items = new[] { Row("WEB-1"), Row("WEB-2") }, nextCursor = "c2" }),
            "c2" => FakeServer.Envelope(HttpStatusCode.OK, new { items = new[] { Row("WEB-3") }, nextCursor = "c3" }),
            _ => FakeServer.Envelope(HttpStatusCode.OK, new { items = new[] { Row("WEB-4") }, nextCursor = (string?)null }),
        });

        // Act
        var result = await cli.RunAsync("item", "list", "-p", "WEB", "--all", "--json");

        // Assert
        var data = JsonNode.Parse(result.Out)!;
        var requests = cli.Server.To("GET", "/v1/projects/WEB/items");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(data["items"]!.AsArray().Select(item => (string?)item!["key"]), Is.EqualTo(new[] { "WEB-1", "WEB-2", "WEB-3", "WEB-4" }));
            Assert.That(data["nextCursor"], Is.Null);
            Assert.That(requests.Select(request => request.Query("cursor")), Is.EqualTo(new string?[] { null, "c2", "c3" }));
            Assert.That(requests.Select(request => request.Query("limit")).Distinct(), Is.EqualTo(new[] { "200" }), "翻頁時一頁用最大的");
        }
    }

    [Test]
    public async Task List_AllWithAnExplicitLimit_KeepsThatPageSizeAndPrintsATableWithoutAHint()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/projects/WEB", Project());
        cli.Server.On("GET", "/v1/projects/WEB/items", request => request.Query("cursor") is null
            ? FakeServer.Envelope(HttpStatusCode.OK, new { items = new[] { Row("WEB-1") }, nextCursor = "c2" })
            : FakeServer.Envelope(HttpStatusCode.OK, new { items = new[] { Row("WEB-2") }, nextCursor = (string?)null }));

        // Act
        var result = await cli.RunAsync("item", "list", "-p", "WEB", "--all", "--limit", "1");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cli.Server.To("GET", "/v1/projects/WEB/items").Select(request => request.Query("limit")), Is.All.EqualTo("1"));
            Assert.That(result.Out, Does.Contain("WEB-1").And.Contain("WEB-2"));
            Assert.That(result.Error, Is.Empty);
        }
    }

    [Test]
    public async Task List_AllAndTheServerKeepsRepeatingACursor_StopsWithAnError()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/projects/WEB/items", new { items = new[] { Row("WEB-1") }, nextCursor = "same" });

        // Act
        var result = await cli.RunAsync("item", "list", "-p", "WEB", "--all", "--json");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("重複"));
        Assert.That(cli.Server.To("GET", "/v1/projects/WEB/items"), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task View_WhenChildrenAndActivityAreTruncated_SaysSo()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/items/WEB-1", new { key = "WEB-1", projectKey = "WEB", title = "父", fields = new { } });
        cli.Server.On("GET", "/v1/projects/WEB", Project());
        cli.Server.On("GET", "/v1/projects/WEB/items", new { items = new[] { Row("WEB-2") }, nextCursor = "more" });
        cli.Server.On("GET", "/v1/items/WEB-1/activity", ActivityPages(12));

        // Act
        var result = await cli.RunAsync("item", "view", "WEB-1");

        // Assert
        Assert.That(result.Out, Does.Contain("還有更多下層，這裡只列出前 50 個").And.Contain("還有更早的活動，這裡只列出最近 10 筆"));
    }

    [Test]
    public async Task View_ActivityAcrossPages_ShowsTheNewestTenNewestFirst()
    {
        // Arrange：伺服器由舊到新、兩頁，共 12 筆留言（留言 1 最舊、留言 12 最新）
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/items/WEB-1", new { key = "WEB-1", projectKey = "WEB", title = "父", fields = new { } });
        cli.Server.On("GET", "/v1/projects/WEB", Project());
        cli.Server.On("GET", "/v1/projects/WEB/items", new { items = Array.Empty<object>(), nextCursor = (string?)null });
        cli.Server.On("GET", "/v1/items/WEB-1/activity", ActivityPages(12));

        // Act
        var text = await cli.RunAsync("item", "view", "WEB-1");
        var json = await cli.RunAsync("item", "view", "WEB-1", "--json");

        // Assert
        var activity = cli.Server.Requests.Where(r => r.Path == "/v1/items/WEB-1/activity").ToList();
        var shown = JsonDocument.Parse(json.Out).RootElement.GetProperty("activity").EnumerateArray().Select(a => a.GetProperty("comment").GetString()).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(activity.Take(2).Select(r => r.Query("cursor")), Is.EqualTo(new string?[] { null, "p2" }), "照游標讀到最後一頁");
            Assert.That(text.Out, Does.Contain("留言 12").And.Contain("留言 3").And.Not.Contain("留言 2\n").And.Contain("還有更早的活動"));
            Assert.That(text.Out.IndexOf("留言 12", StringComparison.Ordinal), Is.LessThan(text.Out.IndexOf("留言 3", StringComparison.Ordinal)));
            Assert.That(shown, Is.EqualTo(Enumerable.Range(3, 10).Reverse().Select(n => $"留言 {n}")));
        }
    }

    [Test]
    public async Task View_ServerKeepsGivingCursors_StopsAtThePageLimitAndWarns()
    {
        // Arrange：每一頁都說還有下一頁（伺服器出錯）
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/items/WEB-1", new { key = "WEB-1", projectKey = "WEB", title = "父", fields = new { } });
        cli.Server.On("GET", "/v1/projects/WEB", Project());
        cli.Server.On("GET", "/v1/projects/WEB/items", new { items = Array.Empty<object>(), nextCursor = (string?)null });
        cli.Server.On("GET", "/v1/items/WEB-1/activity", new { items = Array.Empty<object>(), nextCursor = "again" });

        // Act
        var result = await cli.RunAsync("item", "view", "WEB-1");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(cli.Server.Requests.Count(r => r.Path == "/v1/items/WEB-1/activity"), Is.EqualTo(50));
        Assert.That(result.Error, Does.Contain("只讀了前一萬筆"));
    }

    /// <summary>假的活動清單：由舊到新、每頁最多 10 筆，第一頁沒有游標、之後用 p2、p3…。</summary>
    private static Func<RecordedRequest, HttpResponseMessage> ActivityPages(int count) => request =>
    {
        var page = request.Query("cursor") is { } cursor ? int.Parse(cursor[1..], CultureInfo.InvariantCulture) : 1;
        var start = (page - 1) * 10 + 1;
        var items = Enumerable.Range(start, Math.Max(0, Math.Min(10, count - start + 1))).Select(n => new
        {
            kind = "commented",
            actor = new { displayName = "王" },
            at = $"2026-11-01T00:{n:00}:00Z",
            comment = $"留言 {n}",
            changes = Array.Empty<object>(),
        });
        var next = start + 10 <= count ? $"p{page + 1}" : null;
        return FakeServer.Envelope(HttpStatusCode.OK, new { items, nextCursor = next });
    };

    // ---- --set 依欄位型別 ----

    private async Task<(CliResult Result, JsonObject Fields)> CreateWithAsync(string set, params (string Key, string Type)[] fields)
    {
        var cli = LoggedIn();
        cli.Server.On("GET", "/v1/projects/WEB", Project(fields));
        cli.Server.On("POST", "/v1/projects/WEB/items", new { key = "WEB-9", title = "t" }, HttpStatusCode.Created);
        var result = await cli.RunAsync("item", "create", "-p", "WEB", "--type", "task", "-t", "標題", "--set", set);
        var body = cli.Server.To("POST", "/v1/projects/WEB/items").SingleOrDefault()?.Json;
        cli.Dispose();
        return (result, body?["fields"]?.AsObject() ?? []);
    }

    [TestCase("code=123", "text", "123")]
    [TestCase("code=true", "long_text", "true")]
    [TestCase("code=2026-12-01", "date", "2026-12-01")]
    [TestCase("code=https://example.com/1", "url", "https://example.com/1")]
    [TestCase("code=0x10", "single_select", "0x10")]
    [TestCase("code=\"quoted\"", "text", "\"quoted\"")]
    public async Task Set_StringTypedFields_AlwaysSendAString(string set, string type, string expected)
    {
        var (result, fields) = await CreateWithAsync(set, ("code", type));

        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(fields["code"]!.GetValueKind(), Is.EqualTo(JsonValueKind.String));
        Assert.That((string?)fields["code"], Is.EqualTo(expected));
    }

    [TestCase("budget=1200", "number")]
    [TestCase("budget=1200", "currency")]
    public async Task Set_NumberTypedFields_SendANumber(string set, string type)
    {
        var (result, fields) = await CreateWithAsync(set, ("budget", type));

        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(fields["budget"]!.GetValueKind(), Is.EqualTo(JsonValueKind.Number));
        Assert.That((decimal?)fields["budget"], Is.EqualTo(1200m));
    }

    [Test]
    public async Task Set_CheckboxAndMultiSelect_ParseTheirOwnShapes()
    {
        var (checkbox, boxFields) = await CreateWithAsync("done=FALSE", ("done", "checkbox"));
        var (jsonList, listFields) = await CreateWithAsync("tags=[\"a\",\"b\"]", ("tags", "multi_select"));
        var (commaList, commaFields) = await CreateWithAsync("tags=a, b ,c", ("tags", "multi_select"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(checkbox.ExitCode, Is.EqualTo(ExitCodes.Success), checkbox.Error);
            Assert.That(boxFields["done"]!.GetValueKind(), Is.EqualTo(JsonValueKind.False));
            Assert.That(jsonList.ExitCode, Is.EqualTo(ExitCodes.Success), jsonList.Error);
            Assert.That(listFields["tags"]!.AsArray().Select(option => (string?)option), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(commaFields["tags"]!.AsArray().Select(option => (string?)option), Is.EqualTo(new[] { "a", "b", "c" }));
        }
    }

    [TestCase("budget=abc", "number", "要是數字")]
    [TestCase("done=yes", "checkbox", "只收 true 或 false")]
    [TestCase("tags=[1,2]", "multi_select", "多選欄位")]
    [TestCase("tags=[oops", "multi_select", "多選欄位")]
    public async Task Set_ValueThatDoesNotFitTheFieldType_IsAUsageErrorBeforeCreating(string set, string type, string expected)
    {
        var key = set[..set.IndexOf('=')];
        var (result, _) = await CreateWithAsync(set, (key, type));

        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain(expected));
    }

    [Test]
    public async Task Set_NullClearsAnyFieldAndAnUnknownFieldFallsBackToGuessing()
    {
        var (cleared, clearedFields) = await CreateWithAsync("note=null", ("note", "text"));
        var (guessed, guessedFields) = await CreateWithAsync("mystery=42");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cleared.ExitCode, Is.EqualTo(ExitCodes.Success), cleared.Error);
            Assert.That(clearedFields.ContainsKey("note") && clearedFields["note"] is null, Is.True);
            Assert.That(guessed.ExitCode, Is.EqualTo(ExitCodes.Success), guessed.Error);
            Assert.That((int?)guessedFields["mystery"], Is.EqualTo(42));
        }
    }

    [Test]
    public async Task Set_WhenTheBlueprintCannotBeRead_FallsBackToGuessing()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.OnError("GET", "/v1/projects/WEB", HttpStatusCode.Forbidden, "auth.insufficient_scope", "權杖的範圍不夠。");
        cli.Server.On("PATCH", "/v1/items/WEB-1", new { key = "WEB-1", title = "t" });

        // Act
        var result = await cli.RunAsync("item", "update", "WEB-1", "--set", "budget=7");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That((int?)cli.Server.To("PATCH", "/v1/items/WEB-1").Single().Json!["fields"]!["budget"], Is.EqualTo(7));
    }

    [Test]
    public async Task Set_OnlyCoreProperties_DoesNotLookTheBlueprintUp()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("PATCH", "/v1/items/WEB-1", new { key = "WEB-1", title = "t" });

        // Act
        var result = await cli.RunAsync("item", "update", "WEB-1", "--set", "priority=high", "--set", "estimate=null");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(cli.Server.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Update_ItemKeyWithoutAProjectPrefix_StillWorksWithTheOldGuessing()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("PATCH", "/v1/items/nodash", new { key = "nodash", title = "t" });

        // Act
        var result = await cli.RunAsync("item", "update", "nodash", "--set", "budget=7");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(cli.Server.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Project_RolesAreShownInChinese_AndACustomRoleIsShownAsIs()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/projects", new[]
        {
            new { key = "A", name = "甲", blueprintVersion = 1, myRole = "viewer" },
            new { key = "B", name = "乙", blueprintVersion = 1, myRole = "qa_lead" },
        });

        // Act
        var result = await cli.RunAsync("project", "list");

        // Assert
        Assert.That(result.Out, Does.Contain("檢視者").And.Contain("qa_lead"));
    }
}
