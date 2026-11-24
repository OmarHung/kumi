using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>改項目的工具：只動有給的欄位、權限與規則交給模組、模組的失敗原樣說給代理聽。</summary>
[TestFixture]
public sealed class McpWriteToolTests
{
    [Test]
    public async Task CreateItem_MapsEveryArgumentIntoTheNewItem_AsTheAuthorizedPerson()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();
        mcp.Modules.Items.OnCreate = (_, _, item) => TestData.Item("WEB-7") with { Type = item.Type, Title = item.Title };

        // Act
        var (isError, body) = await mcp.CallAsync("create_item", new
        {
            projectKey = "WEB",
            type = "task",
            title = "寫首頁",
            description = "## 說明",
            assignee = TestUsers.Owner.Email,
            priority = "urgent",
            startDate = "2026-11-02",
            dueDate = "2026-11-20",
            parentKey = "WEB-3",
            fields = new { impact = "high", done = true },
        });

        // Assert
        var (actor, projectKey, item) = mcp.Modules.Items.CreateCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False);
            Assert.That(actor, Is.EqualTo(TestUsers.Member.ToActor()));
            Assert.That(projectKey, Is.EqualTo("WEB"));
            Assert.That(item.Type, Is.EqualTo("task"));
            Assert.That(item.Title, Is.EqualTo("寫首頁"));
            Assert.That(item.Description, Is.EqualTo("## 說明"));
            Assert.That(item.AssigneeId, Is.EqualTo(TestUsers.Owner.PersonId));
            Assert.That(item.Priority, Is.EqualTo(Priority.Urgent));
            Assert.That(item.StartDate, Is.EqualTo(new DateOnly(2026, 11, 2)));
            Assert.That(item.DueDate, Is.EqualTo(new DateOnly(2026, 11, 20)));
            Assert.That(item.ParentKey, Is.EqualTo("WEB-3"));
            Assert.That(item.Fields["impact"].GetString(), Is.EqualTo("high"));
            Assert.That(item.Fields["done"].ValueKind, Is.EqualTo(JsonValueKind.True));
            Assert.That(body["item"]!["key"]!.GetValue<string>(), Is.EqualTo("WEB-7"));
        }
    }

    [Test]
    public async Task CreateItem_OnlyTheRequiredArguments_LeavesTheRestUnset()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();

        // Act
        await mcp.CallAsync("create_item", new { projectKey = "WEB", type = "task", title = "最少的" });

        // Assert
        var item = mcp.Modules.Items.CreateCalls.Single().Item;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(item.AssigneeId, Is.Null);
            Assert.That(item.Priority, Is.EqualTo(Priority.None));
            Assert.That(item.Description, Is.Null);
            Assert.That(item.Fields, Is.Empty);
        }
    }

    [Test]
    public async Task CreateItem_ModuleRefuses_ReturnsEachIssueSoTheAgentCanFixIt()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();
        mcp.Modules.Items.OnCreate = (_, _, _) => new Failure(FailureKind.Invalid, WorkErrorCodes.ItemInvalidValue, "項目的內容不合格。")
        {
            Details = [new FailureDetail("item.title_required", "title", "標題不能空白。")],
        };

        // Act
        var (isError, body) = await mcp.CallAsync("create_item", new { projectKey = "WEB", type = "task", title = "x" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["issues"]![0]!["path"]!.GetValue<string>(), Is.EqualTo("title"));
            Assert.That(body["issues"]![0]!["message"]!.GetValue<string>(), Is.EqualTo("標題不能空白。"));
        }
    }

    [Test]
    public async Task CreateItem_ActorWithoutPermission_GetsTheModulesForbiddenMessage()
    {
        // Arrange：範圍只是上限，真正的權限由模組用授權的人的角色判斷。
        await using var mcp = await McpHarness.StartAsync();
        mcp.Modules.Items.OnCreate = (_, _, _) => new Failure(FailureKind.Forbidden, WorkErrorCodes.Forbidden, "你沒有在這個專案建立項目的權限。");

        // Act
        var (isError, body) = await mcp.CallAsync("create_item", new { projectKey = "WEB", type = "task", title = "x" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["code"]!.GetValue<string>(), Is.EqualTo(WorkErrorCodes.Forbidden));
        }
    }

    [Test]
    public async Task UpdateItem_OnlyTheGivenFieldsAreInThePatch()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();

        // Act
        var (isError, body) = await mcp.CallAsync("update_item", new { itemKey = "WEB-1", title = "新標題", priority = "high" });

        // Assert
        var patch = mcp.Modules.Items.UpdateCalls.Single().Patch;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False);
            Assert.That(patch.Title, Is.EqualTo(new Optional<string>("新標題")));
            Assert.That(patch.Priority, Is.EqualTo(new Optional<Priority>(Priority.High)));
            Assert.That(patch.Description.HasValue, Is.False);
            Assert.That(patch.AssigneeId.HasValue, Is.False);
            Assert.That(patch.DueDate.HasValue, Is.False);
            Assert.That(patch.ParentKey.HasValue, Is.False);
            Assert.That(patch.Fields, Is.Empty);
            Assert.That(body["item"]!["key"]!.GetValue<string>(), Is.EqualTo("WEB-1"));
        }
    }

    [Test]
    public async Task UpdateItem_NullClearsAField_ButADescriptionRevisionRidesAlong()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();

        // Act
        await mcp.CallAsync("update_item", new
        {
            itemKey = "WEB-1",
            assignee = (string?)null,
            dueDate = (string?)null,
            parentKey = (string?)null,
            description = (string?)null,
            expectedDescriptionRevision = "abc123",
            fields = new { impact = (string?)null },
        });

        // Assert
        var patch = mcp.Modules.Items.UpdateCalls.Single().Patch;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patch.AssigneeId, Is.EqualTo(new Optional<Guid?>(null)));
            Assert.That(patch.DueDate, Is.EqualTo(new Optional<DateOnly?>(null)));
            Assert.That(patch.ParentKey, Is.EqualTo(new Optional<string?>(null)));
            Assert.That(patch.Description, Is.EqualTo(new Optional<string?>(null)));
            Assert.That(patch.ExpectedDescriptionRevision, Is.EqualTo("abc123"));
            Assert.That(patch.Fields["impact"].ValueKind, Is.EqualTo(JsonValueKind.Null));
        }
    }

    [Test]
    public async Task UpdateItem_AssigneeNoneAndMe_UnassignsAndAssignsTheActor()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();

        // Act
        await mcp.CallAsync("update_item", new { itemKey = "WEB-1", assignee = "none" });
        await mcp.CallAsync("update_item", new { itemKey = "WEB-1", assignee = "me" });

        // Assert
        var patches = mcp.Modules.Items.UpdateCalls.Select(call => call.Patch).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patches[0].AssigneeId, Is.EqualTo(new Optional<Guid?>(null)));
            Assert.That(patches[1].AssigneeId, Is.EqualTo(new Optional<Guid?>(TestUsers.Member.PersonId)));
        }
    }

    [Test]
    public async Task UpdateItem_NothingToChange_IsAToolErrorBeforeTheModule()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();

        // Act
        var (isError, body) = await mcp.CallAsync("update_item", new { itemKey = "WEB-1" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]!.GetValue<string>(), Does.Contain("沒有要修改"));
            Assert.That(mcp.Modules.Items.UpdateCalls, Is.Empty);
        }
    }

    [Test]
    public async Task UpdateItem_DescriptionConflict_IsReportedAndNothingIsHidden()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();
        mcp.Modules.Items.OnUpdate = (_, _, _) => new Failure(FailureKind.Conflict, WorkErrorCodes.ItemDescriptionConflict, "說明已經被別人改過。");

        // Act
        var (isError, body) = await mcp.CallAsync("update_item", new { itemKey = "WEB-1", description = "新", expectedDescriptionRevision = "old" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["code"]!.GetValue<string>(), Is.EqualTo(WorkErrorCodes.ItemDescriptionConflict));
        }
    }

    [Test]
    public async Task UpdateItem_StateIsNotAnArgument_BecauseStatesGoThroughMoveItem()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();

        // Act
        var (isError, body) = await mcp.CallAsync("update_item", new { itemKey = "WEB-1", state = "done" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]!.GetValue<string>(), Does.Contain("state"));
            Assert.That(mcp.Modules.Items.UpdateCalls, Is.Empty);
        }
    }

    [Test]
    public async Task MoveItem_Transitions_AndAFailureCarriesTheReasonsInTheDetails()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();

        // Act
        var ok = await mcp.CallAsync("move_item", new { itemKey = "WEB-1", state = "doing" });
        mcp.Modules.Items.OnTransition = (_, _, _) => new Failure(FailureKind.Invalid, WorkErrorCodes.TransitionNotAllowed, "不能這樣轉換。")
        {
            Details = [new FailureDetail(WorkErrorCodes.TransitionNotAllowed, "state", "流程沒有 todo 到 done 的轉換。")],
        };
        var refused = await mcp.CallAsync("move_item", new { itemKey = "WEB-1", state = "done" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ok.IsError, Is.False);
            Assert.That(mcp.Modules.Items.TransitionCalls[0], Is.EqualTo((TestUsers.Member.ToActor(), "WEB-1", "doing")));
            Assert.That(refused.IsError, Is.True);
            Assert.That(refused.Body["issues"]![0]!["message"]!.GetValue<string>(), Does.Contain("沒有 todo 到 done"));
        }
    }

    [Test]
    public async Task AddComment_PostsTheBodyAsTheAuthorizedPersonWithoutAReply()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();

        // Act
        var (isError, body) = await mcp.CallAsync("add_comment", new { itemKey = "WEB-1", body = "收到，我來處理。" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False);
            Assert.That(mcp.Modules.Activity.CommentCalls.Single(), Is.EqualTo((TestUsers.Member.ToActor(), "WEB-1", "收到，我來處理。")));
            Assert.That(mcp.Modules.Activity.ReplyToCalls.Single(), Is.Null);
            Assert.That(body["comment"]!["comment"]!.GetValue<string>(), Is.EqualTo("收到，我來處理。"));
            Assert.That(body["comment"]!["actor"]!["displayName"]!.GetValue<string>(), Is.EqualTo(TestUsers.Member.DisplayName));
        }
    }

    [Test]
    public async Task AddComment_ItemTheActorCannotSee_IsNotFound()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();
        mcp.Modules.Activity.OnAddComment = (_, _, _) => new Failure(FailureKind.NotFound, WorkErrorCodes.ItemNotFound, "找不到這個項目。");

        // Act
        var (isError, body) = await mcp.CallAsync("add_comment", new { itemKey = "SEC-1", body = "x" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]!.GetValue<string>(), Is.EqualTo("找不到這個項目。"));
        }
    }

    [Test]
    public async Task WriteTools_ATokenWithOnlyReadScopes_CannotWrite()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync(AccessTokenScope.ItemsRead, AccessTokenScope.BlueprintRead);

        // Act
        var read = await mcp.CallAsync("get_item", new { itemKey = "WEB-1" });
        var write = await mcp.CallAsync("add_comment", new { itemKey = "WEB-1", body = "x" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.IsError, Is.False);
            Assert.That(write.IsError, Is.True);
            Assert.That(write.Body["error"]!.GetValue<string>(), Does.Contain("items:write").And.Contain("建立與修改項目"));
            Assert.That(mcp.Modules.Activity.CommentCalls, Is.Empty);
        }
    }
}
