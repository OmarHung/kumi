using PmTool.Abstractions;
using PmTool.Work.Tests.OwnItems;
using static PmTool.Work.Tests.Planning.PlanningSupport;

namespace PmTool.Work.Tests.Planning;

/// <summary>週期與里程碑是整個專案的事：新增、修改、刪除都要看得到全部項目，只看自己的或完全沒有讀取權限的角色不行。</summary>
[TestFixture]
public sealed class PlanningScopeTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task DeleteCycleAndMilestone_ContributorSeeingOnlyOwnItems_AreRefusedAndTheyRemain()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var contributor = await scenario.MemberAsync("WEB", "contributor");
        var cycle = await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("衝刺 1", 0, 13));
        var milestone = await CreateMilestoneAsync(_host, scenario.Admin, "WEB", MilestoneOf("上線", 20));

        // Act
        var deleteCycle = await _host.Planning.DeleteCycleAsync(contributor, "WEB", cycle.Id, CancellationToken.None);
        var deleteMilestone = await _host.Planning.DeleteMilestoneAsync(contributor, "WEB", milestone.Id, CancellationToken.None);
        var cycles = await _host.Planning.ListCyclesAsync(scenario.Admin, "WEB", CancellationToken.None);
        var milestones = await _host.Planning.ListMilestonesAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleteCycle.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(deleteMilestone.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(cycles.Value.Select(c => c.Id), Is.EqualTo(new[] { cycle.Id }));
            Assert.That(milestones.Value.Select(m => m.Id), Is.EqualTo(new[] { milestone.Id }));
        }
    }

    [Test]
    public async Task WriteCycleAndMilestone_CustomRoleWithUpdateButNoReadPermission_AreRefused()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await OwnItemsWorld.PublishAsync(_host, scenario, "WEB", blueprint => blueprint with
        {
            Roles = [OwnItemsWorld.Role("update_only", WorkPermissions.ItemUpdate)],
        });
        var actor = await scenario.MemberAsync("WEB", "update_only");
        var cycle = await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("衝刺 1", 0, 13));
        var milestone = await CreateMilestoneAsync(_host, scenario.Admin, "WEB", MilestoneOf("上線", 20));

        // Act
        var results = new[]
        {
            (await _host.Planning.CreateCycleAsync(actor, "WEB", CycleOf("衝刺 2", 14, 27), CancellationToken.None)).Failure,
            (await _host.Planning.UpdateCycleAsync(actor, "WEB", cycle.Id, CycleOf("改名", 0, 13), CancellationToken.None)).Failure,
            (await _host.Planning.DeleteCycleAsync(actor, "WEB", cycle.Id, CancellationToken.None)).Failure,
            (await _host.Planning.CreateMilestoneAsync(actor, "WEB", MilestoneOf("新", 30), CancellationToken.None)).Failure,
            (await _host.Planning.UpdateMilestoneAsync(actor, "WEB", milestone.Id, MilestoneOf("改名", 20), CancellationToken.None)).Failure,
            (await _host.Planning.DeleteMilestoneAsync(actor, "WEB", milestone.Id, CancellationToken.None)).Failure,
        };

        // Assert
        Assert.That(results.Select(f => f?.Kind), Is.All.EqualTo(FailureKind.Forbidden));
    }

    [Test]
    public async Task WriteCycleAndMilestone_EditorSeeingAllItems_CreatesUpdatesAndDeletes()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var editor = await scenario.MemberAsync("WEB", "editor");

        // Act
        var cycle = await _host.Planning.CreateCycleAsync(editor, "WEB", CycleOf("衝刺 1", 0, 13), CancellationToken.None);
        var renamed = await _host.Planning.UpdateCycleAsync(editor, "WEB", cycle.Value.Id, CycleOf("衝刺 一", 0, 13), CancellationToken.None);
        var deletedCycle = await _host.Planning.DeleteCycleAsync(editor, "WEB", cycle.Value.Id, CancellationToken.None);
        var milestone = await _host.Planning.CreateMilestoneAsync(editor, "WEB", MilestoneOf("上線", 20), CancellationToken.None);
        var changed = await _host.Planning.UpdateMilestoneAsync(editor, "WEB", milestone.Value.Id, MilestoneOf("正式上線", 20), CancellationToken.None);
        var deletedMilestone = await _host.Planning.DeleteMilestoneAsync(editor, "WEB", milestone.Value.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cycle.Succeeded, Is.True);
            Assert.That(renamed.Value.Name, Is.EqualTo("衝刺 一"));
            Assert.That(deletedCycle.Succeeded, Is.True);
            Assert.That(milestone.Succeeded, Is.True);
            Assert.That(changed.Value.Name, Is.EqualTo("正式上線"));
            Assert.That(deletedMilestone.Succeeded, Is.True);
        }
    }
}
