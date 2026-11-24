using PmTool.Abstractions;
using static PmTool.Work.Tests.History.HistorySupport;

namespace PmTool.Work.Tests.History;

/// <summary>
/// 外部編號已經匯入到呼叫者看不到的專案：不能透露那邊的專案代號或項目編號，當成這一列（則、個）的問題；
/// 看得到的專案照舊（項目略過並帶編號、附件的訊息帶專案代號）。
/// </summary>
[TestFixture]
public sealed class HistoryVisibilityTests
{
    private const string Hidden = "OPS";

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ImportItemsAsync_ExternalIdImportedIntoAProjectTheCallerCannotSee_IsAProblemWithoutAnyKey()
    {
        // Arrange
        var setup = await TwoProjectsAsync();

        // Act
        var result = await _host.History.ImportItemsAsync(setup.Manager, "WEB", Batch(Row("m-1", setup.Member.PersonId)), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((result.Failure?.Code, Paths(result.Failure)), Is.EqualTo((WorkErrorCodes.ImportInvalid, new[] { "rows/0/external_id" })));
            Assert.That(Texts(result.Failure), Does.Not.Contain(Hidden));
            Assert.That(Texts(result.Failure), Does.Contain("已經匯入到別的專案"));
        }
    }

    [Test]
    public async Task ImportItemsAsync_ExternalIdImportedIntoAProjectTheCallerCanSee_IsSkippedWithThatKey()
    {
        // Arrange
        var setup = await TwoProjectsAsync();
        await _host.Projects.SetMemberAsync(setup.Scenario.Admin, Hidden, setup.Manager.PersonId, "viewer", CancellationToken.None);

        // Act
        var result = await _host.History.ImportItemsAsync(setup.Manager, "WEB", Batch(Row("m-1", setup.Member.PersonId)), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Skipped, Is.EqualTo(new[] { new HistoryItemRef("m-1", "OPS-1") }));
            Assert.That(result.Value.Created, Is.Empty);
        }
    }

    [Test]
    public async Task ImportItemsAsync_ParentInAProjectTheCallerCannotSee_IsAProblemWithoutItsKey()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        await scenario.CreateProjectAsync(Hidden, Scenario.AgencyProject);
        var manager = await scenario.MemberAsync("WEB", "admin");
        var opsPerson = await scenario.MemberAsync(Hidden, "editor");
        await _host.Projects.SetMemberAsync(scenario.Admin, "WEB", opsPerson.PersonId, "editor", CancellationToken.None);
        await _host.History.ImportItemsAsync(scenario.Admin, Hidden, Batch(Row("m-1", opsPerson.PersonId).OfType("requirement")), CancellationToken.None);

        // Act
        var result = await _host.History.ImportItemsAsync(manager, "WEB", Batch(Row("m-2", opsPerson.PersonId, parent: "m-1")), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Paths(result.Failure), Is.EqualTo(new[] { "rows/0/parent_external_id" }));
            Assert.That(Texts(result.Failure), Does.Not.Contain(Hidden));
        }
    }

    [Test]
    public async Task ImportCommentsAsync_ItemOrCommentInAProjectTheCallerCannotSee_IsAProblemWithoutTheProjectKey()
    {
        // Arrange
        var setup = await TwoProjectsAsync();
        await _host.History.ImportCommentsAsync(setup.Scenario.Admin, Hidden, Comments(Comment("u-1", "m-1", setup.OpsPerson.PersonId)), CancellationToken.None);
        await _host.History.ImportItemsAsync(setup.Manager, "WEB", Batch(Row("w-1", setup.Member.PersonId)), CancellationToken.None);
        var batch = Comments(Comment("u-1", "w-1", setup.Member.PersonId), Comment("u-2", "m-1", setup.Member.PersonId));

        // Act
        var result = await _host.History.ImportCommentsAsync(setup.Manager, "WEB", batch, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Paths(result.Failure), Is.EqualTo(new[] { "comments/0/external_id", "comments/1/item_external_id" }));
            Assert.That(Texts(result.Failure), Does.Not.Contain(Hidden));
        }
    }

    [Test]
    public async Task ImportCommentsAsync_ItemInAnotherProjectTheCallerCanSee_NamesThatProject()
    {
        // Arrange
        var setup = await TwoProjectsAsync();
        await _host.Projects.SetMemberAsync(setup.Scenario.Admin, Hidden, setup.Manager.PersonId, "viewer", CancellationToken.None);

        // Act
        var result = await _host.History.ImportCommentsAsync(setup.Manager, "WEB", Comments(Comment("u-1", "m-1", setup.Member.PersonId)), CancellationToken.None);

        // Assert
        Assert.That(Texts(result.Failure), Does.Contain(Hidden));
    }

    [Test]
    public async Task ImportAttachmentAsync_ExternalIdInAProjectTheCallerCannotSee_IsHistoryInvalidWithoutTheProjectKey()
    {
        // Arrange
        var setup = await TwoProjectsAsync();
        await _host.History.ImportAttachmentAsync(setup.Scenario.Admin, "OPS-1", Attachment("a-1", setup.OpsPerson.PersonId), CancellationToken.None);
        await _host.History.ImportItemsAsync(setup.Manager, "WEB", Batch(Row("w-1", setup.Member.PersonId)), CancellationToken.None);

        // Act
        var result = await _host.History.ImportAttachmentAsync(setup.Manager, "WEB-1", Attachment("a-1", setup.Member.PersonId), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((result.Failure?.Code, Paths(result.Failure)), Is.EqualTo((WorkErrorCodes.HistoryInvalid, new[] { "externalId" })));
            Assert.That(Texts(result.Failure), Does.Not.Contain(Hidden));
        }
    }

    [Test]
    public async Task ImportAttachmentAsync_ExternalIdInAnotherProjectTheCallerCanSee_NamesThatProject()
    {
        // Arrange
        var setup = await TwoProjectsAsync();
        await _host.Projects.SetMemberAsync(setup.Scenario.Admin, Hidden, setup.Manager.PersonId, "viewer", CancellationToken.None);
        await _host.History.ImportAttachmentAsync(setup.Scenario.Admin, "OPS-1", Attachment("a-1", setup.OpsPerson.PersonId), CancellationToken.None);
        await _host.History.ImportItemsAsync(setup.Manager, "WEB", Batch(Row("w-1", setup.Member.PersonId)), CancellationToken.None);

        // Act
        var result = await _host.History.ImportAttachmentAsync(setup.Manager, "WEB-1", Attachment("a-1", setup.Member.PersonId), CancellationToken.None);

        // Assert
        Assert.That(Texts(result.Failure), Does.Contain(Hidden));
    }

    private static string Texts(Failure? failure) =>
        string.Join("\n", [failure?.Message, .. failure?.Details.Select(d => d.Message) ?? []]);

    /// <summary>WEB 有管理員（呼叫者）與成員；OPS 裡匯入過 m-1，呼叫者不是它的成員。</summary>
    private async Task<(Scenario Scenario, Actor Manager, Actor Member, Actor OpsPerson)> TwoProjectsAsync()
    {
        var (scenario, manager, member) = await ProjectWithMembersAsync(_host);
        await scenario.CreateProjectAsync(Hidden);
        var opsPerson = await scenario.MemberAsync(Hidden, "editor");
        var imported = await _host.History.ImportItemsAsync(scenario.Admin, Hidden, Batch(Row("m-1", opsPerson.PersonId)), CancellationToken.None);
        Assert.That(imported.Succeeded, Is.True, () => imported.Failure!.Message);
        return (scenario, manager, member, opsPerson);
    }
}
