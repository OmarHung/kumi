using PmTool.Abstractions;
using PmTool.Blueprint;
using PmTool.Blueprint.Model;

namespace PmTool.Work.Tests;

/// <summary>
/// 以專案目前的藍圖為底改一版，寫成變更集文件送出。文件用藍圖模組的寫法產生，和用戶端送來的 JSON 同一個形狀。
/// 草稿一律用管理員讀到的完整藍圖寫：沒有 blueprint.read 的人讀到的是精簡版，拿來寫會變成「刪掉自動化」。
/// </summary>
internal static class ChangeSetDrafts
{
    public const string Summary = "範例修改";

    public static string Json(long baseVersion, ProjectBlueprint blueprint, MigrationHints? migrations = null, string? summary = Summary) =>
        BlueprintSerializer.ToJson(new ChangeSetRequest
        {
            BaseVersion = baseVersion,
            Summary = summary,
            Blueprint = blueprint,
            Migrations = migrations,
        });

    /// <summary>專案目前的藍圖與版本。</summary>
    public static async Task<(ProjectBlueprint Blueprint, long Version)> CurrentAsync(Scenario scenario, string projectKey)
    {
        var project = await scenario.Host.Projects.GetAsync(scenario.Admin, projectKey, CancellationToken.None);
        Assert.That(project.Succeeded, Is.True, () => project.Failure!.Message);
        return (project.Value.Blueprint, project.Value.Project.BlueprintVersion);
    }

    /// <summary>照目前的版本寫一份變更集，但不送出。</summary>
    public static async Task<string> DraftAsync(
        Scenario scenario, string projectKey, Func<ProjectBlueprint, ProjectBlueprint> change, MigrationHints? migrations = null)
    {
        var (blueprint, version) = await CurrentAsync(scenario, projectKey);
        return Json(version, change(blueprint), migrations);
    }

    /// <param name="proposer">送出的人；沒給就是管理員。</param>
    public static async Task<Result<ChangeSetDetail>> TryProposeAsync(
        Scenario scenario,
        string projectKey,
        Func<ProjectBlueprint, ProjectBlueprint> change,
        MigrationHints? migrations = null,
        Actor? proposer = null)
    {
        var document = await DraftAsync(scenario, projectKey, change, migrations);
        return await scenario.Host.Changes.ProposeAsync(
            proposer ?? scenario.Admin, projectKey, document, DocumentFormat.Json, CancellationToken.None);
    }

    /// <summary>送出一份一定會通過的變更集，回傳它的內容。</summary>
    public static async Task<ChangeSetDetail> ProposeAsync(
        Scenario scenario,
        string projectKey,
        Func<ProjectBlueprint, ProjectBlueprint> change,
        MigrationHints? migrations = null,
        Actor? proposer = null)
    {
        var result = await TryProposeAsync(scenario, projectKey, change, migrations, proposer);
        Assert.That(result.Succeeded, Is.True, () => Describe(result.Failure!));
        return result.Value;
    }

    public static async Task<ChangeSetDetail> ApplyAsync(Scenario scenario, string projectKey, long number, Actor? actor = null)
    {
        var result = await scenario.Host.Changes.ApplyAsync(actor ?? scenario.Admin, projectKey, number, CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => Describe(result.Failure!));
        return result.Value;
    }

    /// <summary>失敗的代碼、訊息與每一個細項，斷言失敗時看得出是哪裡被擋。</summary>
    public static string Describe(Failure failure) =>
        $"{failure.Code}：{failure.Message} {string.Join("；", failure.Details.Select(d => $"{d.Code} {d.Path} {d.Message}"))}";
}
