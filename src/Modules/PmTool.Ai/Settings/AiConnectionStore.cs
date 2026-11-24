using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Ai.Providers;

namespace PmTool.Ai.Settings;

/// <summary>
/// 連線的新增、修改、重新讀取、刪除、讀取模型（internal/HANDOFF-ai-assistant.md 第 4.6 節）。組織的服務與個人的服務共用：
/// 誰能做由呼叫端先檢查，這裡只管欄位、金鑰、模型清單與唯一規則，一律只看 <see cref="ConnectionOwner"/> 的那一組連線。
/// 寫入在擁有者的 advisory lock 裡排隊；連供應商（最久 20 秒）一律在鎖外面做，不讓一個慢的供應商卡住別人。
/// </summary>
internal sealed partial class AiConnectionStore(
    IDbContextFactory<AiDbContext> contexts,
    IAiProviderGateway gateway,
    AiApiKeyProtector protector,
    AiConnectionMapper mapper,
    TimeProvider clock,
    AiModuleOptions options)
{
    private static readonly Failure ConnectionNotFound =
        new(FailureKind.NotFound, AiErrorCodes.ConnectionNotFound, "找不到這個連線。");

    // 連供應商的那幾秒裡，同一個連線被改了：不拿過期的清單去蓋。
    private static readonly Failure ChangedMeanwhile =
        new(FailureKind.Conflict, AiErrorCodes.SettingsConflict, "這個連線剛被別人改過，請重新整理再試。");

    private static readonly Failure UndecryptableKey =
        new(FailureKind.Invalid, AiErrorCodes.ProviderFailed, "存的金鑰解不開，請重新輸入金鑰。");

    // Ai:AllowPrivateEndpoints 只放寬組織的連線：個人的連線誰都能接，放寬了就是讓任何成員叫伺服器連內網。
    private bool AllowPrivate(ConnectionOwner owner) => options.AllowPrivateEndpoints && !owner.IsPersonal;

    public async Task<Result<AiConnection>> CreateAsync(
        Actor actor, ConnectionOwner owner, AiConnectionInput input, CancellationToken cancellationToken)
    {
        var validated = AiConnectionValidator.Validate(input, existing: null, AllowPrivate(owner), owner.IsPersonal);
        if (validated.Failure is { } invalid)
        {
            return invalid;
        }

        var connection = validated.Value;
        await using (var check = await contexts.CreateDbContextAsync(cancellationToken))
        {
            if (await CheckRoomAsync(check, owner, connection, null, cancellationToken) is { } full)
            {
                return full;
            }
        }

        var discovered = await DiscoverAsync(new AiEndpoint(connection.Kind, connection.BaseUrl, connection.NewApiKey, owner.IsPersonal), cancellationToken);
        if (discovered.Failure is { } failed)
        {
            return failed;
        }

        return await InsertAsync(actor, owner, connection, discovered.Value, cancellationToken);
    }

    /// <summary>連線數是否已到上限（登入前先問，不要白白換一把金鑰）。</summary>
    public async Task<bool> IsFullAsync(ConnectionOwner owner, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Connections.AsNoTracking().OwnedBy(owner).CountAsync(cancellationToken) >= owner.MaxConnections;
    }

    public async Task<IReadOnlyList<string>> NamesAsync(ConnectionOwner owner, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Connections.AsNoTracking().OwnedBy(owner).Select(c => c.Name).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AiConnection>> ListAsync(ConnectionOwner owner, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await db.Connections.AsNoTracking().OwnedBy(owner).ToListAsync(cancellationToken);
        var usage = await mapper.ReadUsageAsync(db, owner.OrganizationId, cancellationToken);
        return
        [
            .. rows
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Id)
                .Select(c => AiConnectionMapper.ToConnection(c, usage)),
        ];
    }

    // 連線數與名稱要在鎖裡再查一次：前面的檢查之後可能有別人新增了。
    private async Task<Result<AiConnection>> InsertAsync(
        Actor actor,
        ConnectionOwner owner,
        ValidatedConnection connection,
        IReadOnlyList<AiDiscoveredModel> discovered,
        CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await owner.LockAsync(db, cancellationToken);
        if (await CheckRoomAsync(db, owner, connection, null, cancellationToken) is { } conflict)
        {
            return conflict;
        }

        var models = AiConnectionModels.Build(connection.Kind, discovered, connection.CustomModels, connection.EnabledModels);
        if (models.Failure is { } missing)
        {
            return missing;
        }

        var row = NewRow(actor, owner, connection, models.Value);
        db.Connections.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await ToConnectionAsync(db, row, cancellationToken);
    }

    private AiConnectionRow NewRow(Actor actor, ConnectionOwner owner, ValidatedConnection connection, IReadOnlyList<AiModel> models)
    {
        var now = clock.GetUtcNow();
        return new AiConnectionRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = owner.OrganizationId,
            OwnerPersonId = owner.PersonId,
            Name = connection.Name,
            NameKey = connection.NameKey,
            Kind = AiEnumNames.NameOf(connection.Kind),
            BaseUrl = connection.BaseUrl,
            ProtectedApiKey = connection.NewApiKey is { } key ? protector.Protect(key) : null,
            ApiKeyHint = HintOf(connection.NewApiKey),
            Models = AiModelsJson.Serialize(models),
            ModelsCheckedAt = now,
            CreatedAt = now,
            CreatedBy = actor.PersonId,
            UpdatedAt = now,
            UpdatedBy = actor.PersonId,
        };
    }

    public async Task<Result<AiConnection>> UpdateAsync(
        Actor actor, ConnectionOwner owner, Guid connectionId, AiConnectionInput input, CancellationToken cancellationToken)
    {
        var first = await FindAsync(owner, connectionId, cancellationToken);
        if (first is null)
        {
            return ConnectionNotFound;
        }

        var validated = AiConnectionValidator.Validate(input, first, AllowPrivate(owner), owner.IsPersonal);
        if (validated.Failure is { } invalid)
        {
            return invalid;
        }

        // 供應商、基底網址或金鑰換了才重新讀清單；只改名稱、開放的模型或手動的模型不連供應商。
        IReadOnlyList<AiDiscoveredModel>? discovered = null;
        if (ChangesEndpoint(first, validated.Value))
        {
            var endpoint = new AiEndpoint(validated.Value.Kind, validated.Value.BaseUrl, validated.Value.NewApiKey, owner.IsPersonal);
            var read = await DiscoverAsync(endpoint, cancellationToken);
            if (read.Failure is { } failed)
            {
                return failed;
            }

            discovered = read.Value;
        }

        return await WriteUpdateAsync(actor, owner, first, validated.Value, discovered, cancellationToken);
    }

    private async Task<Result<AiConnection>> WriteUpdateAsync(
        Actor actor,
        ConnectionOwner owner,
        AiConnectionRow first,
        ValidatedConnection input,
        IReadOnlyList<AiDiscoveredModel>? discovered,
        CancellationToken cancellationToken)
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

        if (await CheckRoomAsync(db, owner, input, first.Id, cancellationToken) is { } conflict)
        {
            return conflict;
        }

        var catalog = discovered ?? AiConnectionModels.CatalogOf(AiModelsJson.Deserialize(row.Models));
        var models = AiConnectionModels.Build(input.Kind, catalog, input.CustomModels, input.EnabledModels);
        if (models.Failure is { } missing)
        {
            return missing;
        }

        if (!owner.IsPersonal
            && await CheckDefaultStillAvailableAsync(db, owner.OrganizationId, first.Id, models.Value, cancellationToken) is { } stillDefault)
        {
            return stillDefault;
        }

        var updated = Apply(row, input, models.Value, discovered is not null, actor);
        db.Connections.Update(updated);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await ToConnectionAsync(db, updated, cancellationToken);
    }

    private AiConnectionRow Apply(
        AiConnectionRow row, ValidatedConnection input, IReadOnlyList<AiModel> models, bool rediscovered, Actor actor)
    {
        var now = clock.GetUtcNow();
        var (protectedKey, hint) = input.KeyChange switch
        {
            ApiKeyChange.Replace => (protector.Protect(input.NewApiKey!), HintOf(input.NewApiKey)),
            ApiKeyChange.Clear => ((string?)null, (string?)null),
            _ => (row.ProtectedApiKey, row.ApiKeyHint),
        };
        return row with
        {
            Name = input.Name,
            NameKey = input.NameKey,
            Kind = AiEnumNames.NameOf(input.Kind),
            BaseUrl = input.BaseUrl,
            ProtectedApiKey = protectedKey,
            ApiKeyHint = hint,
            Models = AiModelsJson.Serialize(models),
            ModelsCheckedAt = rediscovered ? now : row.ModelsCheckedAt,
            UpdatedAt = now,
            UpdatedBy = actor.PersonId,
        };
    }
}
