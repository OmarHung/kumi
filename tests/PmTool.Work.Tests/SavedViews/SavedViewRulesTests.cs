using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using static PmTool.Work.Tests.SavedViews.SavedViewCrudTests;

namespace PmTool.Work.Tests.SavedViews;

/// <summary>檢視的名稱唯一、數量上限（含同時送出）與每一個欄位的驗證。</summary>
[TestFixture]
public sealed class SavedViewRulesTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [TestCase("本週到期", "本週到期")]
    [TestCase("Weekly", "weekly")]
    [TestCase("Weekly", "  WEEKLY ")]
    public async Task CreateAsync_SameNameIgnoringCase_IsNameTaken(string first, string second)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await Create(scenario.Admin, first);

        // Act
        var result = await Create(scenario.Admin, second);

        // Assert
        AssertFailure(result, FailureKind.Conflict, WorkErrorCodes.SavedViewNameTaken);
    }

    [Test]
    public async Task CreateAsync_SameNameForAnotherPersonOrProject_IsAllowed()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateProjectAsync("OPS");
        var editor = await scenario.MemberAsync("WEB", "editor");
        await Create(scenario.Admin, "共同的名字");

        // Act
        var otherPerson = await Create(editor, "共同的名字");
        var otherProject = await Create(scenario.Admin, "共同的名字", "OPS");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(otherPerson.Succeeded, Is.True);
            Assert.That(otherProject.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task CreateAsync_SameNameSentConcurrently_OnlyOneSucceeds()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => CreateInOwnScope(scenario.Admin, i % 2 == 0 ? "同名" : "同名".ToUpperInvariant() + " ")));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(1));
            Assert.That(results.Where(r => !r.Succeeded).Select(r => r.Failure!.Code), Has.All.EqualTo(WorkErrorCodes.SavedViewNameTaken));
        }
    }

    [Test]
    public async Task CreateAsync_AtTheLimit_IsLimitReachedUntilOneIsDeleted()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateProjectAsync("OPS");
        var last = default(SavedView);
        for (var i = 0; i < SavedViewRules.MaxPerProject; i++)
        {
            last = (await Create(scenario.Admin, $"檢視 {i}")).Value;
        }

        // Act
        var over = await Create(scenario.Admin, "多一個");
        var otherProject = await Create(scenario.Admin, "多一個", "OPS");
        await _host.SavedViews.DeleteAsync(scenario.Admin, "WEB", last!.Id, CancellationToken.None);
        var afterDelete = await Create(scenario.Admin, "多一個");

        // Assert
        AssertFailure(over, FailureKind.Conflict, WorkErrorCodes.SavedViewLimitReached);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(otherProject.Succeeded, Is.True, "上限是每個專案各算");
            Assert.That(afterDelete.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task CreateAsync_SentConcurrentlyPastTheLimit_NeverExceedsIt()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        for (var i = 0; i < SavedViewRules.MaxPerProject - 3; i++)
        {
            await Create(scenario.Admin, $"檢視 {i}");
        }

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => CreateInOwnScope(scenario.Admin, $"同時 {i}")));
        var listed = await _host.SavedViews.ListAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(3));
            Assert.That(results.Where(r => !r.Succeeded).Select(r => r.Failure!.Code), Has.All.EqualTo(WorkErrorCodes.SavedViewLimitReached));
            Assert.That(listed.Value, Has.Count.EqualTo(SavedViewRules.MaxPerProject));
        }
    }

    [Test]
    public async Task CreateAsync_FullOfProblems_ReportsEachWithItsPath()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var query = new SavedViewQuery("Task", "x", "someone", new string('字', 201), "due_date", "tomorrow", "huge");

        // Act
        var result = await _host.SavedViews.CreateAsync(
            scenario.Admin, "WEB", new SavedViewDraft(" ", (SavedViewLayout)9, query), CancellationToken.None);

        // Assert
        Assert.That(result.Failure!.Kind, Is.EqualTo(FailureKind.Invalid));
        Assert.That(
            result.Failure.Details.Select(d => d.Path),
            Is.EquivalentTo(new[] { "name", "layout", "query.type", "query.state", "query.assignee", "query.q", "query.sort", "query.due", "query.priority" }));
        Assert.That(result.Failure.Details, Has.All.Matches<FailureDetail>(d => d.Code == WorkErrorCodes.SavedViewInvalid));
    }

    [TestCase("name", "")]
    [TestCase("name", "   ")]
    [TestCase("name", "名字\n換行")]
    [TestCase("name", "名字\0")]
    [TestCase("name", "名\t字")]
    [TestCase("query.type", "t")]
    [TestCase("query.type", "Task")]
    [TestCase("query.type", "1task")]
    [TestCase("query.type", "ta-sk")]
    [TestCase("query.type", "task\n")]
    [TestCase("query.state", "in progress")]
    [TestCase("query.state", "a")]
    [TestCase("query.assignee", "Me")]
    [TestCase("query.assignee", "not-a-guid")]
    [TestCase("query.assignee", "{3f2504e0-4f89-11d3-9a0c-0305e82c3301}")]
    [TestCase("query.q", "搜\0尋")]
    [TestCase("query.q", "搜\n尋")]
    [TestCase("query.sort", "due_date")]
    [TestCase("query.sort", "due_date:up")]
    [TestCase("query.sort", "nope:asc")]
    [TestCase("query.sort", "DueDate:asc")]
    [TestCase("query.sort", "due_date:asc\n")]
    [TestCase("query.sort", "project:asc")]
    [TestCase("query.due", "tomorrow")]
    [TestCase("query.due", "Week")]
    [TestCase("query.priority", "critical")]
    [TestCase("query.priority", "")]
    public async Task CreateAsync_BadValue_IsInvalidAtThatPath(string path, string value)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var draft = Apply(new SavedViewDraft("名字", SavedViewLayout.Table, Empty), path, value);

        // Act
        var result = await _host.SavedViews.CreateAsync(scenario.Admin, "WEB", draft, CancellationToken.None);

        // Assert
        AssertFailure(result, FailureKind.Invalid, WorkErrorCodes.SavedViewInvalid);
        Assert.That(result.Failure!.Details.Select(d => d.Path), Is.EqualTo(new[] { path }));
    }

    [Test]
    public async Task CreateAsync_NameWithALoneSurrogate_IsInvalid()
    {
        // Arrange：落單的代理字元轉不成 UTF-8，存不進資料庫。TestCase 的參數會把它換成別的字元，所以在這裡組。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await Create(scenario.Admin, "名" + (char)0xD800);

        // Assert
        AssertFailure(result, FailureKind.Invalid, WorkErrorCodes.SavedViewInvalid);
    }

    [Test]
    public async Task CreateAsync_NameTooLong_IsInvalidButFiftyCharactersIsFine()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var fifty = await Create(scenario.Admin, new string('名', SavedViewRules.MaxNameLength));
        var fiftyOne = await Create(scenario.Admin, new string('名', SavedViewRules.MaxNameLength + 1));

        // Assert
        Assert.That(fifty.Succeeded, Is.True);
        AssertFailure(fiftyOne, FailureKind.Invalid, WorkErrorCodes.SavedViewInvalid);
    }

    [TestCase("due_date:asc")]
    [TestCase("number:desc")]
    [TestCase("created_at:asc")]
    [TestCase("updated_at:desc")]
    [TestCase("start_date:asc")]
    public async Task CreateAsync_EverySortFieldOfTheApi_IsAccepted(string sort)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await Create(scenario.Admin, "排序", query: Empty with { Sort = sort });

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
    }

    [TestCase("me")]
    [TestCase("unassigned")]
    [TestCase("3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    public async Task CreateAsync_AssigneeForms_AreAccepted(string assignee)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await Create(scenario.Admin, "負責人", query: Empty with { Assignee = assignee });

        // Assert
        Assert.That(result.Value.Query.Assignee, Is.EqualTo(assignee));
    }

    [TestCase("me")]
    [TestCase("3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    public async Task CreateAsync_CreatedByForms_AreAcceptedAndKept(string createdBy)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await Create(scenario.Admin, "我建立的", query: Empty with { CreatedBy = createdBy });
        var listed = await _host.SavedViews.ListAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        Assert.That(result.Value.Query.CreatedBy, Is.EqualTo(createdBy));
        Assert.That(listed.Value.Single().Query.CreatedBy, Is.EqualTo(createdBy));
    }

    [Test]
    public async Task CreateAsync_OpenStaleAndDueSoon_AreAcceptedAndKept()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var query = Empty with { Due = "soon", Open = true, Stale = true };

        // Act
        var result = await Create(scenario.Admin, "停滯的", query: query);
        var listed = await _host.SavedViews.ListAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        var kept = listed.Value.Single().Query;
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        Assert.That((kept.Due, kept.Open, kept.Stale), Is.EqualTo(("soon", (bool?)true, (bool?)true)));
    }

    [Test]
    public async Task CreateAsync_OpenAndStaleFalse_AreStoredAsNull()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var query = Empty with { Open = false, Stale = false };

        // Act
        var result = await Create(scenario.Admin, "關掉的", query: query);
        var listed = await _host.SavedViews.ListAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        var kept = listed.Value.Single().Query;
        Assert.That((result.Value.Query.Open, result.Value.Query.Stale, kept.Open, kept.Stale), Is.EqualTo(((bool?)null, (bool?)null, (bool?)null, (bool?)null)));
    }

    [TestCase("query.createdBy", "someone")]
    [TestCase("query.createdBy", "unassigned")]
    [TestCase("query.createdBy", "ME")]
    [TestCase("query.mine", "created")]
    public async Task CreateAsync_BadCreatedByOrMyWorkOnlyScope_IsInvalidAtThatPath(string path, string value)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var query = path == "query.mine" ? Empty with { Mine = value } : Empty with { CreatedBy = value };

        // Act
        var result = await Create(scenario.Admin, "壞的", query: query);

        // Assert
        AssertFailure(result, FailureKind.Invalid, WorkErrorCodes.SavedViewInvalid);
        Assert.That(result.Failure!.Details.Select(d => d.Path), Is.EqualTo(new[] { path }));
    }

    [Test]
    public async Task ReplaceAsync_BadValue_IsInvalid()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var view = (await Create(scenario.Admin, "原本")).Value;

        // Act
        var result = await _host.SavedViews.ReplaceAsync(
            scenario.Admin, "WEB", view.Id, new SavedViewDraft("原本", SavedViewLayout.Table, Empty with { Due = "x" }), CancellationToken.None);
        var unchanged = await _host.SavedViews.ListAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        AssertFailure(result, FailureKind.Invalid, WorkErrorCodes.SavedViewInvalid);
        Assert.That(unchanged.Value, Is.EqualTo(new[] { view }));
    }

    [Test]
    public async Task CreateAsync_NullDraft_Throws()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act and Assert
        Assert.ThrowsAsync<ArgumentNullException>(() => _host.SavedViews.CreateAsync(scenario.Admin, "WEB", null!, CancellationToken.None));
    }

    private static SavedViewDraft Apply(SavedViewDraft draft, string path, string value) => path switch
    {
        "name" => draft with { Name = value },
        "query.type" => draft with { Query = draft.Query with { Type = value } },
        "query.state" => draft with { Query = draft.Query with { State = value } },
        "query.assignee" => draft with { Query = draft.Query with { Assignee = value } },
        "query.q" => draft with { Query = draft.Query with { Search = value } },
        "query.sort" => draft with { Query = draft.Query with { Sort = value } },
        "query.due" => draft with { Query = draft.Query with { Due = value } },
        "query.priority" => draft with { Query = draft.Query with { Priority = value } },
        _ => throw new ArgumentOutOfRangeException(nameof(path), path, "測試沒有這個路徑。"),
    };

    private Task<Result<SavedView>> Create(
        Actor actor, string name, string project = "WEB", SavedViewQuery? query = null) =>
        _host.SavedViews.CreateAsync(actor, project, new SavedViewDraft(name, SavedViewLayout.Table, query ?? Empty), CancellationToken.None);

    private async Task<Result<SavedView>> CreateInOwnScope(Actor actor, string name)
    {
        await using var scope = _host.Root.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISavedViewService>().CreateAsync(
            actor, "WEB", new SavedViewDraft(name, SavedViewLayout.Table, Empty), CancellationToken.None);
    }
}
