using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Blueprint;
using PmTool.Blueprint.Model;
using PmTool.Work.Persistence;

namespace PmTool.Work.Tests;

/// <summary>
/// 直接讀寫資料庫裡的藍圖版本。需要模板以外的藍圖、又不想經過變更集的計畫與搬移時靠這個準備（例如做出過時的基準版本）；
/// 也用來看存進去的文件本身，不經過快取。
/// </summary>
internal static class StoredBlueprints
{
    public static async Task<string> ReadCurrentAsync(WorkTestHost host, Guid organizationId, string projectKey)
    {
        await using var db = await CreateContextAsync(host);
        var project = await FindProjectAsync(db, organizationId, projectKey);
        return await db.BlueprintVersions
            .Where(v => v.OrganizationId == organizationId && v.ProjectId == project.Id && v.Version == project.CurrentBlueprintVersion)
            .Select(v => v.Document)
            .SingleAsync();
    }

    /// <summary>
    /// 讓專案改用新的一版藍圖：多存一版、專案指到它，和之後套用藍圖變更的結果一樣。
    /// 新的版本不在快取裡，服務會從資料庫讀回來。藍圖要先通過驗證，否則測的是壞掉的資料。
    /// </summary>
    public static async Task PublishAsync(WorkTestHost host, Scenario scenario, string projectKey, ProjectBlueprint blueprint)
    {
        var document = BlueprintSerializer.ToJson(blueprint);
        var check = BlueprintValidator.CreateDefault().ValidateBlueprint(document, DocumentFormat.Json);
        Assert.That(check.Report.Issues, Is.Empty, "測試準備的藍圖沒有通過驗證");
        await PublishDocumentAsync(host, scenario, projectKey, document);
    }

    /// <summary>
    /// 不經驗證直接存一份文件當新版本：模擬規則變嚴之前就存進去的舊藍圖。
    /// 新的規則擋得住新寫入，但擋不住已經在資料庫裡的文件，所以讀回來的路徑要照舊能讀。
    /// </summary>
    public static async Task PublishDocumentAsync(WorkTestHost host, Scenario scenario, string projectKey, string document)
    {
        await using var db = await CreateContextAsync(host);
        var project = await FindProjectAsync(db, scenario.OrganizationId, projectKey);
        var version = project.CurrentBlueprintVersion + 1;
        db.BlueprintVersions.Add(new BlueprintVersionEntity
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = scenario.OrganizationId,
            ProjectId = project.Id,
            Version = version,
            Document = document,
            CreatedBy = scenario.Admin.PersonId,
            CreatedAt = host.Clock.GetUtcNow(),
        });
        await db.SaveChangesAsync();
        await db.Projects
            .Where(p => p.OrganizationId == scenario.OrganizationId && p.Id == project.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.CurrentBlueprintVersion, version));
    }

    private static Task<WorkDbContext> CreateContextAsync(WorkTestHost host) =>
        host.Root.GetRequiredService<IDbContextFactory<WorkDbContext>>().CreateDbContextAsync();

    private static Task<ProjectEntity> FindProjectAsync(WorkDbContext db, Guid organizationId, string projectKey) =>
        db.Projects.AsNoTracking().SingleAsync(p => p.OrganizationId == organizationId && p.Key == projectKey);
}
