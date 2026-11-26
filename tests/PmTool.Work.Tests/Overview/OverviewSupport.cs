using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;

namespace PmTool.Work.Tests.Overview;

/// <summary>專案總覽測試共用的小工具。測試的時鐘預設的時區是 UTC+8（見 <see cref="FixedOrganizationClock"/>）。</summary>
internal static class OverviewSupport
{
    /// <summary>台北的 2026-11-10 中午；組織的「今天」是 11-10。</summary>
    public static readonly DateTimeOffset Noon = new(2026, 11, 10, 4, 0, 0, TimeSpan.Zero);

    public static readonly DateOnly Today = new(2026, 11, 10);

    /// <summary>把測試的時鐘直接撥到指定的時間（同一個測試類別裡的測試共用一個時鐘，所以每個測試開頭都要撥）。</summary>
    public static void SetNow(WorkTestHost host, DateTimeOffset at) => host.Clock.Advance(at - host.Clock.GetUtcNow());

    public static async Task<IReadOnlyList<ProjectOverview>> ListAsync(
        WorkTestHost host, Actor actor, ProjectOverviewScope scope = ProjectOverviewScope.Visible)
    {
        await using var scopeOfRequest = host.Root.CreateAsyncScope();
        return await GetService(scopeOfRequest).ListAsync(actor, scope, CancellationToken.None);
    }

    public static async Task<ProjectOverview> OneAsync(WorkTestHost host, Actor actor, string key, ProjectOverviewScope scope = ProjectOverviewScope.Visible)
    {
        var all = await ListAsync(host, actor, scope);
        return all.Single(o => o.ProjectKey == key);
    }

    public static async Task<WorkItem> TaskAsync(
        Scenario scenario, string project, string title, DateOnly? due = null, Guid? assignee = null, Actor? actor = null) =>
        await scenario.CreateItemAsync(
            project, new NewWorkItem { Type = "task", Title = title, DueDate = due, AssigneeId = assignee }, actor);

    public static async Task MoveAsync(Scenario scenario, string itemKey, string state, Actor? actor = null)
    {
        var moved = await scenario.Host.Items.TransitionAsync(actor ?? scenario.Admin, itemKey, state, CancellationToken.None);
        Assert.That(moved.Succeeded, Is.True, () => moved.Failure!.Message);
    }

    /// <summary>把一個新的人用指定的角色加進專案。</summary>
    public static async Task AddAsync(Scenario scenario, string project, Actor person, string role)
    {
        var result = await scenario.Host.Projects.SetMemberAsync(scenario.Admin, project, person.PersonId, role, CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
    }

    private static IProjectOverviewService GetService(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IProjectOverviewService>();
}
