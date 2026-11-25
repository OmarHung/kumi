using PmTool.Abstractions;
using static PmTool.Work.Tests.History.HistorySupport;

namespace PmTool.Work.Tests.History;

/// <summary>查哪些外部編號已經匯入過：三種都查得到、只回呼叫者看得到的專案、請求的規則。</summary>
[TestFixture]
public sealed class HistoryLookupTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task LookupAsync_ItemsCommentsAndAttachments_ReturnTheirKeysAndTargetIds()
    {
        // Arrange
        var (_, manager, member) = await ProjectWithMembersAsync(_host);
        var item = await _host.History.ImportItemsAsync(manager, "WEB", Batch(Row("m-1", member.PersonId)), CancellationToken.None);
        var comment = await _host.History.ImportCommentsAsync(manager, "WEB", Comments(Comment("u-1", "m-1", member.PersonId)), CancellationToken.None);
        var attachment = await _host.History.ImportAttachmentAsync(manager, "WEB-1", Attachment("a-1", member.PersonId), CancellationToken.None);
        var itemId = (await _host.History.LookupAsync(manager, Lookup(HistoryRefKind.Item, "m-1"), CancellationToken.None)).Value[0].TargetId;

        // Act
        var items = await _host.History.LookupAsync(manager, Lookup(HistoryRefKind.Item, "m-1", "nope"), CancellationToken.None);
        var comments = await _host.History.LookupAsync(manager, Lookup(HistoryRefKind.Comment, "u-1"), CancellationToken.None);
        var attachments = await _host.History.LookupAsync(manager, Lookup(HistoryRefKind.Attachment, "a-1"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(item.Succeeded, Is.True);
            Assert.That(items.Value, Is.EqualTo(new[] { new HistoryRef("m-1", HistoryRefKind.Item, "WEB", "WEB-1", itemId) }));
            Assert.That(comments.Value, Is.EqualTo(new[] { new HistoryRef("u-1", HistoryRefKind.Comment, "WEB", "WEB-1", comment.Value.Created[0].ActivityId) }));
            Assert.That(attachments.Value, Is.EqualTo(new[] { new HistoryRef("a-1", HistoryRefKind.Attachment, "WEB", "WEB-1", attachment.Value.Attachment.Id) }));
        }
    }

    [Test]
    public async Task LookupAsync_SameExternalIdOfDifferentKinds_AreIndependent()
    {
        // Arrange
        var (_, manager, member) = await ProjectWithMembersAsync(_host);
        await _host.History.ImportItemsAsync(manager, "WEB", Batch(Row("same", member.PersonId)), CancellationToken.None);

        // Act
        var comments = await _host.History.LookupAsync(manager, Lookup(HistoryRefKind.Comment, "same"), CancellationToken.None);

        // Assert
        Assert.That(comments.Value, Is.Empty);
    }

    [Test]
    public async Task LookupAsync_CallerWhoManagesNoProject_IsRefused()
    {
        // Arrange
        var (scenario, manager, member) = await ProjectWithMembersAsync(_host);
        await _host.History.ImportItemsAsync(manager, "WEB", Batch(Row("m-1", member.PersonId)), CancellationToken.None);
        var visible = await scenario.MemberAsync("WEB", "viewer");
        var outsider = scenario.Person();

        // Act
        var forViewer = await _host.History.LookupAsync(visible, Lookup(HistoryRefKind.Item, "m-1"), CancellationToken.None);
        var forManager = await _host.History.LookupAsync(manager, Lookup(HistoryRefKind.Item, "m-1"), CancellationToken.None);
        var forOutsider = await _host.History.LookupAsync(outsider, Lookup(HistoryRefKind.Item, "m-1"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(forManager.Value.Select(r => r.ItemKey), Is.EqualTo(new[] { "WEB-1" }));
            Assert.That(forViewer.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(forOutsider.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
        }
    }

    [Test]
    public async Task LookupAsync_RefsInAProjectTheCallerDoesNotManage_AreFilteredOutWithoutRefusingTheRest()
    {
        // Arrange
        var (scenario, manager, member) = await ProjectWithMembersAsync(_host);
        await scenario.CreateProjectAsync("OPS");
        var opsPerson = await scenario.MemberAsync("OPS", "editor");
        await _host.Projects.SetMemberAsync(scenario.Admin, "OPS", manager.PersonId, "editor", CancellationToken.None);
        await _host.History.ImportItemsAsync(manager, "WEB", Batch(Row("w-1", member.PersonId)), CancellationToken.None);
        await _host.History.ImportItemsAsync(scenario.Admin, "OPS", Batch(Row("o-1", opsPerson.PersonId)), CancellationToken.None);

        // Act
        var result = await _host.History.LookupAsync(manager, Lookup(HistoryRefKind.Item, "w-1", "o-1"), CancellationToken.None);

        // Assert
        Assert.That(result.Value.Select(r => (r.ExternalId, r.ItemKey)), Is.EqualTo(new[] { ("w-1", "WEB-1") }));
    }

    [Test]
    public async Task LookupAsync_AnotherOrganization_FindsNothing()
    {
        // Arrange
        var (_, manager, member) = await ProjectWithMembersAsync(_host);
        await _host.History.ImportItemsAsync(manager, "WEB", Batch(Row("m-1", member.PersonId)), CancellationToken.None);
        var other = new Scenario(_host);

        // Act
        var result = await _host.History.LookupAsync(other.Admin, Lookup(HistoryRefKind.Item, "m-1"), CancellationToken.None);

        // Assert
        Assert.That(result.Value, Is.Empty);
    }

    [Test]
    public async Task LookupAsync_BadRequests_AreHistoryInvalid()
    {
        // Arrange
        var admin = new Scenario(_host).Admin;
        var tooMany = new HistoryLookup { Source = Source, Kind = HistoryRefKind.Item, ExternalIds = [.. Enumerable.Range(0, HistoryLookup.MaxIds + 1).Select(n => $"i-{n}")] };

        // Act
        var badSource = await _host.History.LookupAsync(admin, new HistoryLookup { Source = "X", Kind = HistoryRefKind.Item, ExternalIds = ["a"] }, CancellationToken.None);
        var badKind = await _host.History.LookupAsync(admin, new HistoryLookup { Source = Source, Kind = (HistoryRefKind)9, ExternalIds = ["a"] }, CancellationToken.None);
        var badId = await _host.History.LookupAsync(admin, Lookup(HistoryRefKind.Item, "ok", "有 空白"), CancellationToken.None);
        var many = await _host.History.LookupAsync(admin, tooMany, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((badSource.Failure?.Code, Paths(badSource.Failure)), Is.EqualTo((WorkErrorCodes.HistoryInvalid, new[] { "source" })));
            Assert.That(Paths(badKind.Failure), Is.EqualTo(new[] { "kind" }));
            Assert.That(Paths(badId.Failure), Is.EqualTo(new[] { "externalIds/1" }));
            Assert.That(Paths(many.Failure), Is.EqualTo(new[] { "externalIds" }));
        }
    }

    [Test]
    public async Task LookupAsync_ContributorWithoutProjectManage_IsRefusedWhileAManagerStillSeesTheRef()
    {
        // Arrange
        var (scenario, manager, member) = await ProjectWithMembersAsync(_host);
        var contributor = await scenario.MemberAsync("WEB", "contributor");
        await _host.History.ImportItemsAsync(manager, "WEB", Batch(Row("m-1", member.PersonId)), CancellationToken.None);

        // Act
        var refused = await _host.History.LookupAsync(contributor, Lookup(HistoryRefKind.Item, "m-1"), CancellationToken.None);
        var allowed = await _host.History.LookupAsync(manager, Lookup(HistoryRefKind.Item, "m-1"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(refused.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(allowed.Value.Select(r => r.ExternalId), Is.EqualTo(new[] { "m-1" }));
        }
    }

    private static HistoryLookup Lookup(HistoryRefKind kind, params string[] ids) => new() { Source = Source, Kind = kind, ExternalIds = ids };
}
