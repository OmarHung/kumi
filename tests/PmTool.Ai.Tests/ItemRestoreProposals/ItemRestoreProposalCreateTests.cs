using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Ai.Tests.ItemRestoreProposals;

/// <summary>建立復原建議：權限、已刪除的確認（含翻頁的上限）、上層也刪除的處理、數量與原因。</summary>
[TestFixture]
public sealed class ItemRestoreProposalCreateTests : RestoreTestBase
{
    [Test]
    public async Task Create_StoresTitlesAndDeletionTimes_AsPendingInTheGivenOrder()
    {
        // Arrange
        var first = AddDeleted("WEB-12", "第一個", daysAgo: 2);
        var second = AddDeleted("WEB-13", "第二個", daysAgo: 1);

        // Act
        var created = await CreateAsync("刪錯了", "web-13", "WEB-12");

        // Assert
        var read = await Proposals.GetAsync(Me, created.Value.Id, CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
            Assert.That(created.Value.Status, Is.EqualTo(ItemProposalStatus.Pending));
            Assert.That(created.Value.Reason, Is.EqualTo("刪錯了"));
            Assert.That(created.Value.Items, Is.EqualTo(new[]
            {
                new ItemRestoreTarget("WEB-13", "第二個", second.DeletedAt),
                new ItemRestoreTarget("WEB-12", "第一個", first.DeletedAt),
            }));
            Assert.That(created.Value.RestoredKeys, Is.Empty);
            Assert.That(read.Value.Items, Is.EqualTo(created.Value.Items));
        }
    }

    [Test]
    public async Task Create_WithoutItemDeletePermission_IsForbiddenAndReadsNothing()
    {
        // Arrange
        AddDeleted("WEB-12");
        Permissions(canDelete: false);

        // Act
        var created = await CreateAsync(null, "WEB-12");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Failure?.Code, Is.EqualTo(AiErrorCodes.Forbidden));
            Assert.That(created.Failure?.Message, Is.EqualTo("你沒有復原項目的權限。"));
            Assert.That(Deleted.ListCalls, Is.Zero);
        }
    }

    [Test]
    public async Task Create_ReportsEveryProblemAtOnce()
    {
        // Arrange
        AddDeleted("WEB-12");

        // Act
        var created = await CreateAsync(null, "WEB-12", "WEB-12", "WEB-99", "bad\u0001key");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Failure?.Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
            Assert.That(created.Failure!.Details.Any(d => d.Path == "itemKeys[0]"), Is.False);
            Assert.That(IssueAt(created.Failure, "itemKeys[1]"), Does.Contain("不只一次"));
            Assert.That(IssueAt(created.Failure, "itemKeys[2]"), Does.Contain("找不到已刪除的項目").And.Contain("已刪除頁"));
            Assert.That(IssueAt(created.Failure, "itemKeys[3]"), Does.Contain("不合規定"));
        }
    }

    [Test]
    public async Task Create_ANotDeletedItem_IsNotFoundAmongTheDeleted()
    {
        // Arrange
        AddDeleted("WEB-12");

        // Act
        var created = await CreateAsync(null, "WEB-50");

        // Assert
        Assert.That(IssueAt(created.Failure, "itemKeys[0]"), Does.Contain("WEB-50"));
    }

    [Test]
    public async Task Create_WithNoneOrTooManyKeys_IsRejected()
    {
        // Arrange
        var tooMany = Enumerable.Range(1, AiRules.MaxItemsPerDeletionProposal + 1).Select(n => $"WEB-{n}").ToArray();

        // Act
        var none = await CreateAsync(null);
        var many = await CreateAsync(null, tooMany);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(IssueAt(none.Failure, "itemKeys"), Does.Contain("1 到"));
            Assert.That(IssueAt(many.Failure, "itemKeys"), Does.Contain("1 到"));
        }
    }

    [Test]
    public async Task Create_WhenTheParentIsAlsoDeletedAndNotSelected_NamesTheParent()
    {
        // Arrange
        AddDeleted("WEB-10", "上層");
        AddDeleted("WEB-11", "下層", parentKey: "WEB-10");

        // Act
        var created = await CreateAsync(null, "WEB-11");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Failure?.Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
            Assert.That(IssueAt(created.Failure, "itemKeys[0]"), Does.Contain("WEB-10").And.Contain("一起復原"));
        }
    }

    [Test]
    public async Task Create_WhenTheParentIsSelectedToo_IsAccepted()
    {
        // Arrange
        AddDeleted("WEB-10", "上層");
        AddDeleted("WEB-11", "下層", parentKey: "WEB-10");

        // Act
        var created = await CreateAsync(null, "WEB-11", "WEB-10");

        // Assert
        Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
    }

    [Test]
    public async Task Create_WhenTheParentIsNotDeleted_IsAccepted()
    {
        // Arrange
        AddDeleted("WEB-11", "下層", parentKey: "WEB-10");

        // Act
        var created = await CreateAsync(null, "WEB-11");

        // Assert
        Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
    }

    [Test]
    public async Task Create_PagesBackThroughTheDeletedListUpToTheLimit()
    {
        // Arrange
        Deleted.PageSize = 1;
        for (var n = 1; n <= AiRules.MaxDeletedPagesScanned + 1; n++)
        {
            AddDeleted($"WEB-{n}");
        }

        // Act
        var onLastScannedPage = await CreateAsync(null, $"WEB-{AiRules.MaxDeletedPagesScanned}");
        var beyondTheLimit = await CreateAsync(null, $"WEB-{AiRules.MaxDeletedPagesScanned + 1}");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(onLastScannedPage.Succeeded, Is.True, () => onLastScannedPage.Failure?.Message ?? string.Empty);
            Assert.That(IssueAt(beyondTheLimit.Failure, "itemKeys[0]"), Does.Contain("找不到已刪除的項目"));
        }
    }

    [Test]
    public async Task Create_ChecksTheReason()
    {
        // Arrange
        AddDeleted("WEB-12");

        // Act
        var tooLong = await CreateAsync(new string('a', 201), "WEB-12");
        var control = await CreateAsync("兩行\n字", "WEB-12");
        var blank = await CreateAsync("   ", "WEB-12");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(IssueAt(tooLong.Failure, "reason"), Does.Contain("最長"));
            Assert.That(IssueAt(control.Failure, "reason"), Does.Contain("控制字元"));
            Assert.That(blank.Succeeded, Is.True);
            Assert.That(blank.Value.Reason, Is.Null);
        }
    }

    [Test]
    public async Task Create_WhenTheProjectIsNotVisible_ReturnsTheProjectFailureAsIs()
    {
        // Arrange
        H.Projects.OnGet = (_, _) => new Failure(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, "找不到專案。");

        // Act
        var created = await CreateAsync(null, "WEB-12");

        // Assert
        Assert.That(created.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectNotFound));
    }
}
