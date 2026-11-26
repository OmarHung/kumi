using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.HostAgents;

internal sealed partial class HostAgentWorkerService
{
    private const string SubscriptionMessage =
        "登入的是訂閱帳號（或判斷不出計費方式）。Claude 與 ChatGPT 的訂閱依條款不能給全組織共用，請改用 API 用量計費的登入（Anthropic Console、OpenAI API 金鑰）。";

    public async Task<Result<HostLoginClaim>> ClaimLoginAsync(HostAgentIdentity agent, string code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        if (HostSecrets.HashCode(code) is not { } hash)
        {
            return HostFailures.LoginRejected("登入碼不對。");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var login = await db.HostLogins.AsNoTracking().SingleOrDefaultAsync(
            l => l.CodeHash == hash && l.OrganizationId == agent.OrganizationId && l.AgentId == agent.AgentId, cancellationToken);
        if (login is null)
        {
            return HostFailures.LoginRejected("登入碼不對。");
        }

        var now = time.GetUtcNow();
        if (login.State != HostLoginStates.Pending)
        {
            return HostFailures.LoginRejected("這個登入碼已經用過或被作廢了，請在網頁重新開始登入。");
        }

        if (!await IsActiveAsync(db, agent, cancellationToken))
        {
            return HostFailures.LoginRejected(AgentNotActiveMessage);
        }

        if (login.ExpiresAt <= now)
        {
            return HostFailures.LoginRejected("登入碼已經過期，請在網頁重新開始登入。");
        }

        if (!await people.IsOwnerAsync(agent.OrganizationId, login.StartedBy, cancellationToken))
        {
            return HostFailures.LoginRejected("開始登入的人已經不是組織擁有者，請由現在的擁有者重新開始。");
        }

        // 條件寫在更新裡：同一個碼同時認領兩次，只有一個成功。
        var claimed = await db.HostLogins.Where(l => l.Id == login.Id && l.State == HostLoginStates.Pending).ExecuteUpdateAsync(
            set => set.SetProperty(l => l.State, HostLoginStates.Claimed).SetProperty(l => l.ClaimedAt, now), cancellationToken);
        return claimed == 0
            ? HostFailures.LoginRejected("這個登入碼已經用過或被作廢了，請在網頁重新開始登入。")
            : new HostLoginClaim(login.Id, AiEnumNames.ParseKind(login.Engine), login.ExpiresAt);
    }

    public async Task<Result<HostLoginCompleted>> CompleteLoginAsync(
        HostAgentIdentity agent, Guid loginId, HostLoginReport report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(report);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // 鎖住這一列：同一次登入的兩個完成回報排隊，只有第一個看得到「已認領」。
        var rows = await db.HostLogins.FromSql($"SELECT * FROM ai.host_logins WHERE id = {loginId} FOR UPDATE")
            .AsNoTracking().ToListAsync(cancellationToken);
        if (rows.Count == 0 || rows[0].OrganizationId != agent.OrganizationId || rows[0].AgentId != agent.AgentId)
        {
            return HostFailures.LoginNotFound();
        }

        var login = rows[0];
        if (login.State != HostLoginStates.Claimed)
        {
            return HostFailures.LoginRejected("這次登入已經結束（完成、失敗或作廢），不能再回報。");
        }

        var now = time.GetUtcNow();
        var problem = await IsActiveAsync(db, agent, cancellationToken)
            ? await RejectionAsync(login, report, now, cancellationToken)
            : AgentNotActiveMessage;
        if (problem is not null)
        {
            await db.HostLogins.Where(l => l.Id == loginId).ExecuteUpdateAsync(
                set => set
                    .SetProperty(l => l.State, HostLoginStates.Failed)
                    .SetProperty(l => l.CompletedAt, now)
                    .SetProperty(l => l.Billing, HostEnums.NameOf(report.Billing))
                    .SetProperty(l => l.Error, problem),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return HostFailures.LoginRejected(problem);
        }

        // 這個引擎之前的授權作廢，這次成為目前的授權；擁有者要求的登出也算完成了。
        await db.HostLogins
            .Where(l => l.AgentId == agent.AgentId && l.Engine == login.Engine && l.State == HostLoginStates.Completed)
            .ExecuteUpdateAsync(set => set.SetProperty(l => l.State, HostLoginStates.Cancelled), cancellationToken);
        await db.HostLogins.Where(l => l.Id == loginId).ExecuteUpdateAsync(
            set => set
                .SetProperty(l => l.State, HostLoginStates.Completed)
                .SetProperty(l => l.CompletedAt, now)
                .SetProperty(l => l.Billing, HostEnums.NameOf(HostBilling.Api)),
            cancellationToken);
        await ClearLogoutRequestAsync(db, agent.AgentId, login.Engine, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new HostLoginCompleted(loginId, AiEnumNames.ParseKind(login.Engine));
    }

    /// <summary>不能成為授權的原因（給 worker 與擁有者看）；可以是 null。順序：過期、擁有者身分、沒登入、計費方式。</summary>
    private async Task<string?> RejectionAsync(
        HostLoginRow login, HostLoginReport report, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (login.ExpiresAt <= now)
        {
            return "這次登入已經過期，請在網頁重新開始。";
        }

        if (!await people.IsOwnerAsync(login.OrganizationId, login.StartedBy, cancellationToken))
        {
            return "開始登入的人已經不是組織擁有者，這份登入不採用。";
        }

        if (!report.LoggedIn)
        {
            return AiText.Cut(
                report.Problem is { Length: > 0 } reason ? $"CLI 的登入沒有成功：{Clean(reason)}" : "CLI 的登入沒有成功，請重試。", AiRules.MaxErrorLength);
        }

        return report.Billing == HostBilling.Api ? null : SubscriptionMessage;
    }

    private const string AgentNotActiveMessage = "主機代理被停用或還沒配對，這次登入不採用。";

    private static Task<bool> IsActiveAsync(AiDbContext db, HostAgentIdentity agent, CancellationToken cancellationToken) =>
        db.HostAgents.AsNoTracking().AnyAsync(
            a => a.Id == agent.AgentId && a.OrganizationId == agent.OrganizationId && a.State == HostAgentStates.Active, cancellationToken);

    private static string Clean(string text) => LocalAgents.LocalAgentValidator.CleanText(text);

    private static Task ClearLogoutRequestAsync(AiDbContext db, Guid agentId, string engine, CancellationToken cancellationToken) =>
        HostLogoutRequests.RemoveAsync(db, agentId, [engine], cancellationToken);
}
