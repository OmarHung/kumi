using Microsoft.Extensions.DependencyInjection;
using PmTool.Blueprint.Model;
using PmTool.Work.ChangeSets;

namespace PmTool.Work.Tests.ChangeSets;

/// <summary>
/// 一次套用最多改幾個項目。上限調成 2，用三個項目就測得到邊界；正式的上限另外確認接的是
/// <see cref="WorkRules.MaxItemsPerApply"/>。真的建五千個項目要跑很久，測到的仍然是同一個比較。
/// </summary>
[TestFixture]
public sealed class ChangeSetLimitTests
{
    private const int Limit = 2;

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync(services => services.AddSingleton(new ChangeSetLimits(Limit)));

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ApplyAsync_MoreItemsThanTheLimit_IsBlockedAndChangesNothing()
    {
        // Arrange：三個項目在進行中，拿掉進行中要把三個都搬走。
        var scenario = new Scenario(_host);
        var keys = await TasksInProgressAsync(scenario, 3);
        var proposed = await ChangeSetDrafts.ProposeAsync(
            scenario, "WEB", b => b.WithoutState("task_flow", "doing"), BlueprintEdits.Moves(("task_flow", "doing", "todo")));

        // Act
        var result = await _host.Changes.ApplyAsync(scenario.Admin, "WEB", proposed.ChangeSet.Number, CancellationToken.None);
        var states = await StatesAsync(scenario, keys);

        // Assert
        const string message = "這次套用會改到 3 個項目，超過一次最多 2 個的上限；請把修改拆成幾份變更集，分次套用。";
        Assert.That(proposed.Blockers.Select(b => (b.Code, b.Path, b.Message)), Is.EqualTo(new[] { (WorkErrorCodes.ChangeSetTooManyItems, string.Empty, message) }));
        Assert.That((proposed.Impacts[0].BlocksApply, proposed.CanApply), Is.EqualTo((false, false)), "上限不屬於任何一項影響");
        Assert.That((result.Failure!.Code, result.Failure.Details.Single().Code), Is.EqualTo((WorkErrorCodes.ChangeSetApplyBlocked, WorkErrorCodes.ChangeSetTooManyItems)));
        Assert.That(states, Has.All.EqualTo("doing"));
    }

    [Test]
    public async Task ApplyAsync_ExactlyTheLimit_IsApplied()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var keys = await TasksInProgressAsync(scenario, Limit);
        var proposed = await ChangeSetDrafts.ProposeAsync(
            scenario, "WEB", b => b.WithoutState("task_flow", "doing"), BlueprintEdits.Moves(("task_flow", "doing", "todo")));

        // Act
        var result = await _host.Changes.ApplyAsync(scenario.Admin, "WEB", proposed.ChangeSet.Number, CancellationToken.None);
        var states = await StatesAsync(scenario, keys);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => ChangeSetDrafts.Describe(result.Failure!));
        Assert.That(states, Has.All.EqualTo("todo"));
    }

    [Test]
    public async Task ApplyAsync_ItemsWhoseCategoryChanges_CountTowardTheLimit()
    {
        // Arrange：狀態沒有拿掉，只是換了類別；三個項目還是都要改寫。
        var scenario = new Scenario(_host);
        await TasksInProgressAsync(scenario, 3);

        // Act
        var proposed = await ChangeSetDrafts.ProposeAsync(
            scenario, "WEB", b => b.State("task_flow", "doing", s => s with { Category = StateCategory.Done }));

        // Assert
        Assert.That(proposed.Blockers.Select(b => b.Code), Is.EqualTo(new[] { WorkErrorCodes.ChangeSetTooManyItems }));
    }

    [Test]
    public async Task ProposeAsync_ItemsWhoseFieldIsTakenOffTheirType_CountTowardTheLimit()
    {
        // Arrange：三個任務有摘要，一個沒有；摘要從任務的欄位清單拿掉，有值的三個都要改寫。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        for (var n = 0; n < 3; n++)
        {
            await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "有摘要", Fields = Scenario.Fields(("summary", "範例摘要")) });
        }

        await scenario.CreateTaskAsync("WEB");

        // Act
        var proposed = await ChangeSetDrafts.ProposeAsync(
            scenario, "WEB", b => b.ItemType("task", t => t with { Fields = [.. t.Fields.Where(f => f != "summary")] }));

        // Assert
        Assert.That(proposed.Blockers.Select(b => (b.Code, b.Message)), Is.EqualTo(new[]
        {
            (WorkErrorCodes.ChangeSetTooManyItems, "這次套用會改到 3 個項目，超過一次最多 2 個的上限；請把修改拆成幾份變更集，分次套用。"),
        }));
    }

    [Test]
    public async Task ProposeAsync_OverTheLimitWithAConversionThatAborts_ReportsOnlyTheLimitWithoutCheckingEachValue()
    {
        // Arrange：三個任務的摘要都不是數字；超過上限已經一定擋下，不必再逐一檢查轉不轉得過去。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        for (var n = 0; n < 3; n++)
        {
            await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "有摘要", Fields = Scenario.Fields(("summary", "不是數字")) });
        }

        // Act
        var proposed = await ChangeSetDrafts.ProposeAsync(
            scenario,
            "WEB",
            b => b.Field("summary", f => f with { Type = FieldType.Number }),
            BlueprintEdits.Converts("summary", ConversionFailurePolicy.Abort));

        // Assert
        Assert.That(proposed.Blockers.Select(b => b.Code), Is.EqualTo(new[] { WorkErrorCodes.ChangeSetTooManyItems }));
        Assert.That((proposed.Impacts.Single().Affected, proposed.Impacts.Single().BlocksApply), Is.EqualTo((3L, false)));
    }

    [Test]
    public async Task ApplyAsync_DeletedItemsInTheMovedState_DoNotCountTowardTheLimit()
    {
        // Arrange：三個項目在進行中，刪掉一個；剩下的兩個剛好是上限。
        var scenario = new Scenario(_host);
        var keys = await TasksInProgressAsync(scenario, 3);
        var deleted = await _host.Items.DeleteAsync(scenario.Admin, keys[0], CancellationToken.None);
        var proposed = await ChangeSetDrafts.ProposeAsync(
            scenario, "WEB", b => b.WithoutState("task_flow", "doing"), BlueprintEdits.Moves(("task_flow", "doing", "todo")));

        // Act
        var result = await _host.Changes.ApplyAsync(scenario.Admin, "WEB", proposed.ChangeSet.Number, CancellationToken.None);
        var states = await StatesAsync(scenario, keys[1..]);

        // Assert
        Assert.That(deleted.Succeeded, Is.True, () => deleted.Failure!.Message);
        Assert.That((proposed.Blockers.Count, proposed.Impacts.Single().Affected), Is.EqualTo((0, 2L)));
        Assert.That(result.Succeeded, Is.True, () => ChangeSetDrafts.Describe(result.Failure!));
        Assert.That(states, Has.All.EqualTo("todo"));
    }

    [Test]
    public void AddWorkModule_WithoutOverride_UsesTheContractLimit()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddWorkModule("Host=localhost;Database=example");
        using var provider = services.BuildServiceProvider();

        // Assert
        var limits = provider.GetRequiredService<ChangeSetLimits>();
        Assert.That((limits.MaxItemsPerApply, limits.ApplyLockTimeout), Is.EqualTo((WorkRules.MaxItemsPerApply, TimeSpan.FromSeconds(5))));
    }

    private async Task<string[]> TasksInProgressAsync(Scenario scenario, int count)
    {
        await scenario.CreateProjectAsync("WEB");
        var keys = new List<string>();
        for (var n = 0; n < count; n++)
        {
            var item = await scenario.CreateTaskAsync("WEB");
            await _host.Items.TransitionAsync(scenario.Admin, item.Key, "doing", CancellationToken.None);
            keys.Add(item.Key);
        }

        return [.. keys];
    }

    private async Task<string[]> StatesAsync(Scenario scenario, string[] keys)
    {
        var states = new List<string>();
        foreach (var key in keys)
        {
            states.Add((await _host.Items.GetAsync(scenario.Admin, key, CancellationToken.None)).Value.State);
        }

        return [.. states];
    }
}
