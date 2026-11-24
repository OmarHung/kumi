using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using static PmTool.Work.Tests.Planning.PlanningSupport;

namespace PmTool.Work.Tests.Planning;

/// <summary>里程碑：新增、列出、修改、刪除，以及名稱、說明與上限的規則。</summary>
[TestFixture]
public sealed class MilestoneRulesTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task CreateMilestoneAsync_StoresItAndListsItWithEmptyProgress()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var created = await _host.Planning.CreateMilestoneAsync(
            scenario.Admin, "WEB", MilestoneOf(" v1.0 上線 ", 28, "第一個\n對外的版本。"), CancellationToken.None);
        var listed = await _host.Planning.ListMilestonesAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        Assert.That(created.Succeeded, Is.True, () => created.Failure!.Message);
        var expected = new Milestone(created.Value.Id, "v1.0 上線", Day1.AddDays(28), "第一個\n對外的版本。", new ItemProgress(0, 0, 0, null, null));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Value, Is.EqualTo(expected));
            Assert.That(listed.Value, Is.EqualTo(new[] { expected }));
        }
    }

    [Test]
    public async Task CreateMilestoneAsync_BlankDescription_IsStoredAsNull()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var created = await _host.Planning.CreateMilestoneAsync(scenario.Admin, "WEB", MilestoneOf("里程碑", 1, "  \n "), CancellationToken.None);

        // Assert
        Assert.That(created.Value.Description, Is.Null);
    }

    [Test]
    public async Task ListMilestonesAsync_SortsByDueDateThenName()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await CreateMilestoneAsync(_host, scenario.Admin, "WEB", MilestoneOf("乙", 10));
        await CreateMilestoneAsync(_host, scenario.Admin, "WEB", MilestoneOf("甲", 10));
        await CreateMilestoneAsync(_host, scenario.Admin, "WEB", MilestoneOf("丙", 3));

        // Act
        var listed = await _host.Planning.ListMilestonesAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        Assert.That(listed.Value.Select(m => m.Name), Is.EqualTo(new[] { "丙", "乙", "甲" }));
    }

    [Test]
    public async Task UpdateMilestoneAsync_ChangesEverythingAndCanClearTheDescription()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var milestone = await CreateMilestoneAsync(_host, scenario.Admin, "WEB", MilestoneOf("v1", 10, "說明"));

        // Act
        var updated = await _host.Planning.UpdateMilestoneAsync(scenario.Admin, "WEB", milestone.Id, MilestoneOf("v1.1", 12, null), CancellationToken.None);

        // Assert
        Assert.That(updated.Value, Is.EqualTo(milestone with { Name = "v1.1", DueDate = Day1.AddDays(12), Description = null }));
    }

    [Test]
    public async Task UpdateAndDeleteMilestoneAsync_UnknownOrOtherProjectsMilestone_AreNotFound()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateProjectAsync("OPS");
        var other = await CreateMilestoneAsync(_host, scenario.Admin, "OPS", MilestoneOf("v1", 10));

        // Act
        var updated = await _host.Planning.UpdateMilestoneAsync(scenario.Admin, "WEB", other.Id, MilestoneOf("改", 1), CancellationToken.None);
        var deleted = await _host.Planning.DeleteMilestoneAsync(scenario.Admin, "WEB", other.Id, CancellationToken.None);

        // Assert
        AssertFailure(updated, FailureKind.NotFound, WorkErrorCodes.MilestoneNotFound);
        AssertFailure(deleted, FailureKind.NotFound, WorkErrorCodes.MilestoneNotFound);
    }

    [Test]
    public async Task DeleteMilestoneAsync_KeepsTheItemsButTheyHaveNoMilestone()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var milestone = await CreateMilestoneAsync(_host, scenario.Admin, "WEB", MilestoneOf("v1", 10));
        var item = await ItemInAsync(_host, scenario, "WEB", milestoneId: milestone.Id);

        // Act
        var deleted = await _host.Planning.DeleteMilestoneAsync(scenario.Admin, "WEB", milestone.Id, CancellationToken.None);
        var read = await _host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        Assert.That(deleted.Succeeded, Is.True);
        Assert.That(read.Value.MilestoneId, Is.Null);
    }

    [Test]
    public async Task CreateMilestoneAsync_SameNameIgnoringCase_IsNameTakenButAnotherProjectIsFine()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateProjectAsync("OPS");
        await CreateMilestoneAsync(_host, scenario.Admin, "WEB", MilestoneOf("Launch", 10));

        // Act
        var same = await _host.Planning.CreateMilestoneAsync(scenario.Admin, "WEB", MilestoneOf(" LAUNCH", 20), CancellationToken.None);
        var elsewhere = await _host.Planning.CreateMilestoneAsync(scenario.Admin, "OPS", MilestoneOf("Launch", 10), CancellationToken.None);
        var cycleWithSameName = await _host.Planning.CreateCycleAsync(scenario.Admin, "WEB", CycleOf("Launch", 0, 5), CancellationToken.None);

        // Assert
        AssertFailure(same, FailureKind.Conflict, WorkErrorCodes.MilestoneNameTaken);
        Assert.That(elsewhere.Succeeded, Is.True);
        Assert.That(cycleWithSameName.Succeeded, Is.True, "週期與里程碑各自算");
    }

    [Test]
    public async Task CreateMilestoneAsync_SameNameSentConcurrently_OnlyOneSucceeds()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => CreateInOwnRequestAsync(scenario, MilestoneOf("同名", 5))));

        // Assert
        Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(1));
        Assert.That(results.Where(r => !r.Succeeded).Select(r => r.Failure!.Code), Has.All.EqualTo(WorkErrorCodes.MilestoneNameTaken));
    }

    [Test]
    public async Task CreateMilestoneAsync_PastTheLimit_IsLimitReached()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        for (var i = 0; i < PlanningRules.MaxPerProject; i++)
        {
            await CreateMilestoneAsync(_host, scenario.Admin, "WEB", MilestoneOf($"里程碑 {i}", i));
        }

        // Act
        var result = await _host.Planning.CreateMilestoneAsync(scenario.Admin, "WEB", MilestoneOf("多一個", 1), CancellationToken.None);

        // Assert
        AssertFailure(result, FailureKind.Conflict, WorkErrorCodes.MilestoneLimitReached);
    }

    [Test]
    public async Task CreateMilestoneAsync_InvalidFields_ReportsEveryPath()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var draft = new MilestoneDraft("a​b", Day1, "壞\0字元");

        // Act
        var result = await _host.Planning.CreateMilestoneAsync(scenario.Admin, "WEB", draft, CancellationToken.None);

        // Assert
        AssertFailure(result, FailureKind.Invalid, WorkErrorCodes.PlanningInvalid);
        Assert.That(result.Failure!.Details.Select(d => d.Path), Is.EquivalentTo(new[] { "name", "description" }));
    }

    [TestCase("", "name", TestName = "CreateMilestoneAsync_EmptyName_IsInvalid")]
    [TestCase("a\tb", "name", TestName = "CreateMilestoneAsync_TabInName_IsInvalid")]
    public async Task CreateMilestoneAsync_BadName_IsInvalid(string name, string path)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Planning.CreateMilestoneAsync(scenario.Admin, "WEB", MilestoneOf(name, 1), CancellationToken.None);

        // Assert
        Assert.That(result.Failure!.Details.Select(d => d.Path), Is.EqualTo(new[] { path }));
    }

    [Test]
    public async Task CreateMilestoneAsync_LengthLimits_AreInclusive()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var atLimit = await _host.Planning.CreateMilestoneAsync(
            scenario.Admin,
            "WEB",
            MilestoneOf(new string('名', PlanningRules.MaxMilestoneNameLength), 1, new string('說', PlanningRules.MaxDescriptionLength)),
            CancellationToken.None);
        var nameOver = await _host.Planning.CreateMilestoneAsync(
            scenario.Admin, "WEB", MilestoneOf(new string('名', PlanningRules.MaxMilestoneNameLength + 1), 1), CancellationToken.None);
        var descriptionOver = await _host.Planning.CreateMilestoneAsync(
            scenario.Admin, "WEB", MilestoneOf("另一個", 1, new string('說', PlanningRules.MaxDescriptionLength + 1)), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(atLimit.Succeeded, Is.True, () => atLimit.Failure!.Message);
            Assert.That(nameOver.Failure!.Details.Single().Path, Is.EqualTo("name"));
            Assert.That(descriptionOver.Failure!.Details.Single().Path, Is.EqualTo("description"));
        }
    }

    [Test]
    public async Task Permissions_ReadNeedsItemReadAndWriteNeedsItemUpdate()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var viewer = await scenario.MemberAsync("WEB", "viewer");
        var stranger = scenario.Person();
        var milestone = await CreateMilestoneAsync(_host, scenario.Admin, "WEB", MilestoneOf("v1", 10));

        // Act
        var list = await _host.Planning.ListMilestonesAsync(viewer, "WEB", CancellationToken.None);
        var create = await _host.Planning.CreateMilestoneAsync(viewer, "WEB", MilestoneOf("v2", 20), CancellationToken.None);
        var update = await _host.Planning.UpdateMilestoneAsync(viewer, "WEB", milestone.Id, MilestoneOf("改", 20), CancellationToken.None);
        var delete = await _host.Planning.DeleteMilestoneAsync(viewer, "WEB", milestone.Id, CancellationToken.None);
        var strangerList = await _host.Planning.ListMilestonesAsync(stranger, "WEB", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(list.Succeeded, Is.True);
            AssertFailure(create, FailureKind.Forbidden, WorkErrorCodes.Forbidden);
            AssertFailure(update, FailureKind.Forbidden, WorkErrorCodes.Forbidden);
            AssertFailure(delete, FailureKind.Forbidden, WorkErrorCodes.Forbidden);
            AssertFailure(strangerList, FailureKind.NotFound, WorkErrorCodes.ProjectNotFound);
        }
    }

    private async Task<Result<Milestone>> CreateInOwnRequestAsync(Scenario scenario, MilestoneDraft draft)
    {
        var (_, _, scope) = _host.NewRequest();
        await using (scope)
        {
            return await scope.ServiceProvider
                .GetRequiredService<IPlanningService>()
                .CreateMilestoneAsync(scenario.Admin, "WEB", draft, CancellationToken.None);
        }
    }
}
