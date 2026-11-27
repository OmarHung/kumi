using PmTool.Abstractions;
using PmTool.Ai.Actions;
using PmTool.Knowledge;

namespace PmTool.Ai.Tests.Actions.Knowledge;

/// <summary>知識庫頁面的動作建議：新增、修改、搬移、刪除、復原；建立時的檢查與卡片步驟，套用時的呼叫。</summary>
[TestFixture]
internal sealed class KnowledgePageActionTests : KnowledgeActionTestBase
{
    [Test]
    public async Task CreatePage_UnderAnExistingPage_ShowsTheStepAndCreatesItWithTheRightPosition()
    {
        // Arrange
        var payload = Payload(new { action = "create_page", spaceKey = "hr", title = "請假規則", body = "# 請假", parentNumber = 2, beforeNumber = (int?)null });

        // Act
        var prepared = await PrepareOkAsync(payload);
        var result = await ApplyAsync(prepared, 0);

        // Assert
        var step = prepared.Steps.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prepared.Title, Is.EqualTo("建議調整知識庫（HR）"));
            Assert.That((step.Label, step.Target, step.After, step.Format), Is.EqualTo(("新增頁面", "HR／請假規則", "# 請假", ActionTextFormat.Markdown)));
            Assert.That(step.Note, Does.Contain("新進人員"));
            Assert.That(result.Failure, Is.Null);
            Assert.That(result.Link, Is.EqualTo("/knowledge/HR/40"));
            Assert.That(Pages.Created.Single(), Is.EqualTo(new NewPage(2, "請假規則", "# 請假")));
        }
    }

    [Test]
    public async Task CreatePage_WithRefAndParentRef_HangsTheChildUnderThePageTheFirstStepReallyCreated()
    {
        // Arrange
        var payload = Payload(
            new { action = "create_page", spaceKey = "HR", title = "總覽", @ref = "a" },
            new { action = "create_page", spaceKey = "HR", title = "細節", parentRef = "a" });

        // Act
        var prepared = await PrepareOkAsync(payload);
        await ApplyAsync(prepared, 0);
        var second = await ApplyAsync(prepared, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prepared.Steps[1].Note, Does.Contain("總覽"));
            Assert.That(second.Failure, Is.Null);
            Assert.That(Pages.Created[1].ParentNumber, Is.EqualTo(40));
        }
    }

    [Test]
    public async Task CreatePage_WhenTheParentStepFailed_SkipsTheChildWithoutCallingTheService()
    {
        // Arrange
        var prepared = await PrepareOkAsync(Payload(
            new { action = "create_page", spaceKey = "HR", title = "總覽", @ref = "a" },
            new { action = "create_page", spaceKey = "HR", title = "細節", parentRef = "a" }));
        Pages.OnCreate = _ => new Failure(FailureKind.Conflict, KnowledgeErrorCodes.PageLimitReached, "滿了");

        // Act
        var first = await ApplyAsync(prepared, 0);
        var second = await ApplyAsync(prepared, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Failure!.Code, Is.EqualTo(KnowledgeErrorCodes.PageLimitReached));
            Assert.That(second.Failure!.Message, Does.Contain("跳過"));
            Assert.That(Pages.Created, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task CreatePage_BadReferencesAndPositions_AreReportedTogetherWithTheirPaths()
    {
        // Arrange
        var payload = Payload(
            new { action = "create_page", spaceKey = "HR", title = "甲", parentNumber = 99 },
            new { action = "create_page", spaceKey = "HR", title = "乙", parentRef = "nope" },
            new { action = "create_page", spaceKey = "HR", title = "丙", parentNumber = 2, beforeNumber = 4 },
            new { action = "create_page", spaceKey = "NOPE", title = "丁" });

        // Act
        var result = await PrepareAsync(payload);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
            Assert.That(IssuePaths(result), Is.EqualTo(new[] { "changes[0].parentNumber", "changes[1].parentRef", "changes[2].beforeNumber", "changes[3].spaceKey" }));
        }
    }

    [Test]
    public async Task CreatePage_BeyondTheMaximumDepth_IsRejected()
    {
        // Arrange：頁 1→2→3 是三層，再往下放十層就超過。
        var changes = new List<object> { new { action = "create_page", spaceKey = "HR", title = "層0", @ref = "r0", parentNumber = 3 } };
        for (var i = 1; i <= KnowledgeRules.MaxDepth; i++)
        {
            changes.Add(new { action = "create_page", spaceKey = "HR", title = $"層{i}", @ref = $"r{i}", parentRef = $"r{i - 1}" });
        }

        // Act
        var result = await PrepareAsync(Payload([.. changes]));

        // Assert
        Assert.That(result.Failure!.Details.Any(d => d.Message.Contains("層")), Is.True);
    }

    [Test]
    public async Task CreatePage_SecretInBody_IsRejected()
    {
        // Arrange
        var payload = Payload(new { action = "create_page", spaceKey = "HR", title = "金鑰", body = "-----BEGIN RSA PRIVATE KEY-----" });

        // Act
        var result = await PrepareAsync(payload);

        // Assert
        Assert.That(IssuePaths(result), Is.EqualTo(new[] { "changes[0].body" }));
    }

    [Test]
    public async Task UpdatePage_WithTheCurrentRevision_ShowsBeforeAndAfterAndWritesWithThatRevision()
    {
        // Arrange
        var payload = Payload(new { action = "update_page", spaceKey = "HR", number = 2, body = "新內文", title = "新標題", expectedRevision = 3 });

        // Act
        var prepared = await PrepareOkAsync(payload);
        var result = await ApplyAsync(prepared, 0);

        // Assert
        var step = prepared.Steps.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That((step.Label, step.Before, step.After, step.Format), Is.EqualTo(("改頁面內容", "舊內文", "新內文", ActionTextFormat.Markdown)));
            Assert.That(step.Note, Does.Contain("新標題"));
            Assert.That(result.Link, Is.EqualTo("/knowledge/HR/2"));
            Assert.That(Pages.Updated.Single(), Is.EqualTo((2, new PageChanges("新標題", "新內文", 3))));
        }
    }

    [Test]
    public async Task UpdatePage_TitleOnly_IsATitleStepAndDoesNotSendTheBody()
    {
        // Arrange
        var payload = Payload(new { action = "update_page", spaceKey = "HR", number = 2, title = "新標題", expectedRevision = 3 });

        // Act
        var prepared = await PrepareOkAsync(payload);
        await ApplyAsync(prepared, 0);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prepared.Steps.Single().Label, Is.EqualTo("改頁面標題"));
            Assert.That(Pages.Updated.Single().Changes, Is.EqualTo(new PageChanges("新標題", null, 3)));
        }
    }

    [Test]
    public async Task UpdatePage_StaleRevision_IsRejectedAndTellsTheModelToReadAgain()
    {
        // Act
        var result = await PrepareAsync(Payload(new { action = "update_page", spaceKey = "HR", number = 2, body = "別的", expectedRevision = 2 }));

        // Assert
        var issue = result.Failure!.Details.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issue.Path, Is.EqualTo("changes[0].expectedRevision"));
            Assert.That(issue.Message, Does.Contain("第 3 版").And.Contain("get_page"));
        }
    }

    [Test]
    public async Task UpdatePage_SameAsNow_IsRejectedAsNothingToChange()
    {
        // Act
        var result = await PrepareAsync(Payload(new { action = "update_page", spaceKey = "HR", number = 2, body = "舊內文", expectedRevision = 3 }));

        // Assert
        Assert.That(result.Failure!.Details.Single().Message, Does.Contain("一樣"));
    }

    [Test]
    public async Task UpdatePage_BodyOfALongPage_CannotBeProposedButTheTitleCan()
    {
        // Arrange
        Pages.OnGet = number => KnowledgeData.Page(number, new string('字', AiRules.MaxEditableProjectDescriptionLength + 1));

        // Act
        var body = await PrepareAsync(Payload(new { action = "update_page", spaceKey = "HR", number = 2, body = "短", expectedRevision = 3 }));
        var title = await PrepareAsync(Payload(new { action = "update_page", spaceKey = "HR", number = 2, title = "新標題", expectedRevision = 3 }));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body.Failure!.Details.Single().Message, Does.Contain("讀不到全文"));
            Assert.That(title.Failure, Is.Null);
        }
    }

    [Test]
    public async Task MovePage_ToAnotherParent_ShowsFromAndToAndMoves()
    {
        // Arrange
        var payload = Payload(new { action = "move_page", spaceKey = "HR", number = 2, parentNumber = 4 });

        // Act
        var prepared = await PrepareOkAsync(payload);
        var result = await ApplyAsync(prepared, 0);

        // Assert
        var step = prepared.Steps.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That((step.Label, step.Before, step.After), Is.EqualTo(("搬移頁面", "「首頁」底下", "「FAQ」底下，排在最後面")));
            Assert.That(Pages.Moved.Single(), Is.EqualTo((2, new PageMove(4, null))));
            Assert.That(result.Link, Is.EqualTo("/knowledge/HR/2"));
        }
    }

    [TestCase(2, "自己")]
    [TestCase(3, "自己")]
    public async Task MovePage_UnderItselfOrItsOwnDescendant_IsRejected(int parentNumber, string expectedText)
    {
        // Act
        var result = await PrepareAsync(Payload(new { action = "move_page", spaceKey = "HR", number = 2, parentNumber }));

        // Assert
        Assert.That(result.Failure!.Details.Single().Message, Does.Contain(expectedText));
    }

    [Test]
    public async Task MovePage_ToWhereItAlreadyIs_IsRejected()
    {
        // Act：4 FAQ 已經是最上層的最後一頁。
        var result = await PrepareAsync(Payload(new { action = "move_page", spaceKey = "HR", number = 4 }));

        // Assert
        Assert.That(result.Failure!.Details.Single().Message, Does.Contain("已經在那個位置"));
    }

    [Test]
    public async Task DeletePage_ExplainsTheDescendantsGoToTheTrashAndDeletes()
    {
        // Act
        var prepared = await PrepareOkAsync(Payload(new { action = "delete_page", spaceKey = "HR", number = 2 }));
        var result = await ApplyAsync(prepared, 0);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prepared.Steps.Single().Note, Is.EqualTo("底下的 1 頁會一起進垃圾桶，可以復原"));
            Assert.That(result.Failure, Is.Null);
            Assert.That(Pages.Deleted, Is.EqualTo(new[] { 2 }));
        }
    }

    [Test]
    public async Task DeletePage_WithPagesTheUserCannotSee_IsRejectedBeforeAnyWrite()
    {
        // Arrange
        Pages.OnGet = number => KnowledgeData.Page(number, canDelete: false);

        // Act
        var result = await PrepareAsync(Payload(new { action = "delete_page", spaceKey = "HR", number = 2 }));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Details.Single().Message, Does.Contain("看不到的頁面"));
            Assert.That(Pages.Deleted, Is.Empty);
        }
    }

    [Test]
    public async Task RestorePage_FindsTheBatchInTheTrashAndRestores()
    {
        // Arrange
        var parent = new PageReference(KnowledgeData.Page1, 1, "首頁");
        Pages.OnTrash = () => [new TrashEntry(Guid.NewGuid(), new PageReference(Guid.NewGuid(), 7, "舊頁"), 3, parent, KnowledgeData.At, Guid.NewGuid())];

        // Act
        var prepared = await PrepareOkAsync(Payload(new { action = "restore_page", spaceKey = "HR", number = 7 }));
        var result = await ApplyAsync(prepared, 0);

        // Assert
        var step = prepared.Steps.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That((step.Label, step.Target, step.Note), Is.EqualTo(("復原頁面", "HR／舊頁", "底下的 2 頁會一起回來")));
            Assert.That(step.Before, Does.Contain("首頁"));
            Assert.That(Pages.Restored, Is.EqualTo(new[] { 7 }));
            Assert.That(result.Link, Is.EqualTo("/knowledge/HR/7"));
        }
    }

    [Test]
    public async Task RestorePage_NotInTheTrash_IsRejected()
    {
        // Act
        var result = await PrepareAsync(Payload(new { action = "restore_page", spaceKey = "HR", number = 7 }));

        // Assert
        Assert.That(result.Failure!.Details.Single().Message, Does.Contain("垃圾桶裡找不到"));
    }
}
