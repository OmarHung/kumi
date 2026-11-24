using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Common;
using PmTool.Work.Persistence;
using PmTool.Work.Templates;

namespace PmTool.Work.Projects;

/// <summary>
/// 說明格式。同一個組織的新增、修改、刪除靠交易裡的 advisory lock 排成一條：「先數、先比對名稱、再切換預設」
/// 都是讀了再寫，不排隊的話同時的寫入會一起通過上限與同名的檢查。唯一索引只是最後一道防線。
/// </summary>
internal sealed partial class ProjectDescriptionService
{
    private const string OnlyAdminsMessage = "只有組織管理員可以管理說明格式。";

    public async Task<IReadOnlyList<DescriptionTemplate>> ListTemplatesAsync(Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var own = await ReadTemplatesAsync(db, actor.OrganizationId, cancellationToken);
        var ordered = own.OrderByDescending(t => t.IsDefault).ThenBy(t => t.Name, StringComparer.Ordinal).ThenBy(t => t.Id);
        return [.. ordered.Select(ToTemplate), BuiltInDescriptionTemplates.WebProject(isDefault: !own.Any(t => t.IsDefault))];
    }

    public async Task<Result<DescriptionTemplate>> CreateTemplateAsync(
        Actor actor, DescriptionTemplateInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(input);
        if (!actor.IsOrganizationAdmin)
        {
            return WorkFailures.Forbidden(OnlyAdminsMessage);
        }

        var checkedInput = DescriptionTemplateChecks.Check(input);
        if (checkedInput.Failure is { } invalid)
        {
            return invalid;
        }

        var (name, body, isDefault) = checkedInput.Value;
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockTemplatesAsync(db, actor.OrganizationId, cancellationToken);
        var existing = await ReadTemplatesAsync(db, actor.OrganizationId, cancellationToken);
        if (IsNameTaken(existing, name, exceptId: null))
        {
            return DescriptionTemplateChecks.NameTaken(name);
        }

        if (existing.Count >= ProjectDescriptionRules.MaxTemplatesPerOrganization)
        {
            return DescriptionTemplateChecks.LimitReached();
        }

        var now = WorkClock.Now(time);
        var entity = new DescriptionTemplateEntity
        {
            Id = WorkClock.NewId(time),
            OrganizationId = actor.OrganizationId,
            Name = name,
            Body = body,
            IsDefault = isDefault,
            CreatedAt = now,
            CreatedBy = actor.PersonId,
            UpdatedAt = now,
            UpdatedBy = actor.PersonId,
        };
        return await SaveAsync(db, transaction, actor, entity, isNew: true, cancellationToken);
    }

    public async Task<Result<DescriptionTemplate>> UpdateTemplateAsync(
        Actor actor, Guid templateId, DescriptionTemplateInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(input);
        if (!actor.IsOrganizationAdmin)
        {
            return WorkFailures.Forbidden(OnlyAdminsMessage);
        }

        var checkedInput = DescriptionTemplateChecks.Check(input);
        if (checkedInput.Failure is { } invalid)
        {
            return invalid;
        }

        var (name, body, isDefault) = checkedInput.Value;
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockTemplatesAsync(db, actor.OrganizationId, cancellationToken);
        var existing = await ReadTemplatesAsync(db, actor.OrganizationId, cancellationToken);
        var current = existing.SingleOrDefault(t => t.Id == templateId);
        if (current is null)
        {
            return DescriptionTemplateChecks.NotFound();
        }

        if (IsNameTaken(existing, name, templateId))
        {
            return DescriptionTemplateChecks.NameTaken(name);
        }

        var updated = current with
        {
            Name = name,
            Body = body,
            IsDefault = isDefault,
            UpdatedAt = WorkClock.Now(time),
            UpdatedBy = actor.PersonId,
        };
        return await SaveAsync(db, transaction, actor, updated, isNew: false, cancellationToken);
    }

    public async Task<Result<DescriptionTemplate>> DeleteTemplateAsync(Actor actor, Guid templateId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor.IsOrganizationAdmin)
        {
            return WorkFailures.Forbidden(OnlyAdminsMessage);
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockTemplatesAsync(db, actor.OrganizationId, cancellationToken);
        var current = (await ReadTemplatesAsync(db, actor.OrganizationId, cancellationToken)).SingleOrDefault(t => t.Id == templateId);
        if (current is null)
        {
            return DescriptionTemplateChecks.NotFound();
        }

        await db.DescriptionTemplates
            .Where(t => t.OrganizationId == actor.OrganizationId && t.Id == templateId)
            .ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToTemplate(current);
    }

    /// <summary>有要設成預設時，先把別份的預設取消（同一個交易，部分唯一索引才不會撞）。</summary>
    private static async Task<Result<DescriptionTemplate>> SaveAsync(
        WorkDbContext db,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        Actor actor,
        DescriptionTemplateEntity entity,
        bool isNew,
        CancellationToken cancellationToken)
    {
        try
        {
            if (entity.IsDefault)
            {
                await db.DescriptionTemplates
                    .Where(t => t.OrganizationId == actor.OrganizationId && t.IsDefault && t.Id != entity.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsDefault, false), cancellationToken);
            }

            if (isNew)
            {
                db.DescriptionTemplates.Add(entity);
                await db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                await db.DescriptionTemplates
                    .Where(t => t.OrganizationId == actor.OrganizationId && t.Id == entity.Id)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(t => t.Name, entity.Name)
                            .SetProperty(t => t.Body, entity.Body)
                            .SetProperty(t => t.IsDefault, entity.IsDefault)
                            .SetProperty(t => t.UpdatedAt, entity.UpdatedAt)
                            .SetProperty(t => t.UpdatedBy, entity.UpdatedBy),
                        cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return ToTemplate(entity);
        }
        catch (Exception exception) when (DescriptionTemplateChecks.ConflictFor(exception, entity.Name) is not null)
        {
            // 資料庫的 lower() 和程式的比對對少見的字元可能不一致；排隊之後只會在這種情況（或有寫入繞過排隊）撞到。
            return DescriptionTemplateChecks.ConflictFor(exception, entity.Name)!;
        }
    }

    // 內建的名稱也不能用，所以和內建的比對在這裡一起做。
    private static bool IsNameTaken(IReadOnlyList<DescriptionTemplateEntity> existing, string name, Guid? exceptId) =>
        string.Equals(name, BuiltInDescriptionTemplates.WebProjectName, StringComparison.OrdinalIgnoreCase)
        || existing.Any(t => t.Id != exceptId && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    private static async Task<IReadOnlyList<DescriptionTemplateEntity>> ReadTemplatesAsync(
        WorkDbContext db, Guid organizationId, CancellationToken cancellationToken) =>
        await db.DescriptionTemplates.AsNoTracking().Where(t => t.OrganizationId == organizationId).ToListAsync(cancellationToken);

    private static Task LockTemplatesAsync(WorkDbContext db, Guid organizationId, CancellationToken cancellationToken)
    {
        var lockKey = $"description_templates:{organizationId}";
        return db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
    }

    private static DescriptionTemplate ToTemplate(DescriptionTemplateEntity row) =>
        new(row.Id.ToString(), row.Name, row.Body, IsBuiltIn: false, row.IsDefault);
}
