using PmTool.Abstractions;

namespace PmTool.Work.Tests.Planning;

/// <summary>週期與里程碑測試共用的小工具。</summary>
internal static class PlanningSupport
{
    public static readonly DateOnly Day1 = new(2026, 11, 2);

    public static CycleDraft CycleOf(string name, int startOffset, int endOffset) =>
        new(name, Day1.AddDays(startOffset), Day1.AddDays(endOffset));

    public static MilestoneDraft MilestoneOf(string name, int dueOffset, string? description = null) =>
        new(name, Day1.AddDays(dueOffset), description);

    public static async Task<Cycle> CreateCycleAsync(WorkTestHost host, Actor actor, string project, CycleDraft draft)
    {
        var result = await host.Planning.CreateCycleAsync(actor, project, draft, CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        return result.Value;
    }

    public static async Task<Milestone> CreateMilestoneAsync(WorkTestHost host, Actor actor, string project, MilestoneDraft draft)
    {
        var result = await host.Planning.CreateMilestoneAsync(actor, project, draft, CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        return result.Value;
    }

    public static void AssertFailure<T>(Result<T> result, FailureKind kind, string code)
    {
        Assert.That(result.Failure, Is.Not.Null, "應該失敗");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Kind, Is.EqualTo(kind));
            Assert.That(result.Failure.Code, Is.EqualTo(code));
        }
    }

    public static async Task<WorkItem> ItemInAsync(
        WorkTestHost host, Scenario scenario, string project, Guid? cycleId = null, Guid? milestoneId = null, decimal? estimate = null, string title = "任務")
    {
        var result = await host.Items.CreateAsync(
            scenario.Admin,
            project,
            new NewWorkItem { Type = "task", Title = title, CycleId = cycleId, MilestoneId = milestoneId, Estimate = estimate },
            CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        return result.Value;
    }
}
