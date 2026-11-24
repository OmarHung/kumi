using static PmTool.Work.Tests.AttachmentSupport;

namespace PmTool.Work.Tests.Attachments;

/// <summary>權限跟著項目：看得到就能列與下載，新增與刪除要 item.update；看不到的一律當不存在。</summary>
[TestFixture]
public sealed class AttachmentAccessTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [TestCase("viewer")]
    [TestCase("commenter")]
    public async Task ReadOnlyRoles_CanListAndOpenButCannotAddOrDelete(string role)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var added = await _host.Attachments.AddAsync(scenario.Admin, item.Key, Upload("a.txt", "x"), CancellationToken.None);
        var reader = await scenario.MemberAsync("WEB", role);
        var filesBefore = FilesIn(_host).Length;

        // Act
        var listed = await _host.Attachments.ListAsync(reader, item.Key, CancellationToken.None);
        var opened = await _host.Attachments.OpenAsync(reader, added.Value.Id, CancellationToken.None);
        var blockedAdd = await _host.Attachments.AddAsync(reader, item.Key, Upload("b.txt", "y"), CancellationToken.None);
        var blockedDelete = await _host.Attachments.DeleteAsync(reader, added.Value.Id, CancellationToken.None);
        var stillThere = await _host.Attachments.ListAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(listed.Value.Select(a => a.Id), Is.EqualTo(new[] { added.Value.Id }));
            Assert.That(await ReadAllAsync(opened.Value), Is.EqualTo("x"u8.ToArray()));
            Assert.That(blockedAdd.Failure?.Kind, Is.EqualTo(PmTool.Abstractions.FailureKind.Forbidden));
            Assert.That(blockedDelete.Failure?.Kind, Is.EqualTo(PmTool.Abstractions.FailureKind.Forbidden));
            Assert.That(stillThere.Value, Has.Count.EqualTo(1));
            Assert.That(FilesIn(_host), Has.Length.EqualTo(filesBefore), "擋下的上傳沒有留下檔案");
        }
    }

    [Test]
    public async Task Editor_CanAddAndDelete()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var editor = await scenario.MemberAsync("WEB", "editor");

        // Act
        var added = await _host.Attachments.AddAsync(editor, item.Key, Upload("a.txt", "x"), CancellationToken.None);
        var deleted = await _host.Attachments.DeleteAsync(editor, added.Value.Id, CancellationToken.None);

        // Assert
        Assert.That((added.Value.UploadedBy, deleted.Succeeded), Is.EqualTo((editor.PersonId, true)));
    }

    [Test]
    public async Task ArchivedProject_StillListsAndOpensButRefusesAddAndDelete()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var added = await _host.Attachments.AddAsync(scenario.Admin, item.Key, Upload("a.txt", "x"), CancellationToken.None);
        await _host.Projects.ArchiveAsync(scenario.Admin, "WEB", CancellationToken.None);
        var filesBefore = FilesIn(_host).Length;

        // Act
        var listed = await _host.Attachments.ListAsync(scenario.Admin, item.Key, CancellationToken.None);
        var opened = await _host.Attachments.OpenAsync(scenario.Admin, added.Value.Id, CancellationToken.None);
        var blockedAdd = await _host.Attachments.AddAsync(scenario.Admin, item.Key, Upload("b.txt", "y"), CancellationToken.None);
        var blockedDelete = await _host.Attachments.DeleteAsync(scenario.Admin, added.Value.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(listed.Value, Has.Count.EqualTo(1));
            Assert.That(opened.Succeeded, Is.True);
            await opened.Value.DisposeAsync();
            Assert.That(blockedAdd.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectArchived));
            Assert.That(blockedDelete.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectArchived));
            Assert.That(FilesIn(_host), Has.Length.EqualTo(filesBefore));
        }
    }

    [Test]
    public async Task DeletedItem_HidesItsAttachmentsUntilItIsRestored()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var added = await _host.Attachments.AddAsync(scenario.Admin, item.Key, Upload("a.txt", "x"), CancellationToken.None);
        await _host.Items.DeleteAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Act
        var listed = await _host.Attachments.ListAsync(scenario.Admin, item.Key, CancellationToken.None);
        var opened = await _host.Attachments.OpenAsync(scenario.Admin, added.Value.Id, CancellationToken.None);
        var blockedAdd = await _host.Attachments.AddAsync(scenario.Admin, item.Key, Upload("b.txt", "y"), CancellationToken.None);
        var blockedDelete = await _host.Attachments.DeleteAsync(scenario.Admin, added.Value.Id, CancellationToken.None);
        await _host.Items.RestoreAsync(scenario.Admin, item.Key, CancellationToken.None);
        var listedAfterRestore = await _host.Attachments.ListAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(listed.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemNotFound));
            Assert.That(opened.Failure?.Code, Is.EqualTo(WorkErrorCodes.AttachmentNotFound));
            Assert.That(blockedAdd.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemNotFound));
            Assert.That(blockedDelete.Failure?.Code, Is.EqualTo(WorkErrorCodes.AttachmentNotFound));
            Assert.That(listedAfterRestore.Value.Select(a => a.Id), Is.EqualTo(new[] { added.Value.Id }));
        }
    }

    [Test]
    public async Task PersonWhoCannotSeeTheProject_GetsNotFoundEverywhere()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var added = await _host.Attachments.AddAsync(scenario.Admin, item.Key, Upload("a.txt", "x"), CancellationToken.None);
        var outsider = scenario.Person();

        // Act
        var listed = await _host.Attachments.ListAsync(outsider, item.Key, CancellationToken.None);
        var addedByOutsider = await _host.Attachments.AddAsync(outsider, item.Key, Upload("b.txt", "y"), CancellationToken.None);
        var opened = await _host.Attachments.OpenAsync(outsider, added.Value.Id, CancellationToken.None);
        var deleted = await _host.Attachments.DeleteAsync(outsider, added.Value.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(listed.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemNotFound));
            Assert.That(addedByOutsider.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemNotFound));
            Assert.That(opened.Failure?.Code, Is.EqualTo(WorkErrorCodes.AttachmentNotFound));
            Assert.That(deleted.Failure?.Code, Is.EqualTo(WorkErrorCodes.AttachmentNotFound));
        }
    }

    [Test]
    public async Task AnotherOrganization_CannotSeeTheAttachmentsEvenWithTheSameProjectKey()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var added = await _host.Attachments.AddAsync(scenario.Admin, item.Key, Upload("a.txt", "x"), CancellationToken.None);
        var stranger = new Scenario(_host);
        await stranger.CreateProjectAsync("WEB");
        await stranger.CreateTaskAsync("WEB");

        // Act
        var opened = await _host.Attachments.OpenAsync(stranger.Admin, added.Value.Id, CancellationToken.None);
        var deleted = await _host.Attachments.DeleteAsync(stranger.Admin, added.Value.Id, CancellationToken.None);
        var listed = await _host.Attachments.ListAsync(stranger.Admin, item.Key, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(opened.Failure?.Code, Is.EqualTo(WorkErrorCodes.AttachmentNotFound));
            Assert.That(deleted.Failure?.Code, Is.EqualTo(WorkErrorCodes.AttachmentNotFound));
            Assert.That(listed.Value, Is.Empty, "同樣的編號在他的組織裡是另一個項目");
        }
    }

    [Test]
    public async Task UnknownAttachmentAndUnknownItem_AreNotFound()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var opened = await _host.Attachments.OpenAsync(scenario.Admin, Guid.NewGuid(), CancellationToken.None);
        var addedToNothing = await _host.Attachments.AddAsync(scenario.Admin, "WEB-999", Upload("a.txt", "x"), CancellationToken.None);
        var addedToGarbage = await _host.Attachments.AddAsync(scenario.Admin, "not a key", Upload("a.txt", "x"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(opened.Failure?.Code, Is.EqualTo(WorkErrorCodes.AttachmentNotFound));
            Assert.That(addedToNothing.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemNotFound));
            Assert.That(addedToGarbage.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemNotFound));
        }
    }
}
