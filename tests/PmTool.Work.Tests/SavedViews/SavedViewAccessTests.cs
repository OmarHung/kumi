using PmTool.Abstractions;
using static PmTool.Work.Tests.SavedViews.SavedViewCrudTests;

namespace PmTool.Work.Tests.SavedViews;

/// <summary>檢視只有建立的人看得到、動得了；看不到的專案一律當成不存在。</summary>
[TestFixture]
public sealed class SavedViewAccessTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ListAsync_ShowsOnlyTheCallersOwnViews()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var editor = await scenario.MemberAsync("WEB", "editor");
        await Create(scenario.Admin, "管理員的");
        await Create(editor, "編輯者的");

        // Act
        var admins = await _host.SavedViews.ListAsync(scenario.Admin, "WEB", CancellationToken.None);
        var editors = await _host.SavedViews.ListAsync(editor, "WEB", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(admins.Value.Select(v => v.Name), Is.EqualTo(new[] { "管理員的" }));
            Assert.That(editors.Value.Select(v => v.Name), Is.EqualTo(new[] { "編輯者的" }));
        }
    }

    [Test]
    public async Task ReplaceAndDelete_ViewOfSomeoneElse_AreNotFoundAndLeaveItUntouched()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var editor = await scenario.MemberAsync("WEB", "editor");
        var theirs = (await Create(editor, "別人的")).Value;

        // Act
        var replaced = await _host.SavedViews.ReplaceAsync(
            scenario.Admin, "WEB", theirs.Id, new SavedViewDraft("偷改", SavedViewLayout.Board, Empty), CancellationToken.None);
        var deleted = await _host.SavedViews.DeleteAsync(scenario.Admin, "WEB", theirs.Id, CancellationToken.None);
        var stillThere = await _host.SavedViews.ListAsync(editor, "WEB", CancellationToken.None);

        // Assert
        AssertFailure(replaced, FailureKind.NotFound, WorkErrorCodes.SavedViewNotFound);
        AssertFailure(deleted, FailureKind.NotFound, WorkErrorCodes.SavedViewNotFound);
        Assert.That(stillThere.Value, Is.EqualTo(new[] { theirs }));
    }

    [Test]
    public async Task ReplaceAsync_ToANameOnlySomeoneElseUses_Works()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var editor = await scenario.MemberAsync("WEB", "editor");
        await Create(editor, "別人用的名字");
        var mine = (await Create(scenario.Admin, "我的")).Value;

        // Act
        var replaced = await _host.SavedViews.ReplaceAsync(
            scenario.Admin, "WEB", mine.Id, new SavedViewDraft("別人用的名字", SavedViewLayout.Table, Empty), CancellationToken.None);

        // Assert
        Assert.That(replaced.Succeeded, Is.True, () => replaced.Failure!.Message);
    }

    [Test]
    public async Task EveryOperation_ProjectTheCallerCannotSee_IsProjectNotFound()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var stranger = scenario.Person();
        var otherOrgAdmin = new Scenario(_host).Admin;
        var draft = new SavedViewDraft("名", SavedViewLayout.Table, Empty);

        foreach (var actor in new[] { stranger, otherOrgAdmin })
        {
            // Act
            var results = new[]
            {
                Code(await _host.SavedViews.ListAsync(actor, "WEB", CancellationToken.None)),
                Code(await _host.SavedViews.CreateAsync(actor, "WEB", draft, CancellationToken.None)),
                Code(await _host.SavedViews.ReplaceAsync(actor, "WEB", Guid.NewGuid(), draft, CancellationToken.None)),
                Code(await _host.SavedViews.DeleteAsync(actor, "WEB", Guid.NewGuid(), CancellationToken.None)),
                Code(await _host.SavedViews.ListAsync(actor, "NOPE", CancellationToken.None)),
            };

            // Assert
            Assert.That(results, Has.All.EqualTo(WorkErrorCodes.ProjectNotFound));
        }
    }

    [Test]
    public async Task CreateAsync_ReadOnlyMemberAndOrganizationAdminWhoIsNotAMember_Work()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var viewer = await scenario.MemberAsync("WEB", "viewer");
        var otherAdmin = new Actor(scenario.OrganizationId, Guid.NewGuid(), IsOrganizationAdmin: true);

        // Act
        var byViewer = await Create(viewer, "檢視者的");
        var byAdmin = await Create(otherAdmin, "管理員的");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(byViewer.Succeeded, Is.True);
            Assert.That(byAdmin.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task RemovedMember_CanNoLongerListOrCreate()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var editor = await scenario.MemberAsync("WEB", "editor");
        await Create(editor, "之前建的");
        await _host.Projects.RemoveMemberAsync(scenario.Admin, "WEB", editor.PersonId, CancellationToken.None);

        // Act
        var listed = await _host.SavedViews.ListAsync(editor, "WEB", CancellationToken.None);
        var created = await Create(editor, "之後建的");

        // Assert
        Assert.That(Code(listed), Is.EqualTo(WorkErrorCodes.ProjectNotFound));
        Assert.That(Code(created), Is.EqualTo(WorkErrorCodes.ProjectNotFound));
    }

    [Test]
    public async Task EveryOperation_NulInTheProjectKeyOrAView_IsHandledAsNotFound()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var draft = new SavedViewDraft("名", SavedViewLayout.Table, Empty);

        // Act
        var results = new[]
        {
            Code(await _host.SavedViews.ListAsync(scenario.Admin, "WEB\0", CancellationToken.None)),
            Code(await _host.SavedViews.CreateAsync(scenario.Admin, "WEB\0", draft, CancellationToken.None)),
        };

        // Assert
        Assert.That(results, Has.All.EqualTo(WorkErrorCodes.ProjectNotFound));
    }

    private static string? Code<T>(Result<T> result) => result.Failure?.Code;

    private Task<Result<SavedView>> Create(Actor actor, string name) =>
        _host.SavedViews.CreateAsync(actor, "WEB", new SavedViewDraft(name, SavedViewLayout.Table, Empty), CancellationToken.None);
}
