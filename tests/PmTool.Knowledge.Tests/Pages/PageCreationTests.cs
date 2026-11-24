using PmTool.Abstractions;
using Microsoft.EntityFrameworkCore;
using PmTool.Knowledge.Persistence;

namespace PmTool.Knowledge.Tests.Pages;

[TestFixture]
public sealed class PageCreationTests
{
    private KnowledgeTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await KnowledgeTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task CreateAsync_FirstPage_GetsNumberOneRevisionOneAndOneVersion()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");

        // Act
        var page = await org.PageAsync("HR", "  新人手冊  ", body: "# 歡迎");

        // Assert
        Assert.That((page.Number, page.Title, page.Body, page.Revision, page.VersionCount), Is.EqualTo((1, "新人手冊", "# 歡迎", 1L, 1)));
        Assert.That((page.Ancestors, page.IsRestricted, page.RestrictedAncestors), Is.EqualTo((Array.Empty<PageReference>(), false, Array.Empty<PageReference>())));
        Assert.That(page.CreatedBy, Is.EqualTo(org.Admin.PersonId));
        Assert.That(page.MyPermissions, Is.EqualTo(new PagePermissions(CanEdit: true, CanComment: true, CanDelete: true)));
    }

    [Test]
    public async Task CreateAsync_NullBody_IsAnEmptyPage()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");

        // Act
        var page = await org.PageAsync("HR", "空白頁");

        // Assert
        Assert.That(page.Body, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_NumbersGoUpAndAreNeverReusedAfterADelete()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var first = await org.PageAsync("HR", "一");
        await _host.Pages.DeleteAsync(org.Admin, "HR", first.Number, CancellationToken.None);

        // Act
        var second = await org.PageAsync("HR", "二");
        var third = await org.PageAsync("HR", "三");

        // Assert
        Assert.That((second.Number, third.Number), Is.EqualTo((2, 3)));
    }

    [Test]
    public async Task CreateAsync_ChildrenAndBeforeNumber_AreOrderedInThePreorderTree()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var a = await org.PageAsync("HR", "A");
        var b = await org.PageAsync("HR", "B");
        await org.PageAsync("HR", "A1", parent: a.Number);
        await org.PageAsync("HR", "A0", parent: a.Number, before: 3);

        // Act：C 放在 B 的前面（最上層）。
        await org.PageAsync("HR", "C", before: b.Number);
        var outline = await org.OutlineAsync("HR");

        // Assert
        Assert.That(outline, Is.EqualTo(new[] { "A", "A0<1", "A1<1", "C", "B" }));
    }

    [Test]
    public async Task CreateAsync_DetailOfAChild_ListsAncestorsFromTheTop()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var top = await org.PageAsync("HR", "上");
        var middle = await org.PageAsync("HR", "中", parent: top.Number);

        // Act
        var leaf = await org.PageAsync("HR", "下", parent: middle.Number);

        // Assert
        Assert.That(leaf.Ancestors.Select(a => a.Title), Is.EqualTo(new[] { "上", "中" }));
    }

    [Test]
    public async Task CreateAsync_UnknownOrDeletedParent_IsPositionInvalid()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var gone = await org.PageAsync("HR", "刪掉的");
        await _host.Pages.DeleteAsync(org.Admin, "HR", gone.Number, CancellationToken.None);

        // Act
        var unknown = await _host.Pages.CreateAsync(org.Admin, "HR", new NewPage(99, "子", null), CancellationToken.None);
        var deleted = await _host.Pages.CreateAsync(org.Admin, "HR", new NewPage(gone.Number, "子", null), CancellationToken.None);

        // Assert
        Assert.That(unknown.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PagePositionInvalid));
        Assert.That(unknown.Failure?.Details.Single().Path, Is.EqualTo("parentNumber"));
        Assert.That(deleted.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PagePositionInvalid));
    }

    [Test]
    public async Task CreateAsync_BeforeNumberThatIsNotASiblingOrDoesNotExist_IsPositionInvalid()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var a = await org.PageAsync("HR", "A");
        var child = await org.PageAsync("HR", "A1", parent: a.Number);

        // Act：A1 不在最上層；99 不存在。
        var wrongLevel = await _host.Pages.CreateAsync(org.Admin, "HR", new NewPage(null, "X", null) { BeforeNumber = child.Number }, CancellationToken.None);
        var unknown = await _host.Pages.CreateAsync(org.Admin, "HR", new NewPage(null, "X", null) { BeforeNumber = 99 }, CancellationToken.None);

        // Assert
        Assert.That(wrongLevel.Failure?.Details.Single().Path, Is.EqualTo("beforeNumber"));
        Assert.That(unknown.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PagePositionInvalid));
    }

    [Test]
    public async Task CreateAsync_TenLevelsAreAllowedAndTheEleventhIsTooDeep()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var current = await org.PageAsync("HR", "第 1 層");
        for (var level = 2; level <= KnowledgeRules.MaxDepth; level++)
        {
            current = await org.PageAsync("HR", $"第 {level} 層", parent: current.Number);
        }

        // Act
        var tooDeep = await _host.Pages.CreateAsync(org.Admin, "HR", new NewPage(current.Number, "第 11 層", null), CancellationToken.None);

        // Assert
        Assert.That(tooDeep.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageTooDeep));
        Assert.That(tooDeep.Failure?.Details.Single().Path, Is.EqualTo("parentNumber"));
    }

    [Test]
    public async Task CreateAsync_BadTitleAndBody_ReportsBothProblemsAtOnce()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");

        // Act
        var created = await _host.Pages.CreateAsync(org.Admin, "HR", new NewPage(null, " \n ", "有 NUL\0"), CancellationToken.None);

        // Assert
        Assert.That(
            created.Failure!.Details.Select(d => (d.Code, d.Path)),
            Is.EqualTo(new[] { (KnowledgeErrorCodes.PageTitleRequired, "title"), (KnowledgeErrorCodes.PageBodyInvalid, "body") }));
    }

    [Test]
    public async Task CreateAsync_TitleTooLongOrWithInvisibleFormatCharacters_IsRejected()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");

        // Act
        var tooLong = await _host.Pages.CreateAsync(org.Admin, "HR", new NewPage(null, new string('字', 201), null), CancellationToken.None);
        var invisible = await _host.Pages.CreateAsync(org.Admin, "HR", new NewPage(null, "標‮題", null), CancellationToken.None);

        // Assert
        Assert.That(tooLong.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageTitleTooLong));
        Assert.That(invisible.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageTitleRequired));
    }

    [Test]
    public async Task CreateAsync_BodyWithPrivateKeyOrTooLong_IsRejected()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");

        // Act
        var secret = await _host.Pages.CreateAsync(
            org.Admin, "HR", new NewPage(null, "金鑰", "-----BEGIN RSA PRIVATE KEY-----\nabc"), CancellationToken.None);
        var putty = await _host.Pages.CreateAsync(org.Admin, "HR", new NewPage(null, "金鑰", "PuTTY-User-Key-File-3: ssh-rsa"), CancellationToken.None);
        var tooLong = await _host.Pages.CreateAsync(
            org.Admin, "HR", new NewPage(null, "長", new string('a', KnowledgeRules.MaxPageBodyLength + 1)), CancellationToken.None);

        // Assert
        Assert.That(secret.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageBodySecret));
        Assert.That(putty.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageBodySecret));
        Assert.That(tooLong.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageBodyTooLong));
    }

    [Test]
    public async Task CreateAsync_BodyWithWindowsLineBreaks_IsStoredWithUnixLineBreaksAndKeepsIndentation()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");

        // Act
        var page = await org.PageAsync("HR", "換行", body: "  第一行\r\n    第二行\r\t第三行\n");
        var reread = await _host.Pages.GetAsync(org.Admin, "HR", page.Number, CancellationToken.None);

        // Assert
        Assert.That(reread.Value.Body, Is.EqualTo("  第一行\n    第二行\n\t第三行\n"));
    }

    [Test]
    public async Task CreateAsync_ProjectViewer_IsForbiddenButProjectEditorCanCreate()
    {
        // Arrange
        var org = new Org(_host);
        var (space, projectId) = await org.CreateProjectSpaceAsync(org.Admin, "WEB");
        var viewer = org.Member();
        var editor = org.Member();
        _host.Work.Grant(org.Id, viewer.PersonId, projectId, "WEB", FakeWorkAccessQueries.ViewerPermissions);
        _host.Work.Grant(org.Id, editor.PersonId, projectId, "WEB", FakeWorkAccessQueries.EditorPermissions);

        // Act
        var byViewer = await _host.Pages.CreateAsync(viewer, space.Key, new NewPage(null, "想寫", null), CancellationToken.None);
        var byEditor = await _host.Pages.CreateAsync(editor, space.Key, new NewPage(null, "我來寫", null), CancellationToken.None);

        // Assert
        Assert.That(byViewer.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.Forbidden));
        Assert.That(byEditor.Succeeded, Is.True);
    }

    [Test]
    public async Task CreateAsync_ArchivedSpace_IsSpaceArchivedBeforeAnyPermissionCheck()
    {
        // Arrange
        var org = new Org(_host);
        var (space, projectId) = await org.CreateProjectSpaceAsync(org.Admin, "WEB");
        var viewer = org.Member();
        _host.Work.Grant(org.Id, viewer.PersonId, projectId, "WEB", FakeWorkAccessQueries.ViewerPermissions);
        await _host.Spaces.SetArchivedAsync(org.Admin, space.Key, archived: true, CancellationToken.None);

        // Act
        var byViewer = await _host.Pages.CreateAsync(viewer, space.Key, new NewPage(null, "想寫", null), CancellationToken.None);
        var byAdmin = await _host.Pages.CreateAsync(org.Admin, space.Key, new NewPage(null, "想寫", null), CancellationToken.None);

        // Assert
        Assert.That(byViewer.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.SpaceArchived));
        Assert.That(byAdmin.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.SpaceArchived));
        Assert.That(byAdmin.Failure?.Kind, Is.EqualTo(FailureKind.Conflict));
    }

    [Test]
    public async Task CreateAsync_MemberOfAnOrganizationSpace_CanWriteButGuestSeesNothing()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");

        // Act
        var byMember = await _host.Pages.CreateAsync(org.Member(), "HR", new NewPage(null, "成員寫的", null), CancellationToken.None);
        var byGuest = await _host.Pages.CreateAsync(org.Guest(), "HR", new NewPage(null, "訪客寫的", null), CancellationToken.None);

        // Assert
        Assert.That(byMember.Succeeded, Is.True);
        Assert.That(byGuest.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.SpaceNotFound));
    }

    [Test]
    public async Task CreateAsync_SpaceAlreadyAtThePageLimit_IsPageLimitReachedUntilAPageIsDeleted()
    {
        // Arrange
        var org = new Org(_host);
        var space = await org.CreateSpaceAsync("HR");
        await using (var db = await _host.CreateDbContextAsync())
        {
            for (var number = 1; number <= KnowledgeRules.MaxPagesPerSpace; number++)
            {
                db.Pages.Add(new PageRow
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = org.Id,
                    SpaceId = space.Id,
                    Number = number,
                    Position = number - 1,
                    Title = $"頁 {number}",
                    Body = string.Empty,
                    Revision = 1,
                    IsRestricted = false,
                    CreatedBy = org.Admin.PersonId,
                    CreatedAt = KnowledgeTestHost.StartTime,
                    UpdatedBy = org.Admin.PersonId,
                    UpdatedAt = KnowledgeTestHost.StartTime,
                });
            }

            await db.SaveChangesAsync();
            await db.Spaces.Where(s => s.Id == space.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.NextPageNumber, KnowledgeRules.MaxPagesPerSpace + 1));
        }

        // Act
        var overLimit = await _host.Pages.CreateAsync(org.Admin, "HR", new NewPage(null, "多一頁", null), CancellationToken.None);
        await _host.Pages.DeleteAsync(org.Admin, "HR", 1, CancellationToken.None);
        var afterDelete = await _host.Pages.CreateAsync(org.Admin, "HR", new NewPage(null, "多一頁", null), CancellationToken.None);

        // Assert
        Assert.That(overLimit.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageLimitReached));
        Assert.That(afterDelete.Succeeded, Is.True);
    }
}
