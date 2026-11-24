using PmTool.Ai;

namespace PmTool.Api.Endpoints;

/// <summary>新增代理的內文。<c>enabled</c> 新增時不看（一律等配對），修改時 false 是停用；省略是 true。</summary>
internal sealed record HostAgentRequest(string Name, bool Enabled = true)
{
    public HostAgentInput ToInput() => new(Name) { Enabled = Enabled };
}

internal sealed record HostSourceLimitsData(int? DailyRequestsPerPerson, int? MonthlyRequests, long? MonthlyTokens)
{
    public HostSourceLimits ToLimits() => new(DailyRequestsPerPerson, MonthlyRequests, MonthlyTokens);

    public static HostSourceLimitsData From(HostSourceLimits limits) =>
        new(limits.DailyRequestsPerPerson, limits.MonthlyRequests, limits.MonthlyTokens);
}

/// <summary>要存的來源。<c>limits</c> 一定要帶（null 的欄位是不限）：整份取代，不能因為漏寫就把上限默默清掉。</summary>
internal sealed record HostSourceRequest(
    Guid AgentId,
    AiProviderKind Engine,
    string Name,
    bool Enabled,
    IReadOnlyList<string> EnabledModels,
    HostSourceLimitsData Limits,
    string? DefaultModel = null,
    AiEffort? DefaultEffort = null,
    bool AcknowledgeApiBilling = false)
{
    public HostSourceInput ToInput() => new(
        AgentId, Engine, Name, Enabled, EnabledModels, DefaultModel, DefaultEffort, Limits.ToLimits(), AcknowledgeApiBilling);
}

/// <summary>人員編號在 API 上一律是 <c>{ personId, displayName }</c>。</summary>
internal sealed record HostEngineStatusData(
    AiProviderKind Kind,
    HostEngineReadiness Readiness,
    string? Version,
    bool LoggedIn,
    HostBilling Billing,
    IReadOnlyList<AiModelOptionData> Models,
    string? Problem,
    DateTimeOffset? AuthorizedAt,
    PersonReference? AuthorizedBy)
{
    public static HostEngineStatusData From(HostEngineStatus engine, PeopleNames names) => new(
        engine.Kind,
        engine.Readiness,
        engine.Version,
        engine.LoggedIn,
        engine.Billing,
        [.. engine.Models.Select(m => new AiModelOptionData(m.Id, m.DisplayName, m.Efforts, m.DefaultEffort))],
        engine.Problem,
        engine.AuthorizedAt,
        engine.AuthorizedBy is { } person ? new PersonReference(person, names.NameOf(person)) : null);
}

internal sealed record HostLoginData(
    Guid Id,
    AiProviderKind Engine,
    HostLoginState State,
    DateTimeOffset ExpiresAt,
    string? Error,
    PersonReference StartedBy,
    DateTimeOffset CreatedAt)
{
    public static HostLoginData From(HostLogin login, PeopleNames names) => new(
        login.Id, login.Engine, login.State, login.ExpiresAt, login.Error, new PersonReference(login.StartedBy, names.NameOf(login.StartedBy)), login.CreatedAt);
}

internal sealed record HostAgentData(
    Guid Id,
    string Name,
    HostAgentState State,
    bool Online,
    DateTimeOffset? LastSeenAt,
    string? MachineName,
    string? AgentVersion,
    string? ServerUrl,
    IReadOnlyList<HostEngineStatusData> Engines,
    IReadOnlyList<HostLoginData> Logins,
    DateTimeOffset? PairingExpiresAt,
    DateTimeOffset CreatedAt)
{
    public static HostAgentData From(HostAgent agent, PeopleNames names) => new(
        agent.Id,
        agent.Name,
        agent.State,
        agent.Online,
        agent.LastSeenAt,
        agent.MachineName,
        agent.AgentVersion,
        agent.ServerUrl,
        [.. agent.Engines.Select(engine => HostEngineStatusData.From(engine, names))],
        [.. agent.Logins.Select(login => HostLoginData.From(login, names))],
        agent.PairingExpiresAt,
        agent.CreatedAt);

    public static IEnumerable<Guid> PeopleOf(HostAgent agent) =>
        agent.Engines.Select(engine => engine.AuthorizedBy).OfType<Guid>().Concat(agent.Logins.Select(login => login.StartedBy));
}

/// <summary>新增與重新配對：配對碼只在這個回應裡出現。<see cref="ToString"/> 不印它。</summary>
internal sealed record HostAgentPairingData(HostAgentData Agent, string PairingCode, DateTimeOffset PairingExpiresAt)
{
    public static HostAgentPairingData From(HostAgentPairing pairing, PeopleNames names) =>
        new(HostAgentData.From(pairing.Agent, names), pairing.PairingCode, pairing.PairingExpiresAt);

    public override string ToString() => $"HostAgentPairingData {{ Agent = {Agent.Id}, PairingCode = *** }}";
}

/// <summary>開始登入：登入碼只在這個回應裡出現。</summary>
internal sealed record HostLoginStartData(HostLoginData Login, string Code)
{
    public static HostLoginStartData From(HostLoginStart start, PeopleNames names) => new(HostLoginData.From(start.Login, names), start.Code);

    public override string ToString() => $"HostLoginStartData {{ Login = {Login.Id}, Code = *** }}";
}

internal sealed record HostUsageTotalsData(int Requests, int Completed, int Failed, int Stopped, long InputTokens, long OutputTokens)
{
    public static HostUsageTotalsData From(HostUsageTotals totals) =>
        new(totals.Requests, totals.Completed, totals.Failed, totals.Stopped, totals.InputTokens, totals.OutputTokens);
}

internal sealed record HostSourceData(
    Guid Id,
    Guid AgentId,
    AiProviderKind Engine,
    string Name,
    bool Enabled,
    HostSourceStatus Status,
    string? StatusMessage,
    IReadOnlyList<AiModelData> Models,
    string? DefaultModel,
    AiEffort? DefaultEffort,
    HostSourceLimitsData Limits,
    int Running,
    int Queued,
    HostUsageTotalsData Usage,
    DateTimeOffset? BillingAcknowledgedAt,
    DateTimeOffset UpdatedAt,
    PersonReference UpdatedBy)
{
    public static HostSourceData From(HostSource source, PeopleNames names) => new(
        source.Id,
        source.AgentId,
        source.Engine,
        source.Name,
        source.Enabled,
        source.Status,
        source.StatusMessage,
        [.. source.Models.Select(m => new AiModelData(m.Id, m.DisplayName, m.Efforts, m.Enabled, m.Custom))],
        source.DefaultModel,
        source.DefaultEffort,
        HostSourceLimitsData.From(source.Limits),
        source.Running,
        source.Queued,
        HostUsageTotalsData.From(source.Usage),
        source.BillingAcknowledgedAt,
        source.UpdatedAt,
        new PersonReference(source.UpdatedBy, names.NameOf(source.UpdatedBy)));
}

internal sealed record HostAgentOverviewData(IReadOnlyList<HostAgentData> Agents, IReadOnlyList<HostSourceData> Sources)
{
    public static HostAgentOverviewData From(HostAgentOverview overview, PeopleNames names) => new(
        [.. overview.Agents.Select(agent => HostAgentData.From(agent, names))],
        [.. overview.Sources.Select(source => HostSourceData.From(source, names))]);

    public static IEnumerable<Guid> PeopleOf(HostAgentOverview overview) =>
        overview.Agents.SelectMany(HostAgentData.PeopleOf).Concat(overview.Sources.Select(source => source.UpdatedBy));
}

internal sealed record HostPersonUsageData(PersonReference Person, HostUsageTotalsData Totals);

internal sealed record HostProjectUsageData(string ProjectKey, HostUsageTotalsData Totals);

internal sealed record HostSourceUsageData(
    string Month, HostUsageTotalsData Totals, IReadOnlyList<HostPersonUsageData> People, IReadOnlyList<HostProjectUsageData> Projects)
{
    public static HostSourceUsageData From(HostSourceUsage usage, PeopleNames names) => new(
        usage.Month,
        HostUsageTotalsData.From(usage.Totals),
        [.. usage.People.Select(p => new HostPersonUsageData(new PersonReference(p.PersonId, names.NameOf(p.PersonId)), HostUsageTotalsData.From(p.Totals)))],
        [.. usage.Projects.Select(p => new HostProjectUsageData(p.ProjectKey, HostUsageTotalsData.From(p.Totals)))]);

    public static IEnumerable<Guid> PeopleOf(HostSourceUsage usage) => usage.People.Select(p => p.PersonId);
}
