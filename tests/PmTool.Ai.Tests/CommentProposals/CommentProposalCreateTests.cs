using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Ai.Tests.CommentProposals;

/// <summary>建立留言建議：每一種檢查、權限、封存的專案、一次回全部的問題，以及存下來的摘錄。</summary>
[TestFixture]
public sealed class CommentProposalCreateTests : CommentTestBase
{
    [Test]
    public async Task Create_StoresEveryChangeWithAuthorExcerptAndTime_AsPendingForTheCreator()
    {
        // Arrange
        Permissions(manage: true);
        var own = AddComment(text: "我寫的留言");
        var others = AddComment("WEB-13", author: Other, text: "別人寫的");
        var deleted = AddComment(author: Other, deletedAt: DateTimeOffset.UnixEpoch, deletedBy: Other);

        // Act
        var created = await CreatedAsync(Edit("WEB-12", own.Id, "改過的內容"), Delete("WEB-13", others.Id), Restore("WEB-12", deleted.Id));

        // Assert
        var read = await Proposals.GetAsync(Me, created.Id, CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Status, Is.EqualTo(ItemProposalStatus.Pending));
            Assert.That(created.ProjectKey, Is.EqualTo("WEB"));
            Assert.That(created.Changes.Select(c => c.Action), Is.EqualTo(new[] { CommentAction.Edit, CommentAction.Delete, CommentAction.Restore }));
            Assert.That(created.Changes[0].Excerpt, Is.EqualTo("我寫的留言"));
            Assert.That(created.Changes[0].Body, Is.EqualTo("改過的內容"));
            Assert.That(created.Changes[0].AuthorName, Is.Not.Empty);
            Assert.That(created.Changes[0].CommentedAt, Is.EqualTo(own.At));
            Assert.That(created.Changes[1].Body, Is.Null);
            Assert.That(created.Changes[2].Excerpt, Is.Null, "刪除的留言讀不到內容");
            Assert.That(read.Value.Changes, Is.EqualTo(created.Changes));
        }
    }

    [Test]
    public async Task Create_CutsTheExcerptToTheLimit()
    {
        // Arrange
        var long_ = AddComment(text: new string('字', AiRules.MaxCommentExcerptLength + 100));

        // Act
        var created = await CreatedAsync(Delete("WEB-12", long_.Id));

        // Assert
        Assert.That(created.Changes.Single().Excerpt, Has.Length.EqualTo(AiRules.MaxCommentExcerptLength));
    }

    [Test]
    public async Task Create_WithNoChangesOrTooMany_IsRejected()
    {
        // Arrange
        var tooMany = Enumerable.Range(0, AiRules.MaxCommentChangesPerProposal + 1).Select(_ => Delete("WEB-12", Guid.NewGuid())).ToArray();

        // Act
        var none = await CreateAsync();
        var many = await CreateAsync(tooMany);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(none.Failure?.Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
            Assert.That(IssueAt(none.Failure, "changes"), Does.Contain("1 到"));
            Assert.That(IssueAt(many.Failure, "changes"), Does.Contain("1 到"));
        }
    }

    [Test]
    public async Task Create_ReportsEveryProblemAtOnce_WithPaths()
    {
        // Arrange
        var duplicate = AddComment();
        var edited = AddComment(text: "一樣");

        // Act
        var created = await CreateAsync(
            Delete("WEB-99", Guid.NewGuid()),
            Delete("WEB-12", Guid.NewGuid()),
            Delete("WEB-12", duplicate.Id),
            Delete("WEB-12", duplicate.Id),
            Edit("WEB-12", edited.Id, "一樣"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Failure?.Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
            Assert.That(IssueAt(created.Failure, "changes[0].itemKey"), Does.Contain("WEB-99"));
            Assert.That(IssueAt(created.Failure, "changes[1].commentId"), Does.Contain("找不到這則留言"));
            Assert.That(created.Failure!.Details.Any(d => d.Path == "changes[2].commentId"), Is.False);
            Assert.That(IssueAt(created.Failure, "changes[3].commentId"), Does.Contain("不只一次"));
            Assert.That(IssueAt(created.Failure, "changes[4].commentId"), Does.Contain("一樣"));
        }
    }

    [Test]
    public async Task Create_ACommentOfAnotherItem_IsNotFound()
    {
        // Arrange
        var comment = AddComment("WEB-13");

        // Act
        var created = await CreateAsync(Delete("WEB-12", comment.Id));

        // Assert
        Assert.That(IssueAt(created.Failure, "changes[0].commentId"), Does.Contain("找不到這則留言"));
    }

    [Test]
    public async Task Create_EditRules_RefuseOthersImportedDeletedAndBadBodies()
    {
        // Arrange
        var others = AddComment(author: Other);
        var imported = AddComment(importedFrom: "monday");
        var deleted = AddComment(deletedAt: DateTimeOffset.UnixEpoch, deletedBy: Me.PersonId);
        var empty = AddComment();
        var tooLong = AddComment();
        var control = AddComment();

        // Act
        var created = await CreateAsync(
            Edit("WEB-12", others.Id),
            Edit("WEB-12", imported.Id),
            Edit("WEB-12", deleted.Id),
            Edit("WEB-12", empty.Id, "  "),
            Edit("WEB-12", tooLong.Id, new string('a', WorkRules.MaxCommentLength + 1)),
            Edit("WEB-12", control.Id, "含有\0控制字元"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(IssueAt(created.Failure, "changes[0].commentId"), Does.Contain("別人的留言").And.Contain("只有作者"));
            Assert.That(IssueAt(created.Failure, "changes[1].commentId"), Does.Contain("匯入"));
            Assert.That(IssueAt(created.Failure, "changes[2].commentId"), Does.Contain("已經刪除"));
            Assert.That(IssueAt(created.Failure, "changes[3].commentId"), Does.Contain("body"));
            Assert.That(IssueAt(created.Failure, "changes[4].commentId"), Does.Contain("最長"));
            Assert.That(IssueAt(created.Failure, "changes[5].commentId"), Does.Contain("控制字元"));
        }
    }

    [Test]
    public async Task Create_EditingTheSameContentIgnoringWhitespaceAndLineEndings_IsRefused()
    {
        // Arrange
        var own = AddComment(text: "第一行\n第二行");

        // Act
        var created = await CreateAsync(Edit("WEB-12", own.Id, "  第一行\r\n第二行 "));

        // Assert
        Assert.That(IssueAt(created.Failure, "changes[0].commentId"), Does.Contain("一樣"));
    }

    [Test]
    public async Task Create_EditWithoutCommentPermission_IsRefused()
    {
        // Arrange
        Permissions(manage: true, comment: false);
        var own = AddComment();

        // Act
        var created = await CreateAsync(Edit("WEB-12", own.Id));

        // Assert
        Assert.That(IssueAt(created.Failure, "changes[0].commentId"), Does.Contain("沒有留言的權限"));
    }

    [Test]
    public async Task Create_DeleteRules_OthersNeedProjectManage_OwnNeedNothingMore()
    {
        // Arrange
        var others = AddComment(author: Other);
        var own = AddComment();
        var imported = AddComment(importedFrom: "monday");
        var already = AddComment(deletedAt: DateTimeOffset.UnixEpoch, deletedBy: Me.PersonId);

        // Act
        var refused = await CreateAsync(Delete("WEB-12", others.Id), Delete("WEB-12", imported.Id), Delete("WEB-12", already.Id));
        var withBody = await CreateAsync(new CommentChangeDraft("WEB-12", own.Id, CommentAction.Delete, "不該有"));
        var ownOnly = await CreateAsync(Delete("WEB-12", own.Id));
        Permissions(manage: true);
        var manager = await CreateAsync(Delete("WEB-12", others.Id), Delete("WEB-12", imported.Id));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(IssueAt(refused.Failure, "changes[0].commentId"), Does.Contain("專案管理者"));
            Assert.That(IssueAt(refused.Failure, "changes[1].commentId"), Does.Contain("專案管理者"));
            Assert.That(IssueAt(refused.Failure, "changes[2].commentId"), Does.Contain("已經刪除"));
            Assert.That(IssueAt(withBody.Failure, "changes[0].commentId"), Does.Contain("不能給 body"));
            Assert.That(ownOnly.Succeeded, Is.True);
            Assert.That(manager.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task Create_RestoreRules_AuthorCanRestoreOwnDeletion_ButNotOneDoneByAManager()
    {
        // Arrange
        var byAuthor = AddComment(deletedAt: DateTimeOffset.UnixEpoch, deletedBy: Me.PersonId);
        var byManager = AddComment(deletedAt: DateTimeOffset.UnixEpoch, deletedBy: Other);
        var notDeleted = AddComment();

        // Act
        var refused = await CreateAsync(Restore("WEB-12", byManager.Id), Restore("WEB-12", notDeleted.Id));
        var allowed = await CreateAsync(Restore("WEB-12", byAuthor.Id));
        Permissions(manage: true);
        var manager = await CreateAsync(Restore("WEB-12", byManager.Id));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(IssueAt(refused.Failure, "changes[0].commentId"), Does.Contain("只有專案管理者能復原"));
            Assert.That(IssueAt(refused.Failure, "changes[1].commentId"), Does.Contain("沒有刪除"));
            Assert.That(allowed.Succeeded, Is.True);
            Assert.That(manager.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task Create_InAnArchivedProject_IsRefused()
    {
        // Arrange
        Permissions(manage: true, archived: true);
        var own = AddComment();

        // Act
        var created = await CreateAsync(Delete("WEB-12", own.Id));

        // Assert
        Assert.That(created.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectArchived));
    }

    [Test]
    public async Task Create_WhenTheProjectIsNotVisible_ReturnsTheProjectFailureAsIs()
    {
        // Arrange
        H.Projects.OnGet = (_, _) => new Failure(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, "找不到專案。");

        // Act
        var created = await CreateAsync(Delete("WEB-12", Guid.NewGuid()));

        // Assert
        Assert.That(created.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectNotFound));
    }
}
