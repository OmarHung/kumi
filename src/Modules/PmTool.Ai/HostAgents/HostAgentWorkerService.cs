using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
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
    public async Task<HostAgentIdentity?> AuthenticateAsync(string credential, CancellationToken cancellationToken)
    {
        if (!HostSecrets.LooksLikeToken(credential, HostSecrets.CredentialPrefix))
        {
            return null;
        }

        var hash = HostSecrets.Hash(credential);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var found = await db.HostAgents.AsNoTracking()
            .Where(a => a.CredentialHash == hash && a.State != HostAgentStates.AwaitingPairing)
            .Select(a => new HostAgentIdentity(a.OrganizationId, a.Id))
            .SingleOrDefaultAsync(cancellationToken);
        return found;
    }

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
