using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using static PmTool.Work.Tests.History.HistorySupport;

namespace PmTool.Work.Tests.History;

/// <summary>歷史匯入的項目：保留建立的人與時間、直接設定狀態、上下層、重跑略過、全部或全部不、權限與封存。</summary>
[TestFixture]
public sealed class HistoryItemImportTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ImportItemsAsync_ValidRows_KeepsTheOriginalCreatorAndTimes()
    {
        // Arrange
        var (scenario, manager, member) = await ProjectWithMembersAsync(_host);
        var createdAt = new DateTimeOffset(2026, 3, 5, 8, 30, 15, 123, TimeSpan.FromHours(8)).AddTicks(7);

        // Act
        var result = await _host.History.ImportItemsAsync(manager, "WEB", Batch(Row("m-1", member.PersonId, "舊的任務", createdAt)), CancellationToken.None);
        var item = await _host.Items.GetAsync(manager, result.Value.Created[0].ItemKey, CancellationToken.None);
        var activities = await ActivityLog.AllAsync(_host, manager, item.Value.Key);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Created, Is.EqualTo(new[] { new HistoryItemRef("m-1", "WEB-1") }));
            Assert.That(item.Value.CreatedAt, Is.EqualTo(createdAt.ToUniversalTime().AddTicks(-(createdAt.UtcTicks % 10))));
            Assert.That(item.Value.UpdatedAt, Is.EqualTo(item.Value.CreatedAt), "updatedAt 也是原本的建立時間");
            Assert.That(activities, Has.Count.EqualTo(1));
            Assert.That((activities[0].Kind, activities[0].ActorId, activities[0].At), Is.EqualTo((ActivityKind.Created, member.PersonId, item.Value.CreatedAt)));
            Assert.That(activities[0].ImportedFrom, Is.EqualTo("monday"));
        }
    }

    [Test]
    public async Task ImportItemsAsync_RowWithAState_SetsItDirectlyWithoutATransitionActivity()
    {
        // Arrange
        var (scenario, manager, member) = await ProjectWithMembersAsync(_host);

        // Act
        var result = await _host.History.ImportItemsAsync(manager, "WEB", Batch(Row("m-1", member.PersonId, state: "done")), CancellationToken.None);
        var item = await _host.Items.GetAsync(manager, "WEB-1", CancellationToken.None);
        var activities = await ActivityLog.AllAsync(_host, manager, "WEB-1");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
            Assert.That((item.Value.State, item.Value.StateCategory), Is.EqualTo(("done", PmTool.Blueprint.Model.StateCategory.Done)));
            Assert.That(activities.Select(a => a.Kind), Is.EqualTo(new[] { ActivityKind.Created }));
        }
    }

    [Test]
    public async Task ImportItemsAsync_UnknownState_IsAProblemOfThatRow()
    {
        // Arrange
        var (_, manager, member) = await ProjectWithMembersAsync(_host);

        // Act
        var result = await _host.History.ImportItemsAsync(
            manager, "WEB", Batch(Row("m-1", member.PersonId), Row("m-2", member.PersonId, state: "nope")), CancellationToken.None);

        // Assert
        Assert.That(Paths(result.Failure), Is.EqualTo(new[] { "rows/1/state" }));
    }

    [Test]
    public async Task ImportItemsAsync_ParentInTheSameBatch_IsLinked()
    {
        // Arrange
        var (_, manager, member) = await ProjectWithMembersAsync(_host, template: Scenario.AgencyProject);
        var child = Row("m-2", member.PersonId, "子項目", parent: "m-1");

        // Act
        var result = await _host.History.ImportItemsAsync(
            manager, "WEB", Batch(Row("m-1", member.PersonId, "上層").OfType("requirement"), child), CancellationToken.None);
        var stored = await _host.Items.GetAsync(manager, "WEB-2", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
            Assert.That(stored.Value.ParentKey, Is.EqualTo("WEB-1"));
        }
    }

    [Test]
    public async Task ImportItemsAsync_ParentImportedByAnEarlierBatch_IsLinked()
    {
        // Arrange
        var (_, manager, member) = await ProjectWithMembersAsync(_host, template: Scenario.AgencyProject);
        await _host.History.ImportItemsAsync(manager, "WEB", Batch(Row("m-1", member.PersonId, "上層").OfType("requirement")), CancellationToken.None);
        var child = Row("m-2", member.PersonId, parent: "m-1");

        // Act
        var result = await _host.History.ImportItemsAsync(manager, "WEB", Batch(child), CancellationToken.None);
        var stored = await _host.Items.GetAsync(manager, "WEB-2", CancellationToken.None);

        // Assert
        Assert.That(stored.Value.ParentKey, Is.EqualTo("WEB-1"), () => result.Failure!.Message);
    }

    [Test]
    public async Task ImportItemsAsync_ParentListedAfterTheChild_IsAProblem()
    {
        // Arrange
        var (_, manager, member) = await ProjectWithMembersAsync(_host, template: Scenario.AgencyProject);
        var child = Row("m-1", member.PersonId, parent: "m-2");

        // Act
        var result = await _host.History.ImportItemsAsync(manager, "WEB", Batch(child, Row("m-2", member.PersonId).OfType("requirement")), CancellationToken.None);

        // Assert
        Assert.That(Paths(result.Failure), Is.EqualTo(new[] { "rows/0/parent_external_id" }));
    }

    [Test]
    public async Task ImportItemsAsync_UnknownParent_IsAProblem()
    {
        // Arrange
        var (_, manager, member) = await ProjectWithMembersAsync(_host, template: Scenario.AgencyProject);
        var child = Row("m-1", member.PersonId, parent: "nope");

        // Act
        var result = await _host.History.ImportItemsAsync(manager, "WEB", Batch(child), CancellationToken.None);

        // Assert
        Assert.That(Paths(result.Failure), Is.EqualTo(new[] { "rows/0/parent_external_id" }));
    }

    [Test]
    public async Task ImportItemsAsync_ParentKeyInTheItem_IsAProblem()
    {
        // Arrange
        var (_, manager, member) = await ProjectWithMembersAsync(_host, template: Scenario.AgencyProject);
        var row = Row("m-1", member.PersonId) with { Item = new NewWorkItem { Type = "task", Title = "子", ParentKey = "WEB-1" } };

        // Act
        var result = await _host.History.ImportItemsAsync(manager, "WEB", Batch(row), CancellationToken.None);

        // Assert
        Assert.That(Paths(result.Failure), Does.Contain("rows/0/parent"));
    }

    [Test]
    public async Task ImportItemsAsync_RunAgain_SkipsEverythingAndCreatesNothing()
    {
        // Arrange
        var (_, manager, member) = await ProjectWithMembersAsync(_host);
        var rows = Batch(Row("m-1", member.PersonId, "甲"), Row("m-2", member.PersonId, "乙"));
        await _host.History.ImportItemsAsync(manager, "WEB", rows, CancellationToken.None);

        // Act
        var again = await _host.History.ImportItemsAsync(manager, "WEB", rows, CancellationToken.None);
        var listed = await _host.Items.ListAsync(manager, "WEB", new WorkItemQuery(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(again.Value.Created, Is.Empty);
            Assert.That(again.Value.Skipped, Is.EqualTo(new[] { new HistoryItemRef("m-1", "WEB-1"), new HistoryItemRef("m-2", "WEB-2") }));
            Assert.That(listed.Value.Items, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task ImportItemsAsync_RunAgainWithNewRows_CreatesOnlyTheNewOnes()
    {
        // Arrange
        var (_, manager, member) = await ProjectWithMembersAsync(_host);
        await _host.History.ImportItemsAsync(manager, "WEB", Batch(Row("m-1", member.PersonId)), CancellationToken.None);

        // Act
        var again = await _host.History.ImportItemsAsync(manager, "WEB", Batch(Row("m-1", member.PersonId), Row("m-2", member.PersonId)), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(again.Value.Created, Is.EqualTo(new[] { new HistoryItemRef("m-2", "WEB-2") }));
            Assert.That(again.Value.Skipped, Is.EqualTo(new[] { new HistoryItemRef("m-1", "WEB-1") }));
        }
    }

    [Test]
    public async Task ImportItemsAsync_ExternalIdImportedIntoAnotherProject_IsSkippedWithThatProjectsKey()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateProjectAsync("OPS");
        var person = await scenario.MemberAsync("OPS", "editor");
        await scenario.MemberAsync("WEB", "editor");
        await _host.History.ImportItemsAsync(scenario.Admin, "OPS", Batch(Row("m-1", person.PersonId)), CancellationToken.None);

        // Act
        var result = await _host.History.ImportItemsAsync(scenario.Admin, "WEB", Batch(Row("m-1", person.PersonId)), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Skipped, Is.EqualTo(new[] { new HistoryItemRef("m-1", "OPS-1") }));
            Assert.That(result.Value.Created, Is.Empty);
        }
    }

    [Test]
    public async Task ImportItemsAsync_ParentImportedIntoAnotherProject_IsAProblem()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        await scenario.CreateProjectAsync("OPS", Scenario.AgencyProject);
        var person = await scenario.MemberAsync("OPS", "editor");
        await _host.Projects.SetMemberAsync(scenario.Admin, "WEB", person.PersonId, "editor", CancellationToken.None);
        await _host.History.ImportItemsAsync(scenario.Admin, "OPS", Batch(Row("m-1", person.PersonId).OfType("requirement")), CancellationToken.None);
        var child = Row("m-2", person.PersonId, parent: "m-1");

        // Act
        var result = await _host.History.ImportItemsAsync(scenario.Admin, "WEB", Batch(child), CancellationToken.None);

        // Assert
        Assert.That(Paths(result.Failure), Is.EqualTo(new[] { "rows/0/parent_external_id" }));
    }

    [Test]
    public async Task ImportItemsAsync_CreatorOrFollowerWhoIsNotAMember_IsAProblemOfThatRow()
    {
        // Arrange
        var (scenario, manager, member) = await ProjectWithMembersAsync(_host);
        var outsider = scenario.Person();
        var rows = Batch(
            Row("m-1", member.PersonId),
            Row("m-2", outsider.PersonId),
            Row("m-3", member.PersonId) with { Followers = [member.PersonId, outsider.PersonId] });

        // Act
        var result = await _host.History.ImportItemsAsync(manager, "WEB", rows, CancellationToken.None);
        var listed = await _host.Items.ListAsync(manager, "WEB", new WorkItemQuery(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((result.Failure?.Kind, result.Failure?.Code), Is.EqualTo((FailureKind.Invalid, WorkErrorCodes.ImportInvalid)).Or.EqualTo((FailureKind.Invalid, WorkErrorCodes.ItemUnknownAssignee)));
            Assert.That(Paths(result.Failure), Is.EqualTo(new[] { "rows/1/created_by", "rows/2/followers/1" }));
            Assert.That(listed.Value.Items, Is.Empty, "全部或全部不");
        }
    }

    [Test]
    public async Task ImportItemsAsync_AssigneeWhoIsNotAMember_IsAProblemOfThatRow()
    {
        // Arrange
        var (scenario, manager, member) = await ProjectWithMembersAsync(_host);
        var row = Row("m-1", member.PersonId) with { Item = new NewWorkItem { Type = "task", Title = "甲", AssigneeId = scenario.Person().PersonId } };

        // Act
        var result = await _host.History.ImportItemsAsync(manager, "WEB", Batch(row), CancellationToken.None);

        // Assert
        Assert.That(Paths(result.Failure), Is.EqualTo(new[] { "rows/0/assignee" }));
    }

    [Test]
    public async Task ImportItemsAsync_OneBadRow_CreatesNothingAndDoesNotConsumeNumbers()
    {
        // Arrange
        var (scenario, manager, member) = await ProjectWithMembersAsync(_host);

        // Act
        var failed = await _host.History.ImportItemsAsync(manager, "WEB", Batch(Row("m-1", member.PersonId), Row("m-2", member.PersonId, title: "")), CancellationToken.None);
        var next = await scenario.CreateTaskAsync("WEB");
        var existing = await _host.History.LookupAsync(manager, new HistoryLookup { Source = Source, Kind = HistoryRefKind.Item, ExternalIds = ["m-1"] }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Paths(failed.Failure), Is.EqualTo(new[] { "rows/1/title" }));
            Assert.That(next.Key, Is.EqualTo("WEB-1"));
            Assert.That(existing.Value, Is.Empty);
        }
    }

    [Test]
    public async Task ImportItemsAsync_DryRun_ChecksButCreatesNothing()
    {
        // Arrange
        var (_, manager, member) = await ProjectWithMembersAsync(_host);
        await _host.History.ImportItemsAsync(manager, "WEB", Batch(Row("m-1", member.PersonId)), CancellationToken.None);

        // Act
        var dry = await _host.History.ImportItemsAsync(manager, "WEB", Batch(true, Row("m-1", member.PersonId), Row("m-2", member.PersonId)), CancellationToken.None);
        var listed = await _host.Items.ListAsync(manager, "WEB", new WorkItemQuery(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dry.Value.DryRun, Is.True);
            Assert.That(dry.Value.Created, Is.Empty);
            Assert.That(dry.Value.Skipped, Is.EqualTo(new[] { new HistoryItemRef("m-1", "WEB-1") }));
            Assert.That(listed.Value.Items, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task ImportItemsAsync_DryRunWithABadRow_StillReportsTheProblem()
    {
        // Arrange
        var (_, manager, member) = await ProjectWithMembersAsync(_host);

        // Act
        var dry = await _host.History.ImportItemsAsync(manager, "WEB", Batch(true, Row("m-1", member.PersonId, title: "")), CancellationToken.None);

        // Assert
        Assert.That(Paths(dry.Failure), Is.EqualTo(new[] { "rows/0/title" }));
    }

    [Test]
    public async Task ImportItemsAsync_DuplicateExternalIdInTheBatch_IsAProblemOfTheSecondRow()
    {
        // Arrange
        var (_, manager, member) = await ProjectWithMembersAsync(_host);

        // Act
        var result = await _host.History.ImportItemsAsync(manager, "WEB", Batch(Row("m-1", member.PersonId), Row("m-1", member.PersonId)), CancellationToken.None);

        // Assert
        Assert.That(Paths(result.Failure), Is.EqualTo(new[] { "rows/1/external_id" }));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("有 空白")]
    public async Task ImportItemsAsync_BadExternalId_IsAProblemOfThatRow(string? externalId)
    {
        // Arrange
        var (_, manager, member) = await ProjectWithMembersAsync(_host);

        // Act
        var result = await _host.History.ImportItemsAsync(manager, "WEB", Batch(Row(externalId!, member.PersonId)), CancellationToken.None);

        // Assert
        Assert.That(Paths(result.Failure), Is.EqualTo(new[] { "rows/0/external_id" }));
    }

    [Test]
    public async Task ImportItemsAsync_CreatedAtInTheFutureOrBeforeTheYear2000_IsAProblem()
    {
        // Arrange
        var (_, manager, member) = await ProjectWithMembersAsync(_host);
        var rows = Batch(
            Row("m-1", member.PersonId, createdAt: WorkTestHost.StartTime.AddMinutes(4)),
            Row("m-2", member.PersonId, createdAt: WorkTestHost.StartTime.AddMinutes(6)),
            Row("m-3", member.PersonId, createdAt: new DateTimeOffset(1999, 12, 31, 0, 0, 0, TimeSpan.Zero)));

        // Act
        var result = await _host.History.ImportItemsAsync(manager, "WEB", rows, CancellationToken.None);

        // Assert
        Assert.That(Paths(result.Failure), Is.EqualTo(new[] { "rows/1/created_at", "rows/2/created_at" }));
    }

    [TestCase("")]
    [TestCase("Monday")]
    [TestCase("x")]
    public async Task ImportItemsAsync_BadSource_IsHistoryInvalid(string source)
    {
        // Arrange
        var (_, manager, member) = await ProjectWithMembersAsync(_host);
        var import = new HistoryItems { Source = source, Rows = [Row("m-1", member.PersonId)] };

        // Act
        var result = await _host.History.ImportItemsAsync(manager, "WEB", import, CancellationToken.None);

        // Assert
        Assert.That((result.Failure?.Code, Paths(result.Failure)), Is.EqualTo((WorkErrorCodes.HistoryInvalid, new[] { "source" })));
    }

    [Test]
    public async Task ImportItemsAsync_NoRowsOrTooMany_IsHistoryInvalid()
    {
        // Arrange
        var (_, manager, member) = await ProjectWithMembersAsync(_host);
        var tooMany = new HistoryItems { Source = Source, Rows = [.. Enumerable.Repeat(Row("m", member.PersonId), HistoryItems.MaxRows + 1)] };

        // Act
        var empty = await _host.History.ImportItemsAsync(manager, "WEB", Batch(), CancellationToken.None);
        var large = await _host.History.ImportItemsAsync(manager, "WEB", tooMany, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((empty.Failure?.Code, Paths(empty.Failure)), Is.EqualTo((WorkErrorCodes.HistoryInvalid, new[] { "rows" })));
            Assert.That((large.Failure?.Code, Paths(large.Failure)), Is.EqualTo((WorkErrorCodes.HistoryInvalid, new[] { "rows" })));
        }
    }

    [Test]
    public async Task ImportItemsAsync_WithoutProjectManage_IsForbidden()
    {
        // Arrange
        var (_, _, member) = await ProjectWithMembersAsync(_host);

        // Act
        var result = await _host.History.ImportItemsAsync(member, "WEB", Batch(Row("m-1", member.PersonId)), CancellationToken.None);

        // Assert
        Assert.That((result.Failure?.Kind, result.Failure?.Code), Is.EqualTo((FailureKind.Forbidden, WorkErrorCodes.Forbidden)));
    }

    [Test]
    public async Task ImportItemsAsync_NotAMemberOfTheProject_IsProjectNotFound()
    {
        // Arrange
        var (scenario, _, member) = await ProjectWithMembersAsync(_host);

        // Act
        var result = await _host.History.ImportItemsAsync(scenario.Person(), "WEB", Batch(Row("m-1", member.PersonId)), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectNotFound));
    }

    [Test]
    public async Task ImportItemsAsync_ArchivedProject_IsRefused()
    {
        // Arrange
        var (scenario, manager, member) = await ProjectWithMembersAsync(_host);
        await _host.Projects.ArchiveAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Act
        var result = await _host.History.ImportItemsAsync(manager, "WEB", Batch(Row("m-1", member.PersonId)), CancellationToken.None);

        // Assert
        Assert.That((result.Failure?.Kind, result.Failure?.Code), Is.EqualTo((FailureKind.Conflict, WorkErrorCodes.ProjectArchived)));
    }

    [Test]
    public async Task ImportItemsAsync_WritesAHistoryEventThatCarriesTheFollowersAndOriginalActor()
    {
        // Arrange
        var (scenario, manager, member) = await ProjectWithMembersAsync(_host);
        var follower = await scenario.MemberAsync("WEB", "viewer");
        var row = Row("m-1", member.PersonId) with
        {
            Item = new NewWorkItem { Type = "task", Title = "有人關注", AssigneeId = manager.PersonId },
            Followers = [follower.PersonId, manager.PersonId],
        };

        // Act
        await _host.History.ImportItemsAsync(manager, "WEB", Batch(row), CancellationToken.None);
        var created = (await EventFeed.OfAsync(_host, scenario)).Single(e => e.Kind == WorkEventKind.ItemCreated && e.Item.ItemKey == "WEB-1");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((created.Item.History, created.Item.Imported), Is.EqualTo((true, true)));
            Assert.That(created.Item.Followers, Is.EqualTo(new[] { follower.PersonId }), "負責人不用列在關注的人裡");
            Assert.That((created.ActorPersonId, created.OccurredAt), Is.EqualTo((member.PersonId, Past())));
            Assert.That(created.Item.MentionedPersonIds, Is.Empty);
        }
    }

    [Test]
    public async Task ImportItemsAsync_ActivityListReportsImportedFromOnlyForImportedActivities()
    {
        // Arrange
        var (scenario, manager, member) = await ProjectWithMembersAsync(_host);
        await _host.History.ImportItemsAsync(manager, "WEB", Batch(Row("m-1", member.PersonId)), CancellationToken.None);
        var native = await scenario.CreateTaskAsync("WEB", "這裡建立的");

        // Act
        var imported = await ActivityLog.AllAsync(_host, manager, "WEB-1");
        var created = await ActivityLog.AllAsync(_host, manager, native.Key);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(imported.Select(a => a.ImportedFrom), Is.EqualTo(new[] { "monday" }));
            Assert.That(created.Select(a => a.ImportedFrom), Is.EqualTo(new string?[] { null }));
        }
    }

    [Test]
    public async Task ImportItemsAsync_SecondBatchWaitsForTheFirstWhenBothImportTheSameRow()
    {
        // Arrange
        var (_, manager, member) = await ProjectWithMembersAsync(_host);
        var rows = Batch(Row("m-1", member.PersonId));

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            var request = _host.NewRequest();
            await using (request.Scope)
            {
                var service = request.Scope.ServiceProvider.GetRequiredService<IHistoryImportService>();
                return await service.ImportItemsAsync(manager, "WEB", rows, CancellationToken.None);
            }
        }));
        var listed = await _host.Items.ListAsync(manager, "WEB", new WorkItemQuery(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.All(r => r.Succeeded), Is.True, () => string.Join("; ", results.Select(r => r.Failure?.Message)));
            Assert.That(results.Sum(r => r.Value.Created.Count), Is.EqualTo(1));
            Assert.That(listed.Value.Items, Has.Count.EqualTo(1));
        }
    }
}
