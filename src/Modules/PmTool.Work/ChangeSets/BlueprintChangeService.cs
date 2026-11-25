using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Blueprint;
using PmTool.Blueprint.Model;
using PmTool.Blueprint.Planning;
using PmTool.Work.Access;
using PmTool.Work.Blueprints;
using PmTool.Work.Common;
using PmTool.Work.Persistence;

namespace PmTool.Work.ChangeSets;

internal sealed class BlueprintChangeService(
    IDbContextFactory<WorkDbContext> contexts,
    BlueprintCache cache,
    ChangeSetReader reader,
    ChangeSetApplier applier,
    TimeProvider time) : IBlueprintChangeService
{
    public Task<Result<ChangeSetDetail>> ProposeAsync(
        Actor actor, string projectKey, string document, DocumentFormat format, CancellationToken cancellationToken) =>
        ProposeAsync(actor, projectKey, document, format, ChangeSetOrigin.Direct, cancellationToken);

    public async Task<Result<ChangeSetDetail>> ProposeAsync(
        Actor actor, string projectKey, string document, DocumentFormat format, ChangeSetOrigin origin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // 讀藍圖之前先對專案取共用鎖：正在套用的變更集會先做完，這份就對照套用後的版本計畫；
        // 這份送出之前，套用也會等它，之後一起把它標成過期。等待核准的變更集因此不會停在舊的版本上。
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await ProjectLocks.ShareAsync(db, actor.OrganizationId, projectKey, cancellationToken);
        var access = await ProjectAccess.LoadAsync(db, cache, actor, projectKey, cancellationToken);
        if (access is null)
        {
            return WorkFailures.ProjectNotFound(projectKey);
        }

        if (!access.Can(WorkPermissions.BlueprintPropose))
        {
            return access.Missing(WorkPermissions.BlueprintPropose);
        }

        if (!access.ReadsAllItems)
        {
            return ChangeSetFailures.NeedsAllItems();
        }

        var prepared = Prepare(access, document, format);
        if (prepared.Failure is { } invalid)
        {
            return invalid;
        }

        var (request, stored, plan) = prepared.Value;
        if (!plan.Report.IsValid)
        {
            return ChangeSetFailures.Invalid(plan.Report);
        }

        // 發流水號的 UPDATE 會鎖住專案那一列直到提交，同一個專案的新增項目（也要在那一列發號）都得等它；
        // 所以影響（可能要逐一看很多項目）先算好，發號與寫入放在最後。
        var evaluation = await reader.EvaluateAsync(db, ChangeSetReader.Scope(access.Project, access.Schema, request, plan), cancellationToken);
        var inserted = await InsertAsync(db, actor, access, request, stored, origin, cancellationToken);
        if (inserted.Failure is { } tooMany)
        {
            return tooMany;
        }

        await transaction.CommitAsync(cancellationToken);
        return ChangeSetReader.Compose(access, inserted.Value, access.Schema, request, plan, evaluation);
    }

    public async Task<Result<ChangeSetPreview>> PreviewAsync(
        Actor actor, string projectKey, string document, DocumentFormat format, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var readable = await ReadableAsync(db, actor, projectKey, cancellationToken);
        if (readable.Failure is { } failure)
        {
            return failure;
        }

        var access = readable.Value;
        var prepared = Prepare(access, document, format);
        if (prepared.Failure is { } invalid)
        {
            return invalid;
        }

        var (request, _, plan) = prepared.Value;

        // 基準版本過期不算文件有問題：照目前的版本算出差異，讓人看得到改了什麼，再把它列為擋住的原因。
        // 其他的計畫問題（搬移指示沒交代清楚等）和送出一樣當成文件有問題。
        var others = plan.Report.Issues.Where(issue => issue.Code != IssueCodes.PlanBaseVersionMismatch).ToArray();
        if (others.Length > 0)
        {
            return ChangeSetFailures.Invalid([.. plan.Report.Issues.Select(i => new FailureDetail(i.Code, i.Path, i.Message))]);
        }

        var evaluation = await reader.EvaluateAsync(db, ChangeSetReader.Scope(access.Project, access.Schema, request, plan), cancellationToken);
        FailureDetail[] outdated = [.. plan.Report.Issues.Select(i => new FailureDetail(i.Code, i.Path, i.Message))];
        var blockers = (IReadOnlyList<FailureDetail>)[.. outdated, .. evaluation.Blockers];
        return new ChangeSetPreview(
            request.BaseVersion,
            access.Project.CurrentBlueprintVersion,
            request.Blueprint,
            plan.Changes,
            evaluation.Impacts,
            blockers,
            CanApply: blockers.Count == 0 && access.Can(WorkPermissions.BlueprintApply));
    }

    public async Task<Result<IReadOnlyList<ChangeSetSummary>>> ListAsync(Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var access = await ReadableAsync(db, actor, projectKey, cancellationToken);
        if (access.Failure is { } failure)
        {
            return failure;
        }

        return await ChangeSetStore.ListAsync(db, access.Value.Project, cancellationToken);
    }

    public async Task<Result<ChangeSetDetail>> GetAsync(Actor actor, string projectKey, long number, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var access = await ReadableAsync(db, actor, projectKey, cancellationToken);
        if (access.Failure is { } failure)
        {
            return failure;
        }

        var entity = await ChangeSetStore.FindAsync(db, access.Value.Project, number, cancellationToken);
        return entity is null
            ? ChangeSetFailures.NotFound(number)
            : await reader.DescribeAsync(db, access.Value, entity, cancellationToken);
    }

    public Task<Result<ChangeSetDetail>> ApplyAsync(Actor actor, string projectKey, long number, CancellationToken cancellationToken) =>
        applier.ApplyAsync(actor, projectKey, number, cancellationToken);

    public async Task<Result<ChangeSetDetail>> RejectAsync(Actor actor, string projectKey, long number, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // 和送出一樣先取專案的共用鎖：封存（排他鎖）要等進行中的拒絕提交，封存之後才進來的拒絕讀到的就是封存的專案。
        // 讀取時順便把過期的標成過期（ReadableAsync），所以失敗的路徑也提交：過期是事實，要留下來。
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await ProjectLocks.ShareAsync(db, actor.OrganizationId, projectKey, cancellationToken);

        // 看不到變更集的人先擋下，再查這一份存不存在：否則從「沒有權限」與「找不到」就分辨得出哪些編號存在。
        var readable = await ReadableAsync(db, actor, projectKey, cancellationToken);
        if (readable.Failure is { } failure)
        {
            return failure;
        }

        var access = readable.Value;
        var refused = await RefusalAsync(db, access, actor, number, cancellationToken);
        if (refused is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return refused;
        }

        // 只改還在等待核准的那一份：同時有人套用它時，會等套用提交，再發現它已經套用了。
        var entity = (await ChangeSetStore.FindAsync(db, access.Project, number, cancellationToken))!;
        if (!await ChangeSetStore.DecideAsync(db, entity, ChangeSetRecords.Rejected, actor.PersonId, WorkClock.Now(time), null, cancellationToken))
        {
            var current = (await ChangeSetStore.FindAsync(db, access.Project, number, cancellationToken))!;
            await transaction.CommitAsync(cancellationToken);
            return ChangeSetFailures.NotPending(number, ChangeSetRecords.ParseStatus(current.Status));
        }

        var rejected = (await ChangeSetStore.FindAsync(db, access.Project, number, cancellationToken))!;
        await transaction.CommitAsync(cancellationToken);
        return await reader.DescribeAsync(db, access, rejected, cancellationToken);
    }

    /// <summary>拒絕之前的檢查：封存的專案不能動（提案的人自己撤回不需要 apply 權限，不會被權限擋下）、找得到、有權拒絕。</summary>
    private static async Task<Failure?> RefusalAsync(
        WorkDbContext db, ProjectAccess access, Actor actor, long number, CancellationToken cancellationToken)
    {
        if (access.IsArchived)
        {
            return WorkFailures.ProjectArchived(access.Project.Name);
        }

        var entity = await ChangeSetStore.FindAsync(db, access.Project, number, cancellationToken);
        if (entity is null)
        {
            return ChangeSetFailures.NotFound(number);
        }

        return entity.ProposedBy != actor.PersonId && !access.Can(WorkPermissions.BlueprintApply)
            ? ChangeSetFailures.CannotReject()
            : null;
    }

    /// <summary>送出與預覽共用：驗證文件、檢查文字、對照目前的藍圖算計畫。計畫裡的問題由呼叫端決定怎麼處理。</summary>
    private Result<(ChangeSetRequest Request, string Stored, BlueprintPlan Plan)> Prepare(ProjectAccess access, string document, DocumentFormat format)
    {
        var validated = reader.Validate(document, format);
        if (validated.ChangeSet is not { } request)
        {
            return ChangeSetFailures.Invalid(validated.Report);
        }

        var stored = BlueprintSerializer.ToJson(request);
        var problems = ChangeSetText.Problems(request.Summary, stored);
        if (problems.Count > 0)
        {
            return ChangeSetFailures.Invalid(problems);
        }

        return (request, stored, reader.Plan(access.Schema, access.Project.CurrentBlueprintVersion, request));
    }

    private async Task<Result<ChangeSetEntity>> InsertAsync(
        WorkDbContext db,
        Actor actor,
        ProjectAccess access,
        ChangeSetRequest request,
        string document,
        ChangeSetOrigin origin,
        CancellationToken cancellationToken)
    {
        // 發號的 UPDATE 鎖住專案那一列：同一個專案同時送出的會在這裡排隊，數出來的數量不會被同時送出的人繞過。
        var number = await ChangeSetStore.NextNumberAsync(db, access.Project, cancellationToken);
        var pending = await ChangeSetStore.CountPendingAsync(db, access.Project, cancellationToken);
        if (pending >= WorkRules.MaxPendingChangeSetsPerProject)
        {
            return ChangeSetFailures.TooManyPending();
        }

        var entity = new ChangeSetEntity
        {
            Id = WorkClock.NewId(time),
            OrganizationId = actor.OrganizationId,
            ProjectId = access.Project.Id,
            Number = number,
            Status = ChangeSetRecords.Pending,
            BaseVersion = request.BaseVersion,
            Summary = request.Summary,
            Document = document,
            ProposedBy = actor.PersonId,
            ProposedAt = WorkClock.Now(time),
            ProposedVia = ChangeSetRecords.FormatOrigin(origin),
        };
        db.ChangeSets.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    /// <summary>讀之前先把基準版本已經過時的等待核准變更集標成過期，讀到的狀態才是真的。</summary>
    private async Task<Result<ProjectAccess>> ReadableAsync(WorkDbContext db, Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        var access = await ProjectAccess.LoadAsync(db, cache, actor, projectKey, cancellationToken);
        if (access is null)
        {
            return WorkFailures.ProjectNotFound(projectKey);
        }

        if (!access.Can(WorkPermissions.BlueprintRead))
        {
            return access.Missing(WorkPermissions.BlueprintRead);
        }

        // 變更集的預覽、內容、影響都是整個專案的項目數量與編號：只看自己項目的人（藍圖本身照舊看得到）一律擋下。
        if (!access.ReadsAllItems)
        {
            return ChangeSetFailures.NeedsAllItems();
        }

        await ExpireStaleAsync(db, access, cancellationToken);
        return access;
    }

    private Task ExpireStaleAsync(WorkDbContext db, ProjectAccess access, CancellationToken cancellationToken) =>
        ChangeSetStore.ExpireStaleAsync(db, access.Project, access.Project.CurrentBlueprintVersion, WorkClock.Now(time), cancellationToken);
}
