using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.Settings;

/// <summary>
/// 組織的 AI 設定（internal/HANDOFF-ai-assistant.md 第 4.6 節）。只有組織管理員能讀、能改；金鑰加密後才存，也不會讀出來。
/// 寫入在組織層級的 advisory lock 裡排隊，設定與連線同一把鎖：兩個管理員同時改，預設不會指到剛被刪掉的模型。
/// 只看、只改組織的連線（擁有者是 null）；成員自己的連線在 <see cref="PersonalAiConnectionService"/>。連線的實作在 <see cref="AiConnectionStore"/>。
/// </summary>
internal sealed class AiSettingsService(
    IDbContextFactory<AiDbContext> contexts,
    AiConnectionStore store,
    AiConnectionMapper mapper,
    ConnectionUsageLedger usageLedger,
    TimeProvider clock) : IAiSettingsService
{
    private const string DefaultPath = "default";

    public async Task<Result<AiSettings>> GetAsync(Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (Forbidden(actor) is { } forbidden)
        {
            return forbidden;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await ReadSettingsAsync(db, actor.OrganizationId, cancellationToken);
    }

    public async Task<Result<AiSettings>> SaveAsync(Actor actor, AiSettingsInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(input);
        if (Forbidden(actor) is { } forbidden)
        {
            return forbidden;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await ConnectionOwner.Organization(actor.OrganizationId).LockAsync(db, cancellationToken);

        var connections = await db.Connections.AsNoTracking().OwnedBy(ConnectionOwner.Organization(actor.OrganizationId)).ToListAsync(cancellationToken);
        var problems = CheckDefault(input, connections);
        problems.AddRange(CheckLimits(input.Limits));
        if (problems.Count > 0)
        {
            return new Failure(FailureKind.Invalid, AiErrorCodes.SettingsInvalid, "AI 設定有幾個欄位不對。") { Details = problems };
        }

        var existing = await db.Settings.AsNoTracking().SingleOrDefaultAsync(s => s.OrganizationId == actor.OrganizationId, cancellationToken);
        var row = new AiSettingsRow
        {
            OrganizationId = actor.OrganizationId,
            Enabled = input.Enabled,
            DefaultConnectionId = input.Default?.ConnectionId,
            DefaultModel = input.Default?.Model,
            DefaultEffort = AiEnumNames.NameOf(input.Default?.Effort),
            UpdatedAt = clock.GetUtcNow(),
            UpdatedBy = actor.PersonId,
            AllowPersonalConnections = input.AllowPersonalConnections,
        };

        // 沒帶上限是沿用存的（舊的用戶端不送這一欄）；從來沒存過就是預設。
        var limits = input.Limits ?? ConnectionUsageLedger.ToLimits(existing);
        row = row with
        {
            DailyRequestsPerPerson = limits.DailyRequestsPerPerson,
            MonthlyRequests = limits.MonthlyRequests,
            MonthlyTokens = limits.MonthlyTokens,
        };
        if (existing is null)
        {
            db.Settings.Add(row);
        }
        else
        {
            db.Settings.Update(row);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await ReadSettingsAsync(db, actor.OrganizationId, cancellationToken);
    }

    private static List<FailureDetail> CheckDefault(AiSettingsInput input, List<AiConnectionRow> connections)
    {
        var problems = new List<FailureDetail>();
        if (input.Default is not { } selection)
        {
            if (input.Enabled)
            {
                problems.Add(new FailureDetail(AiErrorCodes.FieldRequired, DefaultPath, "開著的時候要選預設的連線與模型。"));
            }

            return problems;
        }

        var connection = connections.FirstOrDefault(c => c.Id == selection.ConnectionId);
        if (connection is null)
        {
            problems.Add(NotAvailable($"{DefaultPath}.connectionId", "找不到這個連線。"));
            return problems;
        }

        var model = AiModelsJson.Deserialize(connection.Models).FirstOrDefault(m => m.Id == selection.Model);
        if (model is not { Enabled: true })
        {
            problems.Add(NotAvailable($"{DefaultPath}.model", "這個模型沒有開放給成員用。"));
        }
        else if (selection.Effort is { } effort && !model.Efforts.Contains(effort))
        {
            problems.Add(NotAvailable($"{DefaultPath}.effort", "這個模型沒有這個思考深度。"));
        }

        return problems;
    }

    private static List<FailureDetail> CheckLimits(AiUsageLimits? limits)
    {
        var problems = new List<FailureDetail>();
        if (limits is null)
        {
            return problems;
        }

        CheckLimit(problems, limits.DailyRequestsPerPerson, AiRules.MaxHostRequestLimit, "limits.dailyRequestsPerPerson");
        CheckLimit(problems, limits.MonthlyRequests, AiRules.MaxHostRequestLimit, "limits.monthlyRequests");
        CheckLimit(problems, limits.MonthlyTokens, AiRules.MaxHostTokenLimit, "limits.monthlyTokens");
        return problems;
    }

    private static void CheckLimit(List<FailureDetail> problems, long? value, long max, string path)
    {
        if (value is { } v && (v < 1 || v > max))
        {
            problems.Add(new FailureDetail(AiErrorCodes.FieldInvalid, path, $"要是 1～{max} 之間的整數，或留空代表不限。"));
        }
    }

    private async Task<AiSettings> ReadSettingsAsync(AiDbContext db, Guid organizationId, CancellationToken cancellationToken)
    {
        var row = await db.Settings.AsNoTracking().SingleOrDefaultAsync(s => s.OrganizationId == organizationId, cancellationToken);
        var rows = await db.Connections.AsNoTracking().OwnedBy(ConnectionOwner.Organization(organizationId)).ToListAsync(cancellationToken);
        var usage = await mapper.ReadUsageAsync(db, organizationId, cancellationToken);
        var monthUsage = await usageLedger.MonthAsync(db, organizationId, cancellationToken);
        var connections = rows
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Id)
            .Select(c => AiConnectionMapper.ToConnection(c, usage))
            .ToList();
        if (row is null)
        {
            return new AiSettings(false, null, connections, null, null) { MonthUsage = monthUsage };
        }

        AiSelection? selection = row.DefaultConnectionId is { } id && row.DefaultModel is { } model
            ? new AiSelection(id, model, AiEnumNames.ParseOptionalEffort(row.DefaultEffort))
            : null;
        return new AiSettings(row.Enabled, selection, connections, row.UpdatedAt, row.UpdatedBy)
        {
            AllowPersonalConnections = row.AllowPersonalConnections,
            Limits = ConnectionUsageLedger.ToLimits(row),
            MonthUsage = monthUsage,
        };
    }

    public async Task<Result<AiConnection>> CreateConnectionAsync(Actor actor, AiConnectionInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(input);
        return Forbidden(actor) is { } forbidden
            ? forbidden
            : await store.CreateAsync(actor, ConnectionOwner.Organization(actor.OrganizationId), input, cancellationToken);
    }

    public async Task<Result<AiConnection>> UpdateConnectionAsync(
        Actor actor, Guid connectionId, AiConnectionInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(input);
        return Forbidden(actor) is { } forbidden
            ? forbidden
            : await store.UpdateAsync(actor, ConnectionOwner.Organization(actor.OrganizationId), connectionId, input, cancellationToken);
    }

    public async Task<Result<AiConnection>> RefreshModelsAsync(Actor actor, Guid connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return Forbidden(actor) is { } forbidden
            ? forbidden
            : await store.RefreshAsync(actor, ConnectionOwner.Organization(actor.OrganizationId), connectionId, cancellationToken);
    }

    public async Task<Result<Unit>> DeleteConnectionAsync(Actor actor, Guid connectionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return Forbidden(actor) is { } forbidden
            ? forbidden
            : await store.DeleteAsync(actor, ConnectionOwner.Organization(actor.OrganizationId), connectionId, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<AiDiscoveredModel>>> DiscoverModelsAsync(
        Actor actor, AiDiscoveryInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(input);
        return Forbidden(actor) is { } forbidden
            ? forbidden
            : await store.DiscoverModelsAsync(ConnectionOwner.Organization(actor.OrganizationId), input, cancellationToken);
    }

    private static FailureDetail NotAvailable(string path, string message) => new(AiErrorCodes.ModelNotAvailable, path, message);

    private static Failure? Forbidden(Actor actor) => actor.IsOrganizationAdmin
        ? null
        : new Failure(FailureKind.Forbidden, AiErrorCodes.Forbidden, "只有組織的擁有者或管理員能看、能改 AI 設定。");
}
