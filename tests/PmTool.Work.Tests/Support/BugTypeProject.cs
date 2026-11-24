using PmTool.Blueprint.Model;

namespace PmTool.Work.Tests;

/// <summary>
/// 接案專案再加一個「錯誤」類型，用自己的流程：triage（backlog）、doing（done 類，和任務流程的 doing 同 key、不同類別）；
/// 只能掛在需求底下。改類型的測試用它做出「同 key 不同類別」「沒有同類別」「上層只允許一部分類型」的情況。
/// </summary>
internal static class BugTypeProject
{
    public static async Task<Scenario> CreateAsync(WorkTestHost host, string projectKey = "WEB")
    {
        var scenario = new Scenario(host);
        await scenario.CreateProjectAsync(projectKey, Scenario.AgencyProject);
        var proposed = await ChangeSetDrafts.ProposeAsync(scenario, projectKey, blueprint => blueprint with
        {
            Workflows =
            [
                .. blueprint.Workflows,
                new Workflow
                {
                    Key = "bug_flow",
                    Name = "錯誤流程",
                    States =
                    [
                        new WorkflowState { Key = "triage", Name = "分類", Category = StateCategory.Backlog },
                        new WorkflowState { Key = "doing", Name = "修好了", Category = StateCategory.Done },
                    ],
                },
            ],
            ItemTypes = [.. blueprint.ItemTypes, new ItemType { Key = "bug", Name = "錯誤", Workflow = "bug_flow", Parents = ["requirement"] }],
        });
        await ChangeSetDrafts.ApplyAsync(scenario, projectKey, proposed.ChangeSet.Number);
        return scenario;
    }
}
