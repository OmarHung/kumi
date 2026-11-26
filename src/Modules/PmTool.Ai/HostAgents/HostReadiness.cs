using Microsoft.EntityFrameworkCore;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.HostAgents;

/// <summary>引擎目前的登入授權：那個代理、那個引擎唯一一筆 <c>completed</c> 的登入。</summary>
internal sealed record HostAuthorization(Guid LoginId, DateTimeOffset At, Guid By);

/// <summary>一個來源連同它的代理與引擎現在的狀態（readiness 已經依現在的授權算好）。</summary>
internal sealed record HostSourceView(HostSourceRow Source, HostAgentRow Agent, HostEngineStatus Engine);

/// <summary>
/// 引擎能不能接工作、來源能不能用。順序照合約：沒裝、版本、沒登入、計費不能共用、沒有 Kumi 授權、可以。
/// 給擁有者看的說明（怎麼裝、怎麼登入）與給成員看的一句話（不叫成員執行任何指令）都在這裡。
/// </summary>
internal static class HostReadiness
{
    /// <param name="logoutRequested">擁有者要求這個引擎登出、worker 還沒回報做完（<c>host_agents.logout_requested</c>）。</param>
    public static HostEngineStatus Evaluate(AiProviderKind kind, StoredHostEngine? report, HostAuthorization? authorization, bool logoutRequested)
    {
        var status = EvaluateReport(kind, report, authorization);
        if (!logoutRequested || !status.LoggedIn)
        {
            return status;
        }

        // 授權在按下登出時就作廢了，這時照一般的規則會是「需要重新授權、請重新登入」，看起來像登出沒成功。
        return status with { LogoutPending = true, Problem = $"已要求 {HostEnums.EngineLabel(kind)} 登出，等主機回報（通常幾秒內）。" };
    }

    /// <summary>某個代理的某個引擎是不是正等著登出。</summary>
    public static bool IsLogoutRequested(HostAgentRow agent, AiProviderKind kind) =>
        HostEnginesJson.DeserializeNames(agent.LogoutRequested).Contains(AiEnumNames.NameOf(kind));

    private static HostEngineStatus EvaluateReport(AiProviderKind kind, StoredHostEngine? report, HostAuthorization? authorization)
    {
        var label = HostEnums.EngineLabel(kind);
        if (report is not { Installed: true })
        {
            return Status(kind, report, HostEngineReadiness.NotInstalled, authorization, [], $"主機上沒有 {label}，請確認 ai-worker 的映像檔有安裝它。");
        }

        if (!report.Supported)
        {
            return Status(kind, report, HostEngineReadiness.VersionUnsupported, authorization, [], $"主機上的 {label} 版本太舊或缺少必要的旗標，請更新 ai-worker 的映像檔。");
        }

        if (!report.LoggedIn)
        {
            return Status(kind, report, HostEngineReadiness.NotLoggedIn, authorization, report.Models, $"{label} 還沒登入，請在這裡按「開始登入」，再到主機上完成登入。");
        }

        if (report.Billing != HostBilling.Api)
        {
            return Status(kind, report, HostEngineReadiness.BillingNotShareable, authorization, report.Models, $"{label} 登入的是訂閱帳號（或判斷不出計費方式），依條款不能給全組織共用。請登出，改用 API 用量計費的登入。");
        }

        if (authorization is null || report.LoginId != authorization.LoginId)
        {
            return Status(kind, report, HostEngineReadiness.NotAuthorized, authorization, report.Models, $"{label} 的登入不是透過這裡開始的（或授權已被撤銷），請重新登入。");
        }

        return Status(kind, report, HostEngineReadiness.Ready, authorization, report.Models, null);
    }

    private static HostEngineStatus Status(
        AiProviderKind kind,
        StoredHostEngine? report,
        HostEngineReadiness readiness,
        HostAuthorization? authorization,
        IReadOnlyList<AiModelOption> models,
        string? defaultProblem) => new(
        kind,
        readiness,
        report?.Version,
        report?.LoggedIn ?? false,
        report?.Billing ?? HostBilling.Unknown,
        models,
        report?.Problem ?? defaultProblem,
        authorization?.At,
        authorization?.By);

    /// <summary>給成員的一句話：來源現在不能用的原因，不叫成員去做任何事、不提指令。</summary>
    public static string? MemberNotice(HostSourceStatus status, HostEngineReadiness readiness) => status switch
    {
        HostSourceStatus.Ready => null,
        HostSourceStatus.Disabled => "這個來源被組織擁有者關掉了。",
        HostSourceStatus.AgentUnavailable => "主機代理目前不能使用，請聯絡組織擁有者。",
        HostSourceStatus.AgentOffline => "主機代理目前離線，請組織擁有者檢查代理服務。",
        HostSourceStatus.EngineUnavailable => readiness switch
        {
            HostEngineReadiness.NotInstalled or HostEngineReadiness.VersionUnsupported => "這個引擎在主機上還不能使用，請聯絡組織擁有者。",
            HostEngineReadiness.BillingNotShareable => "這個來源的登入方式不能給全組織共用，請聯絡組織擁有者。",
            _ => "這個來源的登入已失效，需要組織擁有者重新授權。",
        },
        _ => "這個來源這個月的額度已經用完。",
    };

    /// <summary>來源的狀態，順序照合約：關掉、代理不能用、離線、引擎不能用、額度、可用。</summary>
    public static (HostSourceStatus Status, string? Message) SourceStatus(HostSourceView view, bool quotaReached, DateTimeOffset now)
    {
        if (!view.Source.Enabled)
        {
            return (HostSourceStatus.Disabled, "來源已關閉。");
        }

        if (view.Agent.State != HostAgentStates.Active)
        {
            return (HostSourceStatus.AgentUnavailable, view.Agent.State == HostAgentStates.Disabled ? "代理已停用。" : "代理還沒完成配對。");
        }

        if (view.Agent.LastSeenAt is not { } seen || now - seen > AiRules.HostAgentOnlineWindow)
        {
            return (HostSourceStatus.AgentOffline, "代理最近沒有登記，請檢查 ai-worker 服務有沒有在跑。");
        }

        if (view.Engine.Readiness != HostEngineReadiness.Ready)
        {
            return (HostSourceStatus.EngineUnavailable, view.Engine.Problem);
        }

        return quotaReached ? (HostSourceStatus.QuotaReached, "這個月的則數或 token 已經用到上限。") : (HostSourceStatus.Ready, null);
    }

    /// <summary>每個代理、每個引擎目前的授權（最新一筆 completed）。</summary>
    public static async Task<Dictionary<(Guid AgentId, string Engine), HostAuthorization>> AuthorizationsAsync(
        AiDbContext db, Guid organizationId, IReadOnlyCollection<Guid> agentIds, CancellationToken cancellationToken)
    {
        var rows = await db.HostLogins.AsNoTracking()
            .Where(l => l.OrganizationId == organizationId && agentIds.Contains(l.AgentId) && l.State == HostLoginStates.Completed)
            .OrderBy(l => l.CompletedAt)
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(
            l => (l.AgentId, l.Engine),
            l => new HostAuthorization(l.Id, l.CompletedAt ?? l.CreatedAt, l.StartedBy));
    }

    /// <summary>代理最後一次回報的某個引擎（沒回報過是 null）。</summary>
    public static StoredHostEngine? ReportOf(HostAgentRow agent, AiProviderKind kind) =>
        HostEnginesJson.Deserialize(agent.Engines).FirstOrDefault(e => e.Kind == kind);

    public static async Task<HostSourceView?> LoadViewAsync(
        AiDbContext db, Guid organizationId, Guid sourceId, CancellationToken cancellationToken)
    {
        var source = await db.HostSources.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == sourceId && s.OrganizationId == organizationId, cancellationToken);
        if (source is null)
        {
            return null;
        }

        var agent = await db.HostAgents.AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == source.AgentId && a.OrganizationId == organizationId, cancellationToken);
        return agent is null ? null : await ViewOfAsync(db, source, agent, cancellationToken);
    }

    public static async Task<HostSourceView> ViewOfAsync(
        AiDbContext db, HostSourceRow source, HostAgentRow agent, CancellationToken cancellationToken)
    {
        var auths = await AuthorizationsAsync(db, source.OrganizationId, [agent.Id], cancellationToken);
        return ViewOf(source, agent, auths);
    }

    public static HostSourceView ViewOf(
        HostSourceRow source, HostAgentRow agent, IReadOnlyDictionary<(Guid AgentId, string Engine), HostAuthorization> auths)
    {
        var kind = AiEnumNames.ParseKind(source.Engine);
        auths.TryGetValue((agent.Id, source.Engine), out var auth);
        return new HostSourceView(source, agent, Evaluate(kind, ReportOf(agent, kind), auth, IsLogoutRequested(agent, kind)));
    }
}
