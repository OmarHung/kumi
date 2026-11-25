using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.HostAgents;

/// <summary>
/// worker 那一頭（internal/HANDOFF-host-agent.md 第 6.4 節）。這個檔案是認證與配對；登入在 <c>.Logins.cs</c>，
/// 登記與領工作在 <c>.CheckIn.cs</c>，回報與結束在 <c>.Jobs.cs</c>。
/// 身分是機器憑證，不代表任何人：它只能做這幾件事，工作內容裡的人與權限一律來自伺服器存的工作。
/// </summary>
internal sealed partial class HostAgentWorkerService(
    IDbContextFactory<AiDbContext> contexts,
    HostPeople people,
    HostTimings timings,
    AssistantTimings assistantTimings,
    HostAgentSignals signals,
    TimeProvider time) : IHostAgentWorkerService
{
    public async Task<HostAgentIdentity?> AuthenticateAsync(string credential, string? machineKey, CancellationToken cancellationToken)
    {
        if (!HostSecrets.LooksLikeToken(credential, HostSecrets.CredentialPrefix))
        {
            return null;
        }

        var hash = HostSecrets.Hash(credential);
        var now = time.GetUtcNow();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        // 停用的代理一律不認（等重新啟用，worker 自己每 60 秒重試）；目前的與換新發出、還沒用過的下一把都認。
        var row = await db.HostAgents.AsNoTracking()
            .Where(a => a.State == HostAgentStates.Active && (a.CredentialHash == hash || a.NextCredentialHash == hash))
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var isNext = row.CredentialHash != hash;
        var expiresAt = isNext ? row.NextCredentialExpiresAt : row.CredentialExpiresAt;
        if (expiresAt is not { } expires || expires <= now)
        {
            return null;
        }

        // 每個請求都問身分模組（它自己有短暫的快取）：配對的人被降權或停用之後，下一個請求就不能再用。
        if (row.PairingStartedBy is not { } pairedBy || !await people.IsOwnerAsync(row.OrganizationId, pairedBy, cancellationToken))
        {
            return null;
        }

        // 機器編號綁在每個請求上：憑證被拿到別台，連領登入碼、回報、結束都過不了。只記警告，不撤銷（原本那台照常可用）。
        if (!string.Equals(row.MachineKey, machineKey, StringComparison.Ordinal))
        {
            await RecordSuspectAsync(db, row, machineKey, now, cancellationToken);
            return null;
        }

        if (isNext)
        {
            await PromoteNextCredentialAsync(db, row.Id, hash, cancellationToken);
        }

        return new HostAgentIdentity(row.OrganizationId, row.Id);
    }

    /// <summary>
    /// 報來的機器編號格式對才記（避免把任意長的標頭寫進資料庫）；沒帶標頭就沒有可記的。電腦名稱這條路沒有，留 null。
    /// 條件帶著狀態與讀到的憑證：重新配對（換了憑證）或停用之後，舊憑證的請求不能把警告寫回去。
    /// </summary>
    private static async Task RecordSuspectAsync(
        AiDbContext db, HostAgentRow row, string? machineKey, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!LocalAgentValidator.IsMachineKey(machineKey))
        {
            return;
        }

        var credentialHash = row.CredentialHash;
        await db.HostAgents
            .Where(a => a.Id == row.Id && a.State == HostAgentStates.Active && a.CredentialHash == credentialHash)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(a => a.SuspectMachineKey, machineKey)
                    .SetProperty(a => a.SuspectMachineName, (string?)null)
                    .SetProperty(a => a.SuspectSeenAt, now),
                cancellationToken);
    }

    /// <summary>
    /// 下一把第一次被用到：升成目前的、舊的作廢。條件寫在更新裡，同時兩個請求只有一個會升；
    /// 輸的那個再看一次，已經是目前的就照樣通過（兩個請求拿的是同一把）。
    /// </summary>
    private static async Task PromoteNextCredentialAsync(AiDbContext db, Guid agentId, string hash, CancellationToken cancellationToken) =>
        await db.HostAgents
            .Where(a => a.Id == agentId && a.NextCredentialHash == hash)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(a => a.CredentialHash, a => a.NextCredentialHash)
                    .SetProperty(a => a.CredentialExpiresAt, a => a.NextCredentialExpiresAt)
                    .SetProperty(a => a.NextCredentialHash, (string?)null)
                    .SetProperty(a => a.NextCredentialExpiresAt, (DateTimeOffset?)null),
                cancellationToken);

    public async Task<Result<HostPairingResult>> PairAsync(HostPairingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var valid = HostValidator.Pairing(request);
        if (valid.Failure is { } invalid)
        {
            return invalid;
        }

        if (HostSecrets.HashCode(request.Code) is not { } codeHash)
        {
            return HostFailures.PairingInvalid();
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var now = time.GetUtcNow();
        var agent = await db.HostAgents.AsNoTracking().SingleOrDefaultAsync(
            a => a.PairingCodeHash == codeHash && a.State == HostAgentStates.AwaitingPairing && a.PairingExpiresAt > now, cancellationToken);

        // 開始配對的人要「現在」還是擁有者；不通過什麼都不改（碼也還在，擁有者能重新產生）。
        if (agent?.PairingStartedBy is not { } startedBy
            || !await people.IsOwnerAsync(agent.OrganizationId, startedBy, cancellationToken))
        {
            return HostFailures.PairingInvalid();
        }

        var credential = HostSecrets.NewToken(HostSecrets.CredentialPrefix);
        var credentialHash = HostSecrets.Hash(credential);
        var (machineKey, machineName, version) = valid.Value;

        // 條件寫在更新裡：同一個碼同時來兩次，只有一個成功。
        var updated = await db.HostAgents
            .Where(a => a.Id == agent.Id && a.PairingCodeHash == codeHash && a.State == HostAgentStates.AwaitingPairing)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(a => a.State, HostAgentStates.Active)
                    .SetProperty(a => a.CredentialHash, credentialHash)
                    .SetProperty(a => a.CredentialExpiresAt, now + AiRules.HostCredentialLifetime)
                    .SetProperty(a => a.NextCredentialHash, (string?)null)
                    .SetProperty(a => a.NextCredentialExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(a => a.SuspectMachineKey, (string?)null)
                    .SetProperty(a => a.SuspectMachineName, (string?)null)
                    .SetProperty(a => a.SuspectSeenAt, (DateTimeOffset?)null)
                    .SetProperty(a => a.PairingCodeHash, (string?)null)
                    .SetProperty(a => a.PairingExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(a => a.MachineKey, machineKey)
                    .SetProperty(a => a.MachineName, machineName)
                    .SetProperty(a => a.AgentVersion, version)
                    .SetProperty(a => a.UpdatedAt, now),
                cancellationToken);
        return updated == 0 ? HostFailures.PairingInvalid() : new HostPairingResult(agent.Id, agent.Name, credential);
    }
}
