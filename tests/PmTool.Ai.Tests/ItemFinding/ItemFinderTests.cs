using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai.ItemFinding;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Blueprint.Model;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Ai.Tests.ItemFinding;

/// <summary>find_items 的服務：每個條件、人員編號不在組織、關鍵字、筆數與 hasMore、結果補上專案名稱與人名。權限由工作引擎照 ItemScope 過濾（假的直接回）。</summary>
[TestFixture]
public sealed class ItemFinderTests
{
    private static readonly Guid WebId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OpsId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private AssistantHarness _h = null!;

    [SetUp]
    public async Task StartAsync()
    {
        _h = await AssistantHarness.StartAsync(configure: services => services.AddSingleton<IWorkAccessQueries>(new FakeWorkAccess()));
        _h.Projects.OnList = _ => [Project(WebId, "WEB", "網站改版"), Project(OpsId, "OPS", "維運")];
        _h.WorkItems.OnListAcross = (_, _) => new WorkItemPage([], null);
    }

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    private IAssistantItemFinder Finder => ActivatorUtilities.CreateInstance<AssistantItemFinder>(_h.Host.Services);

    [Test]
    public async Task Find_ByDefault_AsksForOpenItemsOfAnyoneAcrossAllProjectsSortedByDueDate()
    {
        // Act
        var result = await Finder.FindAsync(_h.Actor, null, new ItemFindQuery(30), CancellationToken.None);

        // Assert
        var call = _h.WorkItems.AcrossCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(call.Scope, Is.SameAs(CrossProjectScope.All));
            Assert.That(call.Query.AssigneeId, Is.Null);
            Assert.That(call.Query.OnlyUnassigned, Is.False);
            Assert.That(call.Query.CreatedBy, Is.Null);
            Assert.That(call.Query.StateCategories, Is.EqualTo(new[] { StateCategory.Backlog, StateCategory.NotStarted, StateCategory.InProgress }));
            Assert.That(call.Query.SortBy, Is.EqualTo(WorkItemSortField.DueDate));
            Assert.That(call.Query.Limit, Is.EqualTo(31));
        }
    }

    [Test]
    public async Task Find_AssigneeMe_FiltersByTheAskersOwnId()
    {
        // Act
        await Finder.FindAsync(_h.Actor, null, new ItemFindQuery(10) { Assignee = ItemFindAssignee.Me }, CancellationToken.None);

        // Assert
        Assert.That(_h.WorkItems.AcrossCalls.Single().Query.AssigneeId, Is.EqualTo(_h.Actor.PersonId));
    }

    [Test]
    public async Task Find_AssigneeNobody_AsksForUnassignedItems()
    {
        // Act
        await Finder.FindAsync(_h.Actor, null, new ItemFindQuery(10) { Assignee = ItemFindAssignee.Nobody }, CancellationToken.None);

        // Assert
        var query = _h.WorkItems.AcrossCalls.Single().Query;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query.OnlyUnassigned, Is.True);
            Assert.That(query.AssigneeId, Is.Null);
        }
    }

    [Test]
    public async Task Find_AssigneePerson_FiltersByThatPersonAndCreatorTogether()
    {
        // Arrange
        var ivan = Guid.CreateVersion7();
        var afang = Guid.CreateVersion7();
        _h.People.Set(_h.OrganizationId, ivan, OrganizationRole.Member, name: "Ivan");
        _h.People.Set(_h.OrganizationId, afang, OrganizationRole.Member, name: "阿芳");

        // Act
        var result = await Finder.FindAsync(
            _h.Actor, null, new ItemFindQuery(10) { Assignee = ItemFindAssignee.Person, AssigneeId = ivan, CreatedBy = afang }, CancellationToken.None);

        // Assert
        var query = _h.WorkItems.AcrossCalls.Single().Query;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(query.AssigneeId, Is.EqualTo(ivan));
            Assert.That(query.CreatedBy, Is.EqualTo(afang));
        }
    }

    [Test]
    public async Task Find_AssigneeNotInTheOrganization_IsAFieldErrorAndNothingIsQueried()
    {
        // Arrange
        _h.People.DefaultRole = null;

        // Act
        var result = await Finder.FindAsync(
            _h.Actor, null, new ItemFindQuery(10) { Assignee = ItemFindAssignee.Person, AssigneeId = Guid.CreateVersion7() }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
            Assert.That(result.Failure?.Details?.Single().Path, Is.EqualTo("assignee"));
            Assert.That(_h.WorkItems.AcrossCalls, Is.Empty);
        }
    }

    [Test]
    public async Task Find_CreatorNotInTheOrganization_IsAFieldErrorOnCreatedBy()
    {
        // Arrange
        _h.People.DefaultRole = null;

        // Act
        var result = await Finder.FindAsync(_h.Actor, null, new ItemFindQuery(10) { CreatedBy = Guid.CreateVersion7() }, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Details?.Single().Path, Is.EqualTo("createdBy"));
    }

    [Test]
    public async Task Find_AssigneePersonWithoutAnId_IsAFieldError()
    {
        // Act
        var result = await Finder.FindAsync(
            _h.Actor, null, new ItemFindQuery(10) { Assignee = ItemFindAssignee.Person }, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
    }

    [Test]
    public async Task Find_StateCategoriesAndDueRange_AreForwarded()
    {
        // Act
        await Finder.FindAsync(
            _h.Actor,
            null,
            new ItemFindQuery(10) { StateCategories = ["done", "canceled", "done"], DueFrom = new DateOnly(2026, 11, 30), DueTo = new DateOnly(2026, 12, 6) },
            CancellationToken.None);

        // Assert
        var query = _h.WorkItems.AcrossCalls.Single().Query;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query.StateCategories, Is.EqualTo(new[] { StateCategory.Done, StateCategory.Canceled }));
            Assert.That(query.DueFrom, Is.EqualTo(new DateOnly(2026, 11, 30)));
            Assert.That(query.DueTo, Is.EqualTo(new DateOnly(2026, 12, 6)));
        }
    }

    [Test]
    public async Task Find_Overdue_MeansDueBeforeTodayInTheOrganizationTimeZoneAndStillOpen()
    {
        // Arrange：測試時鐘是 2026-11-18 08:00 UTC，台北已經是同一天 16:00。
        var today = new DateOnly(2026, 11, 18);

        // Act
        await Finder.FindAsync(_h.Actor, null, new ItemFindQuery(10) { Overdue = true }, CancellationToken.None);
        await Finder.FindAsync(_h.Actor, null, new ItemFindQuery(10) { Overdue = true, DueTo = today.AddDays(-5) }, CancellationToken.None);

        // Assert
        var calls = _h.WorkItems.AcrossCalls;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(calls[0].Query.DueTo, Is.EqualTo(today.AddDays(-1)));
            Assert.That(calls[0].Query.StateCategories, Is.EqualTo(new[] { StateCategory.Backlog, StateCategory.NotStarted, StateCategory.InProgress }));
            Assert.That(calls[1].Query.DueTo, Is.EqualTo(today.AddDays(-5)));
        }
    }

    [TestCase("done")]
    [TestCase("canceled")]
    public async Task Find_OverdueWithAFinishedCategory_IsAFieldError(string category)
    {
        // Act
        var result = await Finder.FindAsync(
            _h.Actor, null, new ItemFindQuery(10) { Overdue = true, StateCategories = [category] }, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
    }

    [TestCase(0)]
    [TestCase(AiRules.MaxFindItemsLimit + 1)]
    [TestCase(-3)]
    public async Task Find_LimitOutOfRange_IsAFieldErrorOnLimit(int limit)
    {
        // Act
        var result = await Finder.FindAsync(_h.Actor, null, new ItemFindQuery(limit), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Details?.Single().Path, Is.EqualTo("limit"));
    }

    [Test]
    public async Task Find_UnknownStateCategory_IsAFieldError()
    {
        // Act
        var result = await Finder.FindAsync(_h.Actor, null, new ItemFindQuery(10) { StateCategories = ["所有"] }, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Details?.Single().Path, Is.EqualTo("stateCategory"));
    }

    [Test]
    public async Task Find_DueFromAfterDueTo_IsAFieldError()
    {
        // Act
        var result = await Finder.FindAsync(
            _h.Actor, null, new ItemFindQuery(10) { DueFrom = new DateOnly(2026, 12, 6), DueTo = new DateOnly(2026, 11, 30) }, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
    }

    [TestCase("改版\n")]
    [TestCase("a\0b")]
    [TestCase("tab\there")]
    public async Task Find_TextWithControlCharacters_IsAFieldErrorAndNothingIsQueried(string text)
    {
        // Act
        var result = await Finder.FindAsync(_h.Actor, null, new ItemFindQuery(10) { Text = text }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Details?.Single().Path, Is.EqualTo("text"));
            Assert.That(_h.WorkItems.AcrossCalls, Is.Empty);
        }
    }

    [Test]
    public async Task Find_TextLongerThanTheLimit_IsAFieldError()
    {
        // Act
        var result = await Finder.FindAsync(
            _h.Actor, null, new ItemFindQuery(10) { Text = new string('a', AiRules.MaxFindItemsTextLength + 1) }, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Details?.Single().Path, Is.EqualTo("text"));
    }

    [Test]
    public async Task Find_Text_IsTrimmedAndBlankMeansNoSearch()
    {
        // Act
        await Finder.FindAsync(_h.Actor, null, new ItemFindQuery(10) { Text = "  首頁 " }, CancellationToken.None);
        await Finder.FindAsync(_h.Actor, null, new ItemFindQuery(10) { Text = "   " }, CancellationToken.None);

        // Assert
        var calls = _h.WorkItems.AcrossCalls;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(calls[0].Query.Search, Is.EqualTo("首頁"));
            Assert.That(calls[1].Query.Search, Is.Null);
        }
    }

    [Test]
    public async Task Find_ProjectKeyWithControlCharacters_IsAFieldError()
    {
        // Act
        var result = await Finder.FindAsync(_h.Actor, null, new ItemFindQuery(10) { ProjectKey = "WEB\n" }, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Details?.Single().Path, Is.EqualTo("projectKey"));
    }

    [Test]
    public async Task Find_WithAProjectKey_QueriesThatProjectOnlyAndNotAcrossProjects()
    {
        // Arrange
        _h.WorkItems.Items["WEB-12"] = ItemSamples.Item("WEB-12", "改版首頁", "WEB") with { CreatedBy = _h.Actor.PersonId };

        // Act
        var result = await Finder.FindAsync(_h.Actor, null, new ItemFindQuery(10) { ProjectKey = "WEB" }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Items.Select(i => i.Key), Is.EqualTo(new[] { "WEB-12" }));
            Assert.That(_h.WorkItems.AcrossCalls, Is.Empty);
        }
    }

    [Test]
    public async Task Find_MoreThanTheLimit_ReturnsTheLimitAndHasMore()
    {
        // Arrange
        _h.WorkItems.OnListAcross = (_, _) => new WorkItemPage([Item("WEB-1"), Item("WEB-2"), Item("WEB-3")], null);

        // Act
        var result = await Finder.FindAsync(_h.Actor, null, new ItemFindQuery(2), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Items.Select(i => i.Key), Is.EqualTo(new[] { "WEB-1", "WEB-2" }));
            Assert.That(result.Value.HasMore, Is.True);
        }
    }

    [Test]
    public async Task Find_ExactlyTheLimit_HasNoMore()
    {
        // Arrange
        _h.WorkItems.OnListAcross = (_, _) => new WorkItemPage([Item("WEB-1"), Item("WEB-2")], null);

        // Act
        var result = await Finder.FindAsync(_h.Actor, null, new ItemFindQuery(2), CancellationToken.None);

        // Assert
        Assert.That(result.Value.HasMore, Is.False);
    }

    [Test]
    public async Task Find_Result_CarriesProjectNamesAndPeopleNames()
    {
        // Arrange
        var ivan = Guid.CreateVersion7();
        var afang = Guid.CreateVersion7();
        _h.People.Set(_h.OrganizationId, ivan, OrganizationRole.Member, name: "Ivan");
        _h.People.Set(_h.OrganizationId, afang, OrganizationRole.Member, name: "阿芳");
        _h.WorkItems.OnListAcross = (_, _) => new WorkItemPage(
            [Item("OPS-3", "OPS") with { AssigneeId = ivan, CreatedBy = afang, DueDate = new DateOnly(2026, 12, 2), Priority = Priority.High, StateCategory = StateCategory.InProgress }],
            null);

        // Act
        var result = await Finder.FindAsync(_h.Actor, null, new ItemFindQuery(10), CancellationToken.None);

        // Assert
        var item = result.Value.Items.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(item.ProjectKey, Is.EqualTo("OPS"));
            Assert.That(item.ProjectName, Is.EqualTo("維運"));
            Assert.That(item.StateCategory, Is.EqualTo("in_progress"));
            Assert.That(item.Priority, Is.EqualTo("high"));
            Assert.That(item.DueDate, Is.EqualTo(new DateOnly(2026, 12, 2)));
            Assert.That(item.Assignee, Is.EqualTo(new FoundPerson(ivan, "Ivan")));
            Assert.That(item.CreatedBy, Is.EqualTo(new FoundPerson(afang, "阿芳")));
        }
    }

    [Test]
    public async Task Find_UnassignedItem_HasNoAssignee()
    {
        // Arrange
        _h.WorkItems.OnListAcross = (_, _) => new WorkItemPage([Item("WEB-1")], null);

        // Act
        var result = await Finder.FindAsync(_h.Actor, null, new ItemFindQuery(10), CancellationToken.None);

        // Assert
        Assert.That(result.Value.Items.Single().Assignee, Is.Null);
    }

    [Test]
    public async Task Find_WorkEngineFailure_IsReturnedAsIs()
    {
        // Arrange
        _h.WorkItems.OnListAcross = (_, _) => WorkSamples.Failure(FailureKind.Invalid, WorkErrorCodes.QueryInvalid, "條件不對。");

        // Act
        var result = await Finder.FindAsync(_h.Actor, null, new ItemFindQuery(10), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(WorkErrorCodes.QueryInvalid));
    }

    private static WorkItem Item(string key, string project = "WEB") =>
        ItemSamples.Item(key, $"項目 {key}", project) with { CreatedBy = ItemSamples.MemberId };

    private static ProjectSummary Project(Guid id, string key, string name) => new(id, key, name, 1, "member", DateTimeOffset.UnixEpoch);
}
