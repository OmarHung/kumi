using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using static PmTool.Work.Tests.SavedViews.SavedViewCrudTests;

namespace PmTool.Work.Tests.SavedViews;

/// <summary>「我的工作」的檢視：不屬於任何專案；新增、列出、換掉、刪除、名稱唯一、上限，和專案的檢視互不影響。</summary>
[TestFixture]
public sealed class MyWorkViewTests
{
    private static readonly SavedViewQuery Filtered = Empty with { Category = "done", Group = "project", Project = "WEB" };

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task CreateMyWorkAsync_StoresTheQueryAndListsItBackWithoutAnyProject()
    {
        // Arrange：這個人不是任何專案的成員。
        var person = new Scenario(_host).Person();
        var draft = new SavedViewDraft("完成", SavedViewLayout.Board, Filtered);

        // Act
        var created = await _host.SavedViews.CreateMyWorkAsync(person, draft, CancellationToken.None);
        var listed = await _host.SavedViews.ListMyWorkAsync(person, CancellationToken.None);

        // Assert
        Assert.That(created.Succeeded, Is.True, () => created.Failure!.Message);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Value.Name, Is.EqualTo("完成"));
            Assert.That(created.Value.Layout, Is.EqualTo(SavedViewLayout.Board));
            Assert.That(created.Value.Query, Is.EqualTo(Filtered));
            Assert.That(created.Value.UpdatedAt, Is.EqualTo(created.Value.CreatedAt));
            Assert.That(listed, Is.EqualTo(new[] { created.Value }));
        }
    }

    [Test]
    public async Task CreateMyWorkAsync_ProjectCodeIsStoredInUpperCase()
    {
        // Arrange
        var person = new Scenario(_host).Person();

        // Act
        var created = await CreateAsync(person, "CM", query: Empty with { Project = "cm" });

        // Assert
        Assert.That(created.Value.Query.Project, Is.EqualTo("CM"));
    }

    [Test]
    public async Task ListMyWorkAsync_SortsByNameIgnoringCaseAndOnlyListsMyOwn()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var other = scenario.Person();
        foreach (var name in new[] { "c 檢視", "A 檢視", "b 檢視" })
        {
            await CreateAsync(scenario.Admin, name);
        }

        await CreateAsync(other, "別人的");

        // Act
        var mine = await _host.SavedViews.ListMyWorkAsync(scenario.Admin, CancellationToken.None);

        // Assert
        Assert.That(mine.Select(v => v.Name), Is.EqualTo(new[] { "A 檢視", "b 檢視", "c 檢視" }));
    }

    [Test]
    public async Task ListMyWorkAsync_PersonInAnotherOrganization_SeesNothing()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await CreateAsync(scenario.Admin, "我的");
        var elsewhere = new Scenario(_host).Admin with { PersonId = scenario.Admin.PersonId };

        // Act
        var listed = await _host.SavedViews.ListMyWorkAsync(elsewhere, CancellationToken.None);

        // Assert
        Assert.That(listed, Is.Empty);
    }

    [TestCase("本週到期", "本週到期")]
    [TestCase("Weekly", "weekly")]
    [TestCase("Weekly", "  WEEKLY ")]
    public async Task CreateMyWorkAsync_SameNameIgnoringCase_IsNameTaken(string first, string second)
    {
        // Arrange
        var person = new Scenario(_host).Person();
        await CreateAsync(person, first);

        // Act
        var result = await CreateAsync(person, second);

        // Assert
        AssertFailure(result, FailureKind.Conflict, WorkErrorCodes.SavedViewNameTaken);
    }

    [Test]
    public async Task CreateMyWorkAsync_SameNameAsAProjectViewOrAnotherPerson_IsAllowed()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var other = scenario.Person();
        await _host.SavedViews.CreateAsync(scenario.Admin, "WEB", new SavedViewDraft("共同的名字", SavedViewLayout.Table, Empty), CancellationToken.None);

        // Act
        var sameAsProjectView = await CreateAsync(scenario.Admin, "共同的名字");
        var otherPerson = await CreateAsync(other, "共同的名字");
        var projectViewAfter = await _host.SavedViews.CreateAsync(
            scenario.Admin, "WEB", new SavedViewDraft("我的工作裡的名字", SavedViewLayout.Table, Empty), CancellationToken.None);
        await CreateAsync(scenario.Admin, "我的工作裡的名字");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sameAsProjectView.Succeeded, Is.True);
            Assert.That(otherPerson.Succeeded, Is.True);
            Assert.That(projectViewAfter.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task CreateMyWorkAsync_SameNameSentConcurrently_OnlyOneSucceeds()
    {
        // Arrange
        var person = new Scenario(_host).Person();

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => InOwnScopeAsync(person, i % 2 == 0 ? "同名" : "同名 ")));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(1));
            Assert.That(results.Where(r => !r.Succeeded).Select(r => r.Failure!.Code), Has.All.EqualTo(WorkErrorCodes.SavedViewNameTaken));
        }
    }

    [Test]
    public async Task CreateMyWorkAsync_AtTheLimit_IsLimitReachedUntilOneIsDeletedAndProjectViewsDoNotCount()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var person = scenario.Admin;
        var last = default(SavedView);
        for (var i = 0; i < SavedViewRules.MaxMyWork; i++)
        {
            last = (await CreateAsync(person, $"檢視 {i}")).Value;
        }

        // Act
        var over = await CreateAsync(person, "多一個");
        var projectView = await _host.SavedViews.CreateAsync(person, "WEB", new SavedViewDraft("多一個", SavedViewLayout.Table, Empty), CancellationToken.None);
        await _host.SavedViews.DeleteMyWorkAsync(person, last!.Id, CancellationToken.None);
        var afterDelete = await CreateAsync(person, "多一個");

        // Assert
        AssertFailure(over, FailureKind.Conflict, WorkErrorCodes.SavedViewLimitReached);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(over.Failure!.Message, Does.Contain("我的工作"));
            Assert.That(projectView.Succeeded, Is.True, "專案的檢視另外算");
            Assert.That(afterDelete.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task CreateMyWorkAsync_SentConcurrentlyPastTheLimit_NeverExceedsIt()
    {
        // Arrange
        var person = new Scenario(_host).Person();
        for (var i = 0; i < SavedViewRules.MaxMyWork - 3; i++)
        {
            await CreateAsync(person, $"檢視 {i}");
        }

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => InOwnScopeAsync(person, $"同時 {i}")));
        var listed = await _host.SavedViews.ListMyWorkAsync(person, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(3));
            Assert.That(results.Where(r => !r.Succeeded).Select(r => r.Failure!.Code), Has.All.EqualTo(WorkErrorCodes.SavedViewLimitReached));
            Assert.That(listed, Has.Count.EqualTo(SavedViewRules.MaxMyWork));
        }
    }

    [Test]
    public async Task ReplaceMyWorkAsync_ReplacesEverythingKeepsCreatedAtAndAllowsKeepingItsOwnName()
    {
        // Arrange
        var person = new Scenario(_host).Person();
        var created = (await CreateAsync(person, "我的檢視", query: Filtered)).Value;
        _host.Clock.Advance(TimeSpan.FromMinutes(5));

        // Act
        var sameName = await _host.SavedViews.ReplaceMyWorkAsync(
            person, created.Id, new SavedViewDraft("我的檢視", SavedViewLayout.Timeline, Empty with { Due = "week" }), CancellationToken.None);
        var listed = await _host.SavedViews.ListMyWorkAsync(person, CancellationToken.None);

        // Assert
        Assert.That(sameName.Succeeded, Is.True, () => sameName.Failure!.Message);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sameName.Value.Layout, Is.EqualTo(SavedViewLayout.Timeline));
            Assert.That(sameName.Value.Query, Is.EqualTo(Empty with { Due = "week" }), "沒給的條件被清掉");
            Assert.That(sameName.Value.CreatedAt, Is.EqualTo(created.CreatedAt));
            Assert.That(sameName.Value.UpdatedAt, Is.GreaterThan(created.UpdatedAt));
            Assert.That(listed, Is.EqualTo(new[] { sameName.Value }));
        }
    }

    [Test]
    public async Task ReplaceMyWorkAsync_NameOfAnotherViewOfMine_IsNameTaken()
    {
        // Arrange
        var person = new Scenario(_host).Person();
        await CreateAsync(person, "甲");
        var second = (await CreateAsync(person, "乙")).Value;

        // Act
        var result = await _host.SavedViews.ReplaceMyWorkAsync(
            person, second.Id, new SavedViewDraft("甲", SavedViewLayout.Table, Empty), CancellationToken.None);

        // Assert
        AssertFailure(result, FailureKind.Conflict, WorkErrorCodes.SavedViewNameTaken);
    }

    [Test]
    public async Task ReplaceAndDeleteMyWorkAsync_AnotherPersonsViewProjectViewOrMissingView_AreNotFound()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var owner = scenario.Admin;
        var other = scenario.Person();
        var mine = (await CreateAsync(owner, "我的")).Value;
        var projectView = (await _host.SavedViews.CreateAsync(owner, "WEB", new SavedViewDraft("專案的", SavedViewLayout.Table, Empty), CancellationToken.None)).Value;
        var draft = new SavedViewDraft("改", SavedViewLayout.Table, Empty);

        // Act
        var replaceOthers = await _host.SavedViews.ReplaceMyWorkAsync(other, mine.Id, draft, CancellationToken.None);
        var deleteOthers = await _host.SavedViews.DeleteMyWorkAsync(other, mine.Id, CancellationToken.None);
        var replaceProjectView = await _host.SavedViews.ReplaceMyWorkAsync(owner, projectView.Id, draft, CancellationToken.None);
        var deleteProjectView = await _host.SavedViews.DeleteMyWorkAsync(owner, projectView.Id, CancellationToken.None);
        var replaceMissing = await _host.SavedViews.ReplaceMyWorkAsync(owner, Guid.NewGuid(), draft, CancellationToken.None);
        var stillThere = await _host.SavedViews.ListMyWorkAsync(owner, CancellationToken.None);

        // Assert
        AssertFailure(replaceOthers, FailureKind.NotFound, WorkErrorCodes.SavedViewNotFound);
        AssertFailure(deleteOthers, FailureKind.NotFound, WorkErrorCodes.SavedViewNotFound);
        AssertFailure(replaceProjectView, FailureKind.NotFound, WorkErrorCodes.SavedViewNotFound);
        AssertFailure(deleteProjectView, FailureKind.NotFound, WorkErrorCodes.SavedViewNotFound);
        AssertFailure(replaceMissing, FailureKind.NotFound, WorkErrorCodes.SavedViewNotFound);
        Assert.That(stillThere.Select(v => v.Name), Is.EqualTo(new[] { "我的" }));
    }

    [Test]
    public async Task ProjectViewMethods_AMyWorkView_IsNotFoundThereAndNotListed()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var mine = (await CreateAsync(scenario.Admin, "我的工作的")).Value;

        // Act
        var listed = await _host.SavedViews.ListAsync(scenario.Admin, "WEB", CancellationToken.None);
        var replaced = await _host.SavedViews.ReplaceAsync(
            scenario.Admin, "WEB", mine.Id, new SavedViewDraft("改", SavedViewLayout.Table, Empty), CancellationToken.None);
        var deleted = await _host.SavedViews.DeleteAsync(scenario.Admin, "WEB", mine.Id, CancellationToken.None);

        // Assert
        Assert.That(listed.Value, Is.Empty);
        AssertFailure(replaced, FailureKind.NotFound, WorkErrorCodes.SavedViewNotFound);
        AssertFailure(deleted, FailureKind.NotFound, WorkErrorCodes.SavedViewNotFound);
    }

    [Test]
    public async Task DeleteMyWorkAsync_RemovesTheViewAndWorksWithoutAnyProject()
    {
        // Arrange
        var person = new Scenario(_host).Person();
        var created = (await CreateAsync(person, "要刪的")).Value;

        // Act
        var deleted = await _host.SavedViews.DeleteMyWorkAsync(person, created.Id, CancellationToken.None);
        var listed = await _host.SavedViews.ListMyWorkAsync(person, CancellationToken.None);

        // Assert
        Assert.That(deleted.Succeeded, Is.True);
        Assert.That(listed, Is.Empty);
    }

    private Task<Result<SavedView>> CreateAsync(Actor actor, string name, SavedViewLayout layout = SavedViewLayout.Table, SavedViewQuery? query = null) =>
        _host.SavedViews.CreateMyWorkAsync(actor, new SavedViewDraft(name, layout, query ?? Empty), CancellationToken.None);

    private async Task<Result<SavedView>> InOwnScopeAsync(Actor actor, string name)
    {
        await using var scope = _host.Root.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISavedViewService>().CreateMyWorkAsync(
            actor, new SavedViewDraft(name, SavedViewLayout.Table, Empty), CancellationToken.None);
    }
}
