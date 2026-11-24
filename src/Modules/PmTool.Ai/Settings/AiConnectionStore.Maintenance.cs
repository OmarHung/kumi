using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Ai.Providers;

namespace PmTool.Ai.Settings;

internal sealed partial class AiConnectionStore
{
    public async Task<Result<AiConnection>> RefreshAsync(
        Actor actor, ConnectionOwner owner, Guid connectionId, CancellationToken cancellationToken)
    {
        var first = await FindAsync(owner, connectionId, cancellationToken);
        if (first is null)
        {
            return ConnectionNotFound;
        }

        var endpoint = EndpointOf(first, owner);
        if (endpoint.Failure is { } undecryptable)
        {
            return undecryptable;
        }

        var discovered = await DiscoverAsync(endpoint.Value, cancellationToken);
        if (discovered.Failure is { } failed)
        {
            return failed;
        }

        return await WriteRefreshAsync(actor, owner, first, discovered.Value, cancellationToken);
    }

    private async Task<Result<AiConnection>> WriteRefreshAsync(
        Actor actor, ConnectionOwner owner, AiConnectionRow first, IReadOnlyList<AiDiscoveredModel> discovered, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await owner.LockAsync(db, cancellationToken);
        var row = await db.Connections.AsNoTracking().OwnedBy(owner).SingleOrDefaultAsync(c => c.Id == first.Id, cancellationToken);
        if (row is null)
        {
            return ConnectionNotFound;
        }

        if (row.UpdatedAt != first.UpdatedAt)
        {
            return ChangedMeanwhile;
        }

        var now = clock.GetUtcNow();
        var merged = AiConnectionModels.Refresh(AiModelsJson.Deserialize(row.Models), discovered);
        var updated = row with
        {
            Models = AiModelsJson.Serialize(merged),
            ModelsCheckedAt = now,
            UpdatedAt = now,
            UpdatedBy = actor.PersonId,
        };
        db.Connections.Update(updated);
        await db.SaveChangesAsync(cancellationToken);
        if (!owner.IsPersonal)
        {
            await ClearDefaultEffortIfGoneAsync(db, actor, first.Id, merged, now, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await ToConnectionAsync(db, updated, cancellationToken);
    }

    public async Task<Result<Unit>> DeleteAsync(Actor actor, ConnectionOwner owner, Guid connectionId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await owner.LockAsync(db, cancellationToken);
        var deleted = await db.Connections.OwnedBy(owner).Where(c => c.Id == connectionId).ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
        {
            return ConnectionNotFound;
        }

        // 組織的連線是預設時一起清掉預設並關掉：沒有預設不能開著。個人的連線不會是預設。
        if (!owner.IsPersonal)
        {
            var now = clock.GetUtcNow();
            await db.Settings
                .Where(s => s.OrganizationId == owner.OrganizationId && s.DefaultConnectionId == connectionId)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.Enabled, false)
                    .SetProperty(s => s.DefaultConnectionId, (Guid?)null)
                    .SetProperty(s => s.DefaultModel, (string?)null)
                    .SetProperty(s => s.DefaultEffort, (string?)null)
                    .SetProperty(s => s.UpdatedAt, now)
                    .SetProperty(s => s.UpdatedBy, actor.PersonId), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return Unit.Value;
    }

    public async Task<Result<IReadOnlyList<AiDiscoveredModel>>> DiscoverModelsAsync(
        ConnectionOwner owner, AiDiscoveryInput input, CancellationToken cancellationToken)
    {
        AiConnectionRow? existing = null;
        if (input.ConnectionId is { } id)
        {
            existing = await FindAsync(owner, id, cancellationToken);
            if (existing is null)
            {
                return ConnectionNotFound;
            }
        }

        var validated = AiConnectionValidator.ValidateForDiscovery(input, existing, AllowPrivate(owner), owner.IsPersonal);
        if (validated.Failure is { } invalid)
        {
            return invalid;
        }

        var connection = validated.Value;
        string? key = connection.NewApiKey;
        if (connection.KeyChange == ApiKeyChange.Keep)
        {
            var stored = EndpointOf(existing!, owner);
            if (stored.Failure is { } undecryptable)
            {
                return undecryptable;
            }

            key = stored.Value.ApiKey;
        }

        return await DiscoverAsync(new AiEndpoint(connection.Kind, connection.BaseUrl, key, owner.IsPersonal), cancellationToken);
    }

    private async Task<AiConnectionRow?> FindAsync(ConnectionOwner owner, Guid connectionId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Connections.AsNoTracking().OwnedBy(owner).SingleOrDefaultAsync(c => c.Id == connectionId, cancellationToken);
    }

    private async Task<AiConnection> ToConnectionAsync(AiDbContext db, AiConnectionRow row, CancellationToken cancellationToken) =>
        AiConnectionMapper.ToConnection(row, await mapper.ReadUsageAsync(db, row.OrganizationId, cancellationToken));

    private async Task<Result<IReadOnlyList<AiDiscoveredModel>>> DiscoverAsync(AiEndpoint endpoint, CancellationToken cancellationToken)
    {
        try
        {
            return (await gateway.DiscoverAsync(endpoint, cancellationToken)).ToList();
        }
        catch (AiProviderException exception)
        {
            return new Failure(FailureKind.Invalid, AiErrorCodes.ProviderFailed, exception.Message);
        }
    }

    /// <summary>存的金鑰解開後的連線。解不開（金鑰環換了）時請重新輸入。</summary>
    private Result<AiEndpoint> EndpointOf(AiConnectionRow row, ConnectionOwner owner)
    {
        string? key = null;
        if (row.ProtectedApiKey is { } stored)
        {
            if (!protector.TryUnprotect(stored, out var plain))
            {
                return UndecryptableKey;
            }

            key = plain;
        }

        return new AiEndpoint(AiEnumNames.ParseKind(row.Kind), row.BaseUrl, key, owner.IsPersonal);
    }

    private static bool ChangesEndpoint(AiConnectionRow existing, ValidatedConnection next) =>
        AiEnumNames.ParseKind(existing.Kind) != next.Kind
        || !string.Equals(existing.BaseUrl, next.BaseUrl, StringComparison.OrdinalIgnoreCase)
        || next.KeyChange == ApiKeyChange.Replace
        || (next.KeyChange == ApiKeyChange.Clear && existing.ProtectedApiKey is not null);

    private static string? HintOf(string? apiKey) =>
        apiKey is { } key && key.Length >= 2 * AiRules.ApiKeyHintLength ? key[^AiRules.ApiKeyHintLength..] : null;

    /// <summary>擁有者的連線數到上限（細項路徑是空字串），或名稱和同一個擁有者的別的連線重複。</summary>
    private static async Task<Failure?> CheckRoomAsync(
        AiDbContext db, ConnectionOwner owner, ValidatedConnection connection, Guid? excludingId, CancellationToken cancellationToken)
    {
        var others = db.Connections.AsNoTracking().OwnedBy(owner).Where(c => c.Id != excludingId);
        if (excludingId is null && await others.CountAsync(cancellationToken) >= owner.MaxConnections)
        {
            return TooMany(owner);
        }

        if (await others.AnyAsync(c => c.NameKey == connection.NameKey, cancellationToken))
        {
            return new Failure(FailureKind.Invalid, AiErrorCodes.SettingsInvalid, "連線的設定有幾個欄位不對。")
            {
                Details = [new FailureDetail(AiErrorCodes.FieldInvalid, "name", "已經有同名的連線。")],
            };
        }

        return null;
    }

    internal static Failure TooMany(ConnectionOwner owner)
    {
        var message = $"{owner.Subject}最多 {owner.MaxConnections} 個連線。";
        return new Failure(FailureKind.Invalid, AiErrorCodes.SettingsInvalid, message)
        {
            Details = [new FailureDetail(AiErrorCodes.TooManyConnections, string.Empty, message)],
        };
    }

    private static FailureDetail NotAvailable(string path, string message) => new(AiErrorCodes.ModelNotAvailable, path, message);

    /// <summary>預設的模型要還開著，預設的思考深度也要還在那個模型的清單裡；不然成員一送訊息就被「選擇不能用」擋下。</summary>
    private static async Task<Failure?> CheckDefaultStillAvailableAsync(
        AiDbContext db, Guid organizationId, Guid connectionId, IReadOnlyList<AiModel> models, CancellationToken cancellationToken)
    {
        var settings = await db.Settings.AsNoTracking().SingleOrDefaultAsync(s => s.OrganizationId == organizationId, cancellationToken);
        if (settings?.DefaultConnectionId != connectionId)
        {
            return null;
        }

        var model = models.FirstOrDefault(m => m.Id == settings.DefaultModel && m.Enabled);
        if (model is null)
        {
            return DefaultConflict("這個模型是預設，先換預設再關掉。");
        }

        return AiEnumNames.ParseOptionalEffort(settings.DefaultEffort) is { } effort && !model.Efforts.Contains(effort)
            ? DefaultConflict("這個模型不再提供預設用的思考深度，先換預設再改。")
            : null;
    }

    private static Failure DefaultConflict(string message) =>
        new(FailureKind.Invalid, AiErrorCodes.SettingsInvalid, "連線的設定有幾個欄位不對。")
        {
            Details = [NotAvailable("enabledModels", message)],
        };

    /// <summary>
    /// 重新讀取後，預設的思考深度不在那個模型的新清單裡，就改成「預設」（null）。不擋重新讀取：
    /// 這是供應商那邊的變動，不是管理員改了什麼，擋下來只會讓清單一直停在舊的；模型本身還在，成員照樣能用。
    /// </summary>
    private static async Task ClearDefaultEffortIfGoneAsync(
        AiDbContext db, Actor actor, Guid connectionId, IReadOnlyList<AiModel> models, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var settings = await db.Settings.AsNoTracking().SingleOrDefaultAsync(s => s.OrganizationId == actor.OrganizationId, cancellationToken);
        if (settings?.DefaultConnectionId != connectionId || AiEnumNames.ParseOptionalEffort(settings.DefaultEffort) is not { } effort)
        {
            return;
        }

        var model = models.FirstOrDefault(m => m.Id == settings.DefaultModel);
        if (model is null || model.Efforts.Contains(effort))
        {
            return;
        }

        await db.Settings
            .Where(s => s.OrganizationId == actor.OrganizationId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.DefaultEffort, (string?)null)
                .SetProperty(s => s.UpdatedAt, now)
                .SetProperty(s => s.UpdatedBy, actor.PersonId), cancellationToken);
    }
}
