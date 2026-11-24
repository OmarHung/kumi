using System.Text.Json;
using PmTool.Abstractions;

namespace PmTool.Work.Tests;

/// <summary>
/// 一個測試自己的組織。每個測試都開新的組織，同一個資料庫裡的測試彼此看不到對方的資料，
/// 專案代號也不會撞在一起。人員編號是隨機的，不需要身分模組。
/// </summary>
internal sealed class Scenario
{
    public const string SimpleTasks = "simple_tasks";
    public const string AgencyProject = "agency_project";

    public Scenario(WorkTestHost host)
    {
        Host = host;
        OrganizationId = Guid.NewGuid();
        Admin = new Actor(OrganizationId, Guid.NewGuid(), IsOrganizationAdmin: true);
    }

    public WorkTestHost Host { get; }

    public Guid OrganizationId { get; }

    /// <summary>組織管理員，也是這個情境裡專案的建立者。</summary>
    public Actor Admin { get; }

    /// <summary>組織裡的一般人員，還不是任何專案的成員。</summary>
    public Actor Person() => new(OrganizationId, Guid.NewGuid(), IsOrganizationAdmin: false);

    public async Task<ProjectSummary> CreateProjectAsync(string key = "WEB", string template = SimpleTasks)
    {
        var result = await Host.Projects.CreateAsync(Admin, new NewProject(key, $"範例專案 {key}", template), CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        return result.Value;
    }

    /// <summary>把一個新的人加進專案，回傳代表他的 Actor。</summary>
    public async Task<Actor> MemberAsync(string projectKey, string role)
    {
        var person = Person();
        var result = await Host.Projects.SetMemberAsync(Admin, projectKey, person.PersonId, role, CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        return person;
    }

    public async Task<WorkItem> CreateItemAsync(string projectKey, NewWorkItem item, Actor? actor = null)
    {
        var result = await Host.Items.CreateAsync(actor ?? Admin, projectKey, item, CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        return result.Value;
    }

    public Task<WorkItem> CreateTaskAsync(string projectKey, string title = "範例任務") =>
        CreateItemAsync(projectKey, new NewWorkItem { Type = "task", Title = title });

    public static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);

    public static IReadOnlyDictionary<string, JsonElement> Fields(params (string Key, object? Value)[] fields) =>
        fields.ToDictionary(f => f.Key, f => JsonSerializer.SerializeToElement(f.Value));
}
