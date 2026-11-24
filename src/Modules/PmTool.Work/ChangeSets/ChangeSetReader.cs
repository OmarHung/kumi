using PmTool.Blueprint;
using PmTool.Blueprint.Model;
using PmTool.Blueprint.Planning;
using PmTool.Work.Access;
using PmTool.Work.Blueprints;
using PmTool.Work.Persistence;

namespace PmTool.Work.ChangeSets;

/// <summary>
/// 存著的變更集讀回模型、對照基準版本算計畫、組出 <see cref="ChangeSetDetail"/>。
/// 計畫是純邏輯，每次讀都重算，不另外存計畫的結果。
/// </summary>
internal sealed class ChangeSetReader(IBlueprintValidator validator, IBlueprintPlanner planner, BlueprintCache cache, ChangeSetLimits limits)
{
    public ChangeSetValidationResult Validate(string? document, DocumentFormat format) => validator.ValidateChangeSet(document, format);

    public BlueprintPlan Plan(ProjectSchema current, long currentVersion, ChangeSetRequest request) =>
        planner.Plan(current.Blueprint, currentVersion, request);

    /// <summary>存進去之前驗證過；讀回來不通過表示資料被動過或驗證規則變了，不是使用者能處理的事。</summary>
    public ChangeSetRequest Read(ChangeSetEntity entity)
    {
        var result = validator.ValidateChangeSet(entity.Document, DocumentFormat.Json, ValidationMode.Stored);
        return result.ChangeSet ?? throw new InvalidOperationException(
            $"變更集 {entity.Id} 讀不回來：{string.Join("；", result.Report.Issues.Take(3).Select(i => $"{i.Path} {i.Code}"))}");
    }

    /// <summary>
    /// 等待核准的依目前的資料重算影響與擋住的原因；已經決定的照當時的基準版本算差異，數量一律是 0。
    /// 計畫一律對照基準版本：等待核准的基準版本就是目前的版本（過期的在讀之前已經標好）。
    /// </summary>
    public async Task<ChangeSetDetail> DescribeAsync(WorkDbContext db, ProjectAccess access, ChangeSetEntity entity, CancellationToken cancellationToken)
    {
        var request = Read(entity);
        var baseSchema = await cache.GetAsync(db, entity.OrganizationId, entity.ProjectId, entity.BaseVersion, cancellationToken);
        var plan = Plan(baseSchema, entity.BaseVersion, request);
        if (ChangeSetRecords.ParseStatus(entity.Status) != ChangeSetStatus.Pending)
        {
            return new ChangeSetDetail(
                ChangeSetRecords.ToSummary(entity), request.Blueprint, plan.Changes, ImpactEvaluator.Unevaluated(plan.DataImpacts), [], CanApply: false)
            {
                BaseBlueprint = baseSchema.Blueprint,
            };
        }

        // 同一份變更集對照同一版藍圖，計畫一定和送出時一樣；不一樣表示計畫的規則改了，存著的資料要先處理。
        if (!plan.Report.IsValid)
        {
            throw new InvalidOperationException(
                $"變更集 {entity.Id} 對照第 {entity.BaseVersion} 版算不出計畫：{string.Join("；", plan.Report.Issues.Take(3).Select(i => i.Code))}");
        }

        var evaluation = await EvaluateAsync(db, Scope(access.Project, baseSchema, request, plan), cancellationToken);
        return Compose(access, entity, baseSchema, request, plan, evaluation);
    }

    /// <summary>等待核准的變更集：差異照計畫，影響與擋住的原因照剛算好的結果。</summary>
    public static ChangeSetDetail Compose(
        ProjectAccess access, ChangeSetEntity entity, ProjectSchema current, ChangeSetRequest request, BlueprintPlan plan, ChangeSetEvaluation evaluation) =>
        new(
            ChangeSetRecords.ToSummary(entity),
            request.Blueprint,
            plan.Changes,
            evaluation.Impacts,
            evaluation.Blockers,
            CanApply: evaluation.Blockers.Count == 0 && access.Can(WorkPermissions.BlueprintApply))
        {
            BaseBlueprint = current.Blueprint,
        };

    public Task<ChangeSetEvaluation> EvaluateAsync(WorkDbContext db, MigrationScope scope, CancellationToken cancellationToken) =>
        ImpactEvaluator.EvaluateAsync(db, scope, limits, cancellationToken);

    public static MigrationScope Scope(ProjectEntity project, ProjectSchema current, ChangeSetRequest request, BlueprintPlan plan) =>
        new(project, current, ProjectSchema.From(request.Blueprint), plan.DataImpacts);
}
