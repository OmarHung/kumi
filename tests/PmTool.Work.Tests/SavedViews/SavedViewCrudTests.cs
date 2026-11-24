using PmTool.Abstractions;

namespace PmTool.Work.Tests.SavedViews;

/// <summary>儲存的檢視：新增、列出、換掉、刪除，以及名稱與上限的規則。</summary>
[TestFixture]
public sealed class SavedViewCrudTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task CreateAsync_StoresTheQueryAndListsItBack()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var query = new SavedViewQuery("task", "in_progress", "me", "登入", "due_date:asc", "week", "high");

        // Act
        var created = await _host.SavedViews.CreateAsync(
            scenario.Admin, "WEB", new SavedViewDraft("我本週要交的", SavedViewLayout.Board, query), CancellationToken.None);
        var listed = await _host.SavedViews.ListAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        Assert.That(created.Succeeded, Is.True, () => created.Failure!.Message);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Value.Name, Is.EqualTo("我本週要交的"));
            Assert.That(created.Value.Layout, Is.EqualTo(SavedViewLayout.Board));
            Assert.That(created.Value.Query, Is.EqualTo(query));
            Assert.That(created.Value.CreatedAt, Is.EqualTo(_host.Clock.GetUtcNow()));
            Assert.That(created.Value.UpdatedAt, Is.EqualTo(created.Value.CreatedAt));
            Assert.That(listed.Value, Is.EqualTo(new[] { created.Value }));
        }
    }

    [Test]
    public async Task CreateAsync_EmptyQuery_StoresNulls()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var created = await Create(scenario.Admin, "全部", SavedViewLayout.Table);
        var listed = await _host.SavedViews.ListAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        Assert.That(listed.Value.Single().Query, Is.EqualTo(Empty));
        Assert.That(created.Value.Layout, Is.EqualTo(SavedViewLayout.Table));
    }

    [TestCase(SavedViewLayout.Calendar)]
    [TestCase(SavedViewLayout.Timeline)]
    public async Task CreateAsync_CalendarAndTimelineLayouts_AreStoredAndListedBack(SavedViewLayout layout)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var created = await Create(scenario.Admin, "排程", layout);
        var listed = await _host.SavedViews.ListAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        Assert.That(created.Succeeded, Is.True);
        Assert.That(listed.Value.Single().Layout, Is.EqualTo(layout));
    }

    [Test]
    public async Task ReplaceAsync_TableToTimeline_ChangesTheLayout()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var created = await Create(scenario.Admin, "排程");

        // Act
        var replaced = await _host.SavedViews.ReplaceAsync(
            scenario.Admin, "WEB", created.Value.Id, new SavedViewDraft("排程", SavedViewLayout.Timeline, Empty), CancellationToken.None);

        // Assert
        Assert.That(replaced.Value.Layout, Is.EqualTo(SavedViewLayout.Timeline));
    }

    [Test]
    public async Task CreateAsync_TrimsTheName()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var created = await Create(scenario.Admin, "  本週到期  ");

        // Assert
        Assert.That(created.Value.Name, Is.EqualTo("本週到期"));
    }

    [Test]
    public async Task ListAsync_SortsByName()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        foreach (var name in new[] { "c 檢視", "A 檢視", "b 檢視" })
        {
            await Create(scenario.Admin, name);
        }

        // Act
        var listed = await _host.SavedViews.ListAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        Assert.That(listed.Value.Select(v => v.Name), Is.EqualTo(new[] { "A 檢視", "b 檢視", "c 檢視" }));
    }

    [Test]
    public async Task ListAsync_LowerCaseProjectKey_Works()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await Create(scenario.Admin, "一");

        // Act
        var listed = await _host.SavedViews.ListAsync(scenario.Admin, "web", CancellationToken.None);

        // Assert
        Assert.That(listed.Value, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ReplaceAsync_ReplacesEverythingAndKeepsCreatedAt()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var created = (await Create(scenario.Admin, "舊名字", query: Empty with { Priority = "low" })).Value;
        _host.Clock.Advance(TimeSpan.FromMinutes(5));
        var draft = new SavedViewDraft("新名字", SavedViewLayout.Board, Empty with { Due = "overdue" });

        // Act
        var replaced = await _host.SavedViews.ReplaceAsync(scenario.Admin, "WEB", created.Id, draft, CancellationToken.None);
        var listed = await _host.SavedViews.ListAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        Assert.That(replaced.Succeeded, Is.True, () => replaced.Failure!.Message);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(replaced.Value.Id, Is.EqualTo(created.Id));
            Assert.That(replaced.Value.Name, Is.EqualTo("新名字"));
            Assert.That(replaced.Value.Layout, Is.EqualTo(SavedViewLayout.Board));
            Assert.That(replaced.Value.Query, Is.EqualTo(draft.Query), "沒給的條件被清掉，不是保留舊的");
            Assert.That(replaced.Value.CreatedAt, Is.EqualTo(created.CreatedAt));
            Assert.That(replaced.Value.UpdatedAt, Is.EqualTo(created.CreatedAt + TimeSpan.FromMinutes(5)));
            Assert.That(listed.Value, Is.EqualTo(new[] { replaced.Value }));
        }
    }

    [Test]
    public async Task ReplaceAsync_KeepingItsOwnName_IsNotAConflict()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var created = (await Create(scenario.Admin, "我的檢視")).Value;

        // Act
        var replaced = await _host.SavedViews.ReplaceAsync(
            scenario.Admin, "WEB", created.Id,
            new SavedViewDraft("我的檢視", SavedViewLayout.Table, Empty with { Due = "today" }), CancellationToken.None);

        // Assert
        Assert.That(replaced.Succeeded, Is.True, () => replaced.Failure!.Message);
    }

    [Test]
    public async Task ReplaceAsync_ToTheNameOfAnotherOwnView_IsNameTaken()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await Create(scenario.Admin, "甲");
        var other = (await Create(scenario.Admin, "乙")).Value;

        // Act
        var replaced = await _host.SavedViews.ReplaceAsync(
            scenario.Admin, "WEB", other.Id, new SavedViewDraft("甲", SavedViewLayout.Table, Empty), CancellationToken.None);

        // Assert
        AssertFailure(replaced, FailureKind.Conflict, WorkErrorCodes.SavedViewNameTaken);
    }

    [Test]
    public async Task DeleteAsync_RemovesTheView()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var created = (await Create(scenario.Admin, "要刪的")).Value;
        await Create(scenario.Admin, "留著");

        // Act
        var deleted = await _host.SavedViews.DeleteAsync(scenario.Admin, "WEB", created.Id, CancellationToken.None);
        var listed = await _host.SavedViews.ListAsync(scenario.Admin, "WEB", CancellationToken.None);
        var again = await _host.SavedViews.DeleteAsync(scenario.Admin, "WEB", created.Id, CancellationToken.None);

        // Assert
        Assert.That(deleted.Succeeded, Is.True, () => deleted.Failure!.Message);
        Assert.That(listed.Value.Select(v => v.Name), Is.EqualTo(new[] { "留著" }));
        AssertFailure(again, FailureKind.NotFound, WorkErrorCodes.SavedViewNotFound);
    }

    [Test]
    public async Task DeleteAsync_FreesTheNameForReuse()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var created = (await Create(scenario.Admin, "可重用")).Value;
        await _host.SavedViews.DeleteAsync(scenario.Admin, "WEB", created.Id, CancellationToken.None);

        // Act
        var again = await Create(scenario.Admin, "可重用");

        // Assert
        Assert.That(again.Succeeded, Is.True, () => again.Failure!.Message);
    }

    [Test]
    public async Task ReplaceAsync_UnknownId_IsNotFound()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.SavedViews.ReplaceAsync(
            scenario.Admin, "WEB", Guid.NewGuid(), new SavedViewDraft("名", SavedViewLayout.Table, Empty), CancellationToken.None);

        // Assert
        AssertFailure(result, FailureKind.NotFound, WorkErrorCodes.SavedViewNotFound);
    }

    [Test]
    public async Task ReplaceAsync_ViewOfAnotherProject_IsNotFound()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateProjectAsync("OPS");
        var view = (await Create(scenario.Admin, "在 WEB")).Value;

        // Act
        var result = await _host.SavedViews.DeleteAsync(scenario.Admin, "OPS", view.Id, CancellationToken.None);

        // Assert
        AssertFailure(result, FailureKind.NotFound, WorkErrorCodes.SavedViewNotFound);
    }

    internal Task<Result<SavedView>> Create(
        Actor actor, string name, SavedViewLayout layout = SavedViewLayout.Table, SavedViewQuery? query = null, string project = "WEB") =>
        _host.SavedViews.CreateAsync(actor, project, new SavedViewDraft(name, layout, query ?? Empty), CancellationToken.None);

    internal static SavedViewQuery Empty { get; } = new(null, null, null, null, null, null, null);

    internal static void AssertFailure<T>(Result<T> result, FailureKind kind, string code)
    {
        Assert.That(result.Failure, Is.Not.Null, "應該失敗");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Kind, Is.EqualTo(kind));
            Assert.That(result.Failure.Code, Is.EqualTo(code));
        }
    }
}
