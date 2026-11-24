using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.HostAgents;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Persistence;
using PmTool.Ai.Providers;

namespace PmTool.Ai.Settings;

/// <summary>
/// 成員能選的 AI：組織開著時的組織連線，加上組織擁有者開的主機來源（<see cref="AiConnectionOption.Hosted"/>），加上組織允許時自己的連線（<see cref="AiConnectionOption.Personal"/>，排在後面），
/// 只列有開放模型的連線。每個登入的人都能讀，不看是不是管理員。
/// </summary>
internal sealed class AiOptionsService(IDbContextFactory<AiDbContext> contexts, TimeProvider time, HostSourceOptions hostSources) : IAiOptionsService
{
    public async Task<AiOptions> GetAsync(Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var settings = await db.Settings.AsNoTracking().SingleOrDefaultAsync(s => s.OrganizationId == actor.OrganizationId, cancellationToken);
        var allowPersonal = settings?.AllowPersonalConnections ?? false;
        var organization = settings is { Enabled: true }
            ? await OptionsOfAsync(db, ConnectionOwner.Organization(actor.OrganizationId), personal: false, cancellationToken)
            : [];
        var personal = allowPersonal
            ? await OptionsOfAsync(db, ConnectionOwner.Personal(actor), personal: true, cancellationToken)
            : [];
        var local = allowPersonal ? await LocalOptionsAsync(db, actor, cancellationToken) : [];

        // 主機來源排在組織的連線後面、自己的前面；和組織的 AI 開關、個人連線的開關無關。
        var hosted = await hostSources.ListAsync(actor, cancellationToken);
        var connections = organization.Concat(hosted).Concat(personal).Concat(local).ToList();
        return new AiOptions(connections.Count > 0, connections, organization.Count == 0 ? null : DefaultOf(settings!, organization))
        {
            CanAddPersonal = allowPersonal,
        };
    }

    // 自己在線的電腦上的引擎。排在自己存的連線後面；離線（60 秒內沒登記過）的不列，選了也送不出去。
    private async Task<List<AiConnectionOption>> LocalOptionsAsync(AiDbContext db, Actor actor, CancellationToken cancellationToken)
    {
        var since = time.GetUtcNow() - AiRules.LocalAgentOnlineWindow;
        var rows = await (
            from engine in db.LocalEngines.AsNoTracking()
            join agent in db.LocalAgents.AsNoTracking() on engine.AgentId equals agent.Id
            where agent.OrganizationId == actor.OrganizationId && agent.PersonId == actor.PersonId
                && engine.OrganizationId == actor.OrganizationId && agent.LastSeenAt >= since
            select new LocalEngine(engine, agent)).ToListAsync(cancellationToken);
        return rows
            .Select(row => (Row: row, Models: LocalModelsJson.Deserialize(row.Engine.Models)))
            .Where(row => row.Models.Count > 0)
            .Select(row =>
            {
                var kind = AiEnumNames.ParseKind(row.Row.Engine.Kind);
                return new AiConnectionOption(row.Row.Engine.Id, LocalEngineLookup.NameOf(kind, row.Row.Agent.Name), kind, row.Models)
                {
                    Personal = true,
                };
            })
            .OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.Id)
            .ToList();
    }

    private static async Task<List<AiConnectionOption>> OptionsOfAsync(
        AiDbContext db, ConnectionOwner owner, bool personal, CancellationToken cancellationToken)
    {
        var rows = await db.Connections.AsNoTracking().OwnedBy(owner).ToListAsync(cancellationToken);
        return rows
            .Select(c => (Row: c, Models: AiModelsJson.Deserialize(c.Models).Where(m => m.Enabled).ToList()))
            .Where(c => c.Models.Count > 0)
            .OrderBy(c => c.Row.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Row.Id)
            .Select(c => new AiConnectionOption(
                c.Row.Id,
                c.Row.Name,
                AiEnumNames.ParseKind(c.Row.Kind),
                [.. c.Models.Select(m => OptionOf(AiEnumNames.ParseKind(c.Row.Kind), m.Id, m.DisplayName, m.Efforts))])
            {
                Personal = personal,
            })
            .ToList();
    }

    private static AiModelOption OptionOf(AiProviderKind kind, string id, string displayName, IReadOnlyList<AiEffort> efforts) =>
        new(id, displayName, efforts)
        {
            // 只有 Anthropic 有文件可以對照；其他供應商不猜（本機的引擎自己回報，走 LocalModelsJson）。
            DefaultEffort = kind == AiProviderKind.Anthropic ? AiEffortCatalog.DefaultForAnthropic(id, efforts) : null,
        };

    // 預設指到已經關掉或不見的模型時不給：成員的輸入框會用它當起點，給了就是一個一送出就失敗的選擇。
    private static AiSelection? DefaultOf(AiSettingsRow settings, List<AiConnectionOption> connections)
    {
        if (settings.DefaultConnectionId is not { } id || settings.DefaultModel is not { } modelId)
        {
            return null;
        }

        var model = connections.FirstOrDefault(c => c.Id == id)?.Models.FirstOrDefault(m => m.Id == modelId);
        var effort = AiEnumNames.ParseOptionalEffort(settings.DefaultEffort);
        return model is null || (effort is { } e && !model.Efforts.Contains(e)) ? null : new AiSelection(id, modelId, effort);
    }
}
