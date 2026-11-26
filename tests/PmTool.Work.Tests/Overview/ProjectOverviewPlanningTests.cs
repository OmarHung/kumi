using PmTool.Blueprint.Model;
using static PmTool.Work.Tests.Overview.OverviewSupport;
using static PmTool.Work.Tests.Planning.PlanningSupport;

namespace PmTool.Work.Tests.Overview;

/// <summary>專案總覽的狀態橫條、這期週期與下一個里程碑。</summary>
[TestFixture]
public sealed class ProjectOverviewPlanningTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ListAsync_States_FollowTheBlueprintOrderAndSkipEmptyOnes()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var finished = await TaskAsync(scenario, "WEB", "完成");
        var going = await TaskAsync(scenario, "WEB", "進行中");
        await TaskAsync(scenario, "WEB", "待辦一");
        await TaskAsync(scenario, "WEB", "待辦二");
        await MoveAsync(scenario, finished.Key, "done");
        await MoveAsync(scenario, going.Key, "doing");

        // Act
        var overview = await OneAsync(_host, scenario.Admin, "WEB");

        // Assert
        Assert.That(
            overview.States,
            Is.EqualTo(new[]
            {
                new OverviewStateCount("todo", "待辦", StateCategory.NotStarted, 2),
                new OverviewStateCount("doing", "進行中", StateCategory.InProgress, 1),
                new OverviewStateCount("done", "完成", StateCategory.Done, 1),
            }));
    }

    [Test]
    public async Task ListAsync_SameStateKeyInSeveralWorkflows_IsMergedUnderTheFirstWorkflowsName()
    {
        // Arrange：第二個流程也有 todo，另外有自己的 review。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var proposed = await ChangeSetDrafts.ProposeAsync(scenario, "WEB", blueprint => blueprint with
        {
            Workflows =
            [
                .. blueprint.Workflows,
                new Workflow
                {
                    Key = "alt_flow",
                    Name = "另一個流程",
                    States =
                    [
                        new WorkflowState { Key = "todo", Name = "要做", Category = StateCategory.NotStarted },
                        new WorkflowState { Key = "review", Name = "審查", Category = StateCategory.InProgress },
                        new WorkflowState { Key = "shipped", Name = "出貨", Category = StateCategory.Done },
                    ],
                },
            ],
            ItemTypes = [.. blueprint.ItemTypes, new ItemType { Key = "alt", Name = "另一種", Workflow = "alt_flow" }],
        });
        await ChangeSetDrafts.ApplyAsync(scenario, "WEB", proposed.ChangeSet.Number);
        await TaskAsync(scenario, "WEB", "一般任務");
        var alt = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "alt", Title = "另一種項目" });
        var reviewing = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "alt", Title = "審查中" });
        await MoveAsync(scenario, reviewing.Key, "review");
        Assert.That(alt.State, Is.EqualTo("todo"));

        // Act
        var overview = await OneAsync(_host, scenario.Admin, "WEB");

        // Assert
        Assert.That(
            overview.States,
            Is.EqualTo(new[]
            {
                new OverviewStateCount("todo", "待辦", StateCategory.NotStarted, 2),
                new OverviewStateCount("review", "審查", StateCategory.InProgress, 1),
            }));
    }

    [Test]
    public async Task ListAsync_CurrentCycle_IsTheOneTodayFallsInIncludingBothEndsWithItsProgress()
    {
        // Arrange
        SetNow(_host, Noon);
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await CreateCycleAsync(_host, scenario.Admin, "WEB", new CycleDraft("上一期", Today.AddDays(-14), Today.AddDays(-7)));
        var current = await CreateCycleAsync(_host, scenario.Admin, "WEB", new CycleDraft("這一期", Today.AddDays(-6), Today));
        await CreateCycleAsync(_host, scenario.Admin, "WEB", new CycleDraft("下一期", Today.AddDays(1), Today.AddDays(7)));
        var finished = await ItemInAsync(_host, scenario, "WEB", cycleId: current.Id, estimate: 3);
        await ItemInAsync(_host, scenario, "WEB", cycleId: current.Id, estimate: 5);
        await MoveAsync(scenario, finished.Key, "done");

        // Act
        var onLastDay = await OneAsync(_host, scenario.Admin, "WEB");
        SetNow(_host, Noon.AddDays(1));
        var afterwards = await OneAsync(_host, scenario.Admin, "WEB");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(onLastDay.CurrentCycle, Is.EqualTo(new OverviewCycle(
                current.Id, "這一期", Today.AddDays(-6), Today, new ItemProgress(2, 1, 0, 8, 3))));
            Assert.That(afterwards.CurrentCycle?.Name, Is.EqualTo("下一期"), "隔天換成下一期");
        }
    }

    [Test]
    public async Task ListAsync_CurrentCycle_WhenTodayIsInNoneOfThemIsNull()
    {
        // Arrange
        SetNow(_host, Noon);
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await CreateCycleAsync(_host, scenario.Admin, "WEB", new CycleDraft("過去", Today.AddDays(-14), Today.AddDays(-1)));
        await CreateCycleAsync(_host, scenario.Admin, "WEB", new CycleDraft("未來", Today.AddDays(1), Today.AddDays(14)));

        // Act
        var overview = await OneAsync(_host, scenario.Admin, "WEB");

        // Assert
        Assert.That(overview.CurrentCycle, Is.Null);
    }

    [Test]
    public async Task ListAsync_CycleAndMilestoneProgress_OnlyCountItemsTheCallerCanSee()
    {
        // Arrange
        SetNow(_host, Noon);
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var contributor = await scenario.MemberAsync("WEB", "contributor");
        var editor = await scenario.MemberAsync("WEB", "editor");
        var cycle = await CreateCycleAsync(_host, scenario.Admin, "WEB", new CycleDraft("這一期", Today.AddDays(-3), Today.AddDays(3)));
        var milestone = await CreateMilestoneAsync(_host, scenario.Admin, "WEB", DueIn("上線", 10));
        var mine = await scenario.CreateItemAsync(
            "WEB",
            new NewWorkItem { Type = "task", Title = "我的", AssigneeId = contributor.PersonId, CycleId = cycle.Id, MilestoneId = milestone.Id },
            editor);
        await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "別人的", CycleId = cycle.Id, MilestoneId = milestone.Id }, editor);
        await MoveAsync(scenario, mine.Key, "done", editor);

        // Act
        var own = await OneAsync(_host, contributor, "WEB");
        var all = await OneAsync(_host, editor, "WEB");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(own.CurrentCycle?.Progress, Is.EqualTo(new ItemProgress(1, 1, 0, null, null)));
            Assert.That(all.CurrentCycle?.Progress, Is.EqualTo(new ItemProgress(2, 1, 0, null, null)));
            Assert.That(own.NextMilestone, Is.Null, "看得到的項目都結束了，沒有要等的里程碑");
            Assert.That(all.NextMilestone?.Progress, Is.EqualTo(new ItemProgress(2, 1, 0, null, null)));
        }
    }

    [Test]
    public async Task ListAsync_NextMilestone_IsTheEarliestOneDueTodayOrLaterThatStillHasOpenItems()
    {
        // Arrange
        SetNow(_host, Noon);
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var passed = await CreateMilestoneAsync(_host, scenario.Admin, "WEB", DueIn("過期的", -3));
        var allDone = await CreateMilestoneAsync(_host, scenario.Admin, "WEB", DueIn("都做完了", 0));
        await CreateMilestoneAsync(_host, scenario.Admin, "WEB", DueIn("沒有項目", 1));
        var wanted = await CreateMilestoneAsync(_host, scenario.Admin, "WEB", DueIn("要等的", 5));
        var later = await CreateMilestoneAsync(_host, scenario.Admin, "WEB", DueIn("更晚的", 9));
        await ItemInAsync(_host, scenario, "WEB", milestoneId: passed.Id);
        var finished = await ItemInAsync(_host, scenario, "WEB", milestoneId: allDone.Id);
        await MoveAsync(scenario, finished.Key, "done");
        await ItemInAsync(_host, scenario, "WEB", milestoneId: wanted.Id);
        await ItemInAsync(_host, scenario, "WEB", milestoneId: later.Id);

        // Act
        var overview = await OneAsync(_host, scenario.Admin, "WEB");

        // Assert
        Assert.That(
            overview.NextMilestone,
            Is.EqualTo(new OverviewMilestone(wanted.Id, "要等的", Today.AddDays(5), new ItemProgress(1, 0, 0, null, null))));
    }

    [Test]
    public async Task ListAsync_MilestoneDueToday_StillCountsAsNext()
    {
        // Arrange
        SetNow(_host, Noon);
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var milestone = await CreateMilestoneAsync(_host, scenario.Admin, "WEB", DueIn("今天到期", 0));
        await ItemInAsync(_host, scenario, "WEB", milestoneId: milestone.Id);

        // Act
        var overview = await OneAsync(_host, scenario.Admin, "WEB");

        // Assert
        Assert.That(overview.NextMilestone?.Name, Is.EqualTo("今天到期"));
    }

    private static MilestoneDraft DueIn(string name, int days) => new(name, Today.AddDays(days), null);
}
