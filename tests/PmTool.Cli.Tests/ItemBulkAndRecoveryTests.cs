using System.Net;
using System.Text.Json.Nodes;

namespace PmTool.Cli.Tests;

/// <summary>刪除、復原、已刪除的清單，以及多個編號的改狀態與修改。</summary>
[TestFixture]
public sealed class ItemBulkAndRecoveryTests
{
    private static CliHarness LoggedIn() => new CliHarness().LoggedInByEnvironment();

    private static object Row(string key, string state = "doing") =>
        new { key, projectKey = "WEB", type = "task", title = $"標題 {key}", state, stateCategory = "in_progress" };

    private static object Failure(string key, string message) =>
        new { key, error = new { code = "item.has_children", message, details = Array.Empty<object>() } };

    private static object Project() => new
    {
        project = new { key = "WEB", blueprintVersion = 1 },
        blueprint = new
        {
            fields = Array.Empty<object>(),
            workflows = new[] { new { key = "flow", states = new[] { new { key = "doing", name = "進行中" } } } },
            itemTypes = new[] { new { key = "task", name = "任務", workflow = "flow" } },
        },
        myPermissions = new[] { "item.read" },
    };

    // ---- 刪除 ----

    [Test]
    public async Task Delete_OneKey_UsesTheSingleEndpoint()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("DELETE", "/v1/items/WEB-12", new { key = "WEB-12", deletedAt = "2026-11-18T01:00:00Z" });

        // Act
        var result = await cli.RunAsync("item", "delete", "WEB-12");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Out, Does.Contain("已刪除 WEB-12"));
            Assert.That(cli.Server.Requests, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Delete_SeveralKeys_UsesBulkDeleteWithTheKeysInOrder()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("POST", "/v1/projects/WEB/items/bulk-delete", new
        {
            deleted = new[] { new { key = "WEB-12", deletedAt = "2026-11-18T01:00:00Z" }, new { key = "WEB-13", deletedAt = "2026-11-18T01:00:00Z" } },
            failed = Array.Empty<object>(),
        });

        // Act
        var result = await cli.RunAsync("item", "delete", "WEB-12", "WEB-13");

        // Assert
        var body = cli.Server.To("POST", "/v1/projects/WEB/items/bulk-delete").Single().Json!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(body["keys"]!.AsArray().Select(key => (string?)key), Is.EqualTo(new[] { "WEB-12", "WEB-13" }));
            Assert.That(result.Out, Does.Contain("已刪除 WEB-12").And.Contain("已刪除 WEB-13"));
        }
    }

    [Test]
    public async Task Delete_SameProjectWrittenInDifferentCase_IsOneProjectLikeTheServerSees()
    {
        // Arrange：伺服器的專案代號不分大小寫（ProjectKeys.TryNormalize），web-2 和 WEB-1 是同一個專案。
        using var cli = LoggedIn();
        cli.Server.On("POST", "/v1/projects/WEB/items/bulk-delete", new
        {
            deleted = new[]
            {
                new { key = "WEB-1", deletedAt = "2026-11-18T01:00:00Z" },
                new { key = "WEB-2", deletedAt = "2026-11-18T01:00:00Z" },
            },
            failed = Array.Empty<object>(),
        });

        // Act
        var result = await cli.RunAsync("item", "delete", "WEB-1", "web-2");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(cli.Server.To("POST", "/v1/projects/WEB/items/bulk-delete"), Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Delete_KeysFromDifferentProjects_ExitsWith2AndSendsNothing()
    {
        // Arrange
        using var cli = LoggedIn();

        // Act
        var result = await cli.RunAsync("item", "delete", "WEB-1", "AGY-2");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(result.Error, Does.Contain("同一個專案"));
            Assert.That(cli.Server.Requests, Is.Empty);
        }
    }

    [Test]
    public async Task Delete_PartialFailure_PrintsSuccessesToOutFailuresToErrorAndExitsWith2()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("POST", "/v1/projects/WEB/items/bulk-delete", new
        {
            deleted = new[] { new { key = "WEB-1", deletedAt = "2026-11-18T01:00:00Z" } },
            failed = new[] { Failure("WEB-3", "還有下層項目，不能刪除。") },
        });

        // Act
        var result = await cli.RunAsync("item", "delete", "WEB-1", "WEB-3");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(result.Out, Does.Contain("已刪除 WEB-1").And.Not.Contain("WEB-3"));
            Assert.That(result.Error, Does.Contain("WEB-3：還有下層項目，不能刪除。"));
        }
    }

    [Test]
    public async Task Delete_BulkJson_PrintsTheRawDataAndStillExitsWith2OnFailure()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("POST", "/v1/projects/WEB/items/bulk-delete", new
        {
            deleted = new[] { new { key = "WEB-1", deletedAt = "2026-11-18T01:00:00Z" } },
            failed = new[] { Failure("WEB-3", "擋下") },
        });

        // Act
        var result = await cli.RunAsync("item", "delete", "WEB-1", "WEB-3", "--json");

        // Assert
        var data = JsonNode.Parse(result.Out)!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That((string?)data["deleted"]![0]!["key"], Is.EqualTo("WEB-1"));
            Assert.That((string?)data["failed"]![0]!["error"]!["code"], Is.EqualTo("item.has_children"));
        }
    }

    // ---- 復原 ----

    [Test]
    public async Task Restore_OneKey_UsesTheSingleEndpointWithoutABody()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("POST", "/v1/items/WEB-12/restore", Row("WEB-12"));

        // Act
        var result = await cli.RunAsync("item", "restore", "WEB-12");

        // Assert
        var request = cli.Server.To("POST", "/v1/items/WEB-12/restore").Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(request.Body, Is.Empty);
            Assert.That(result.Out, Does.Contain("已復原 WEB-12：標題 WEB-12"));
        }
    }

    [Test]
    public async Task Restore_SingleRuleFailure_ShowsTheServerMessageAndExitsWith1()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.OnError("POST", "/v1/items/WEB-12/restore", HttpStatusCode.Conflict, "item.parent_deleted", "上層 WEB-3 也已刪除，先復原它。");

        // Act
        var result = await cli.RunAsync("item", "restore", "WEB-12");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("上層 WEB-3 也已刪除，先復原它。"));
        }
    }

    [Test]
    public async Task Restore_SeveralKeys_UsesBulkRestoreAndReportsFailures()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("POST", "/v1/projects/WEB/items/bulk-restore", new
        {
            restored = new[] { Row("WEB-1") },
            failed = new[] { Failure("WEB-2", "上層 WEB-9 也已刪除，先復原它。") },
        });

        // Act
        var result = await cli.RunAsync("item", "restore", "WEB-1", "WEB-2");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(result.Out, Does.Contain("已復原 WEB-1"));
            Assert.That(result.Error, Does.Contain("WEB-2：上層 WEB-9 也已刪除，先復原它。"));
        }
    }

    // ---- 已刪除的清單 ----

    private static object Deleted(string key, string? by = "小明") => new
    {
        key, projectKey = "WEB", number = 1, type = "task", title = $"標題 {key}", state = "doing", stateCategory = "in_progress",
        parentKey = (string?)null, deletedBy = by is null ? null : new { personId = Guid.NewGuid(), displayName = by }, deletedAt = "2026-11-18T01:00:00Z",
    };

    [Test]
    public async Task Deleted_PrintsNamesAndTheDeleter()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/projects/WEB", Project());
        cli.Server.On("GET", "/v1/projects/WEB/items/deleted", new { items = new[] { Deleted("WEB-5") }, nextCursor = (string?)null });

        // Act
        var result = await cli.RunAsync("item", "deleted", "-p", "WEB");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Out, Does.Contain("WEB-5").And.Contain("任務").And.Contain("進行中").And.Contain("小明"));
            Assert.That(result.Out, Does.Not.Contain("personId"));
        }
    }

    [Test]
    public async Task Deleted_LimitAndCursor_AreSentAndTheHintTellsHowToContinue()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/projects/WEB", Project());
        cli.Server.On("GET", "/v1/projects/WEB/items/deleted", new { items = new[] { Deleted("WEB-5", by: null) }, nextCursor = "next-2" });

        // Act
        var result = await cli.RunAsync("item", "deleted", "-p", "WEB", "--limit", "10", "--cursor", "page-2");

        // Assert
        var request = cli.Server.To("GET", "/v1/projects/WEB/items/deleted").Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.Query("limit"), Is.EqualTo("10"));
            Assert.That(request.Query("cursor"), Is.EqualTo("page-2"));
            Assert.That(result.Error, Does.Contain("--cursor next-2"));
        }
    }

    [Test]
    public async Task Deleted_AllWithJson_FollowsEveryPageAndMergesThem()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/projects/WEB/items/deleted", request => request.Query("cursor") switch
        {
            null => FakeServer.Envelope(HttpStatusCode.OK, new { items = new[] { Deleted("WEB-2") }, nextCursor = "c2" }),
            _ => FakeServer.Envelope(HttpStatusCode.OK, new { items = new[] { Deleted("WEB-1") }, nextCursor = (string?)null }),
        });

        // Act
        var result = await cli.RunAsync("item", "deleted", "-p", "WEB", "--all", "--json");

        // Assert
        var data = JsonNode.Parse(result.Out)!;
        var requests = cli.Server.To("GET", "/v1/projects/WEB/items/deleted");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(data["items"]!.AsArray().Select(item => (string?)item!["key"]), Is.EqualTo(new[] { "WEB-2", "WEB-1" }));
            Assert.That(data["nextCursor"], Is.Null);
            Assert.That(requests.Select(request => request.Query("limit")).Distinct(), Is.EqualTo(new[] { "200" }));
        }
    }

    // ---- 多個編號的 move 與 update ----

    [Test]
    public async Task Move_SeveralKeys_UsesBulkUpdateWithTheStateAndPrintsNames()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("POST", "/v1/projects/WEB/items/bulk-update", new { updated = new[] { Row("WEB-1"), Row("WEB-2") }, failed = Array.Empty<object>() });
        cli.Server.On("GET", "/v1/projects/WEB", Project());

        // Act
        var result = await cli.RunAsync("item", "move", "WEB-1", "WEB-2", "--to", "doing");

        // Assert
        var body = cli.Server.To("POST", "/v1/projects/WEB/items/bulk-update").Single().Json!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That((string?)body["state"], Is.EqualTo("doing"));
            Assert.That(body["keys"]!.AsArray(), Has.Count.EqualTo(2));
            Assert.That(result.Out, Does.Contain("WEB-1 已移到「進行中」。").And.Contain("WEB-2 已移到「進行中」。"));
        }
    }

    [Test]
    public async Task Move_OneKey_StillUsesTheTransitionEndpoint()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("POST", "/v1/items/WEB-1/transitions", Row("WEB-1"));
        cli.Server.On("GET", "/v1/projects/WEB", Project());

        // Act
        var result = await cli.RunAsync("item", "move", "WEB-1", "--to", "doing");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
    }

    [Test]
    public async Task Move_PartialFailure_ExitsWith2AndPrintsTheReasonPerKey()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("POST", "/v1/projects/WEB/items/bulk-update", new
        {
            updated = new[] { Row("WEB-1") },
            failed = new[] { Failure("WEB-2", "不能從「完成」直接移到「進行中」。") },
        });
        cli.Server.On("GET", "/v1/projects/WEB", Project());

        // Act
        var result = await cli.RunAsync("item", "move", "WEB-1", "WEB-2", "--to", "doing");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(result.Error, Does.Contain("WEB-2：不能從「完成」直接移到「進行中」。"));
        }
    }

    [Test]
    public async Task Move_KeysFromDifferentProjects_ExitsWith2AndSendsNothing()
    {
        // Arrange
        using var cli = LoggedIn();

        // Act
        var result = await cli.RunAsync("item", "move", "WEB-1", "AGY-1", "--to", "doing");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(cli.Server.Requests, Is.Empty);
        }
    }

    [Test]
    public async Task Update_SeveralKeys_SendsOnlyTheAllowedPropertiesToBulkUpdate()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("GET", "/v1/session", new { user = new { personId = "11111111-1111-1111-1111-111111111111" } });
        cli.Server.On("POST", "/v1/projects/WEB/items/bulk-update", new { updated = new[] { Row("WEB-1"), Row("WEB-2") }, failed = Array.Empty<object>() });

        // Act
        var result = await cli.RunAsync("item", "update", "WEB-1", "WEB-2", "--assignee", "@me", "--set", "priority=high", "--set", "dueDate=2026-12-01");

        // Assert
        var body = cli.Server.To("POST", "/v1/projects/WEB/items/bulk-update").Single().Json!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That((string?)body["assigneeId"], Is.EqualTo("11111111-1111-1111-1111-111111111111"));
            Assert.That((string?)body["priority"], Is.EqualTo("high"));
            Assert.That((string?)body["dueDate"], Is.EqualTo("2026-12-01"));
            Assert.That(result.Out, Does.Contain("已更新 WEB-1。").And.Contain("已更新 WEB-2。"));
        }
    }

    [Test]
    public async Task Update_SeveralKeysWithNullAndNone_ClearsTheProperties()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("POST", "/v1/projects/WEB/items/bulk-update", new { updated = new[] { Row("WEB-1") }, failed = Array.Empty<object>() });

        // Act
        var result = await cli.RunAsync("item", "update", "WEB-1", "WEB-2", "--assignee", "none", "--set", "dueDate=null");

        // Assert
        var body = cli.Server.To("POST", "/v1/projects/WEB/items/bulk-update").Single().Json!.AsObject();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(body.ContainsKey("assigneeId") && body["assigneeId"] is null, Is.True);
            Assert.That(body.ContainsKey("dueDate") && body["dueDate"] is null, Is.True);
        }
    }

    [TestCase("--title", "新標題")]
    [TestCase("--set", "description=說明")]
    [TestCase("--set", "parentKey=WEB-9")]
    [TestCase("--set", "budget=1200")]
    public async Task Update_SeveralKeysWithADisallowedProperty_ExitsWith2AndSendsNothing(string option, string value)
    {
        // Arrange
        using var cli = LoggedIn();

        // Act
        var result = await cli.RunAsync("item", "update", "WEB-1", "WEB-2", option, value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(result.Error, Does.Contain("一次改多個項目時只能改負責人、優先度、開始日、到期日"));
            Assert.That(cli.Server.Requests, Is.Empty);
        }
    }

    [Test]
    public async Task Update_SeveralKeysWithNothingToChange_ExitsWith2()
    {
        // Arrange
        using var cli = LoggedIn();

        // Act
        var result = await cli.RunAsync("item", "update", "WEB-1", "WEB-2");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(cli.Server.Requests, Is.Empty);
        }
    }

    [Test]
    public async Task Update_OneKey_StillUsesPatch()
    {
        // Arrange
        using var cli = LoggedIn();
        cli.Server.On("PATCH", "/v1/items/WEB-1", Row("WEB-1"));

        // Act
        var result = await cli.RunAsync("item", "update", "WEB-1", "--title", "新標題");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
    }

    // ---- 說明 ----

    [Test]
    public async Task Help_ListsTheNewItemCommands()
    {
        // Arrange
        using var cli = LoggedIn();

        // Act
        var overview = await cli.RunAsync("--help");
        var group = await cli.RunAsync("item", "--help");
        var detail = await cli.RunAsync("item", "deleted", "--help");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(overview.Out, Does.Contain("item delete").And.Contain("item restore").And.Contain("item deleted"));
            Assert.That(group.Out, Does.Contain("item delete").And.Contain("item restore").And.Contain("item deleted"));
            Assert.That(detail.Out, Does.Contain("--limit").And.Contain("--cursor").And.Contain("--all"));
        }
    }
}
