using PmTool.Abstractions;
using PmTool.Blueprint.Model;

namespace PmTool.Work.Tests.OwnItems;

/// <summary>
/// 「只看自己的項目」的測試情境：專案 WEB 裡有一位執行者（contributor）、一位編輯者，
/// 三個項目：指派給執行者的、執行者自己建立的、只和編輯者有關的。
/// </summary>
internal sealed record OwnItemsWorld(
    Scenario Scenario, Actor Contributor, Actor Editor, WorkItem Assigned, WorkItem Created, WorkItem Others)
{
    public static async Task<OwnItemsWorld> CreateAsync(WorkTestHost host, string role = "contributor")
    {
        var scenario = new Scenario(host);
        await scenario.CreateProjectAsync("WEB");
        var contributor = await scenario.MemberAsync("WEB", role);
        var editor = await scenario.MemberAsync("WEB", "editor");
        var assigned = await scenario.CreateItemAsync(
            "WEB", new NewWorkItem { Type = "task", Title = "指派給執行者", AssigneeId = contributor.PersonId }, editor);
        var created = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "執行者自己建的" }, contributor);
        var others = await scenario.CreateItemAsync(
            "WEB", new NewWorkItem { Type = "task", Title = "只有編輯者的", AssigneeId = editor.PersonId }, editor);
        return new OwnItemsWorld(scenario, contributor, editor, assigned, created, others);
    }

    /// <summary>執行者看得到的兩個項目的編號，依編號排。</summary>
    public string[] OwnKeys => [Assigned.Key, Created.Key];

    /// <summary>以專案目前的藍圖為底改一版（自訂角色用）。</summary>
    public static async Task PublishAsync(WorkTestHost host, Scenario scenario, string projectKey, Func<ProjectBlueprint, ProjectBlueprint> change)
    {
        var current = await host.Projects.GetAsync(scenario.Admin, projectKey, CancellationToken.None);
        await StoredBlueprints.PublishAsync(host, scenario, projectKey, change(current.Value.Blueprint));
    }

    public static RoleDefinition Role(string key, params string[] permissions) =>
        new() { Key = key, Name = key, Permissions = permissions };
}
