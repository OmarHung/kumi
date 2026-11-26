using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using PmTool.Blueprint;
using PmTool.Work.Persistence;

namespace PmTool.Work.Blueprints;

/// <summary>
/// 解析過的藍圖，以專案與版本為鍵。一版藍圖存進去之後就不會再變，所以快取不會過期，
/// 每個請求都重新驗證整份文件太浪費。
/// </summary>
internal sealed class BlueprintCache(IBlueprintValidator validator)
{
    // 舊版本不會再被讀，但字典不會自己變小；到上限就整個清掉重來，最多多解析一次，比追蹤誰最久沒用簡單。
    private const int MaxEntries = 1_000;

    private readonly ConcurrentDictionary<(Guid ProjectId, long Version), ProjectSchema> _entries = new();

    public async Task<ProjectSchema> GetAsync(
        WorkDbContext db, Guid organizationId, Guid projectId, long version, CancellationToken cancellationToken)
    {
        if (_entries.TryGetValue((projectId, version), out var cached))
        {
            return cached;
        }

        var document = await db.BlueprintVersions
            .AsNoTracking()
            .Where(v => v.OrganizationId == organizationId && v.ProjectId == projectId && v.Version == version)
            .Select(v => v.Document)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"專案 {projectId} 找不到第 {version} 版的藍圖。");
        var schema = Parse(document, projectId, version);
        Remember(projectId, version, schema);
        return schema;
    }

    /// <summary>
    /// 一次取好幾個專案的藍圖：沒快取的版本合成一個查詢讀，不要一個專案查一次（總覽一次要看幾十個專案）。
    /// 版本照呼叫端給的配對查，不在查詢裡重讀專案目前的版本：呼叫端讀版本與查藍圖之間有人套用變更集時，
    /// 重讀會把它要的那一版排除掉。版本存進去就不會變，所以照舊快取。
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, ProjectSchema>> GetManyAsync(
        WorkDbContext db, Guid organizationId, IReadOnlyCollection<(Guid ProjectId, long Version)> wanted, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, ProjectSchema>(wanted.Count);
        var missing = new List<(Guid ProjectId, long Version)>();
        foreach (var (projectId, version) in wanted)
        {
            if (_entries.TryGetValue((projectId, version), out var cached))
            {
                result[projectId] = cached;
            }
            else
            {
                missing.Add((projectId, version));
            }
        }

        if (missing.Count == 0)
        {
            return result;
        }

        var projectIds = missing.Select(m => m.ProjectId).Distinct().ToArray();
        var versions = missing.Select(m => m.Version).Distinct().ToArray();
        var rows = await db.BlueprintVersions
            .AsNoTracking()
            .Where(v => v.OrganizationId == organizationId
                && projectIds.Contains(v.ProjectId)
                && versions.Contains(v.Version))
            .Select(v => new { v.ProjectId, v.Version, v.Document })
            .ToListAsync(cancellationToken);
        var documents = rows.ToDictionary(r => (r.ProjectId, r.Version), r => r.Document);
        foreach (var (projectId, version) in missing)
        {
            var document = documents.GetValueOrDefault((projectId, version))
                ?? throw new InvalidOperationException($"專案 {projectId} 找不到第 {version} 版的藍圖。");
            var schema = Parse(document, projectId, version);
            Remember(projectId, version, schema);
            result[projectId] = schema;
        }

        return result;
    }

    public void Remember(Guid projectId, long version, ProjectSchema schema)
    {
        if (_entries.Count >= MaxEntries)
        {
            _entries.Clear();
        }

        _entries[(projectId, version)] = schema;
    }

    private ProjectSchema Parse(string document, Guid projectId, long version)
    {
        var result = validator.ValidateBlueprint(document, DocumentFormat.Json, ValidationMode.Stored);

        // 存進去之前驗證過，讀回來還不通過表示資料被動過或驗證規則變了，不是使用者能處理的事。
        return result.Blueprint is { } blueprint
            ? ProjectSchema.From(blueprint)
            : throw new InvalidOperationException(
                $"專案 {projectId} 第 {version} 版的藍圖讀不回來：{string.Join("；", result.Report.Issues.Take(3).Select(i => $"{i.Path} {i.Code}"))}");
    }
}
