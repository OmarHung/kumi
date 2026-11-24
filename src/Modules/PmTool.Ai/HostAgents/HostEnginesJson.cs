using System.Text.Json;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.HostAgents;

/// <summary>worker 最後一次登記回報的一個引擎（存在 <c>host_agents.engines</c>）。readiness 不存，讀的時候才算。</summary>
internal sealed record StoredHostEngine(
    AiProviderKind Kind,
    bool Installed,
    string? Version,
    bool Supported,
    bool LoggedIn,
    HostBilling Billing,
    Guid? LoginId,
    IReadOnlyList<AiModelOption> Models,
    string? Problem);

/// <summary><c>host_agents.engines</c>、<c>logout_requested</c> 與 <c>host_sources.enabled_models</c>（jsonb）的讀寫。</summary>
internal static class HostEnginesJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(IEnumerable<StoredHostEngine> engines) => JsonSerializer.Serialize(
        engines.Select(e => new Stored(
            AiEnumNames.NameOf(e.Kind),
            e.Installed,
            e.Version,
            e.Supported,
            e.LoggedIn,
            HostEnums.NameOf(e.Billing),
            e.LoginId,
            [.. e.Models.Select(m => new StoredModel(
                m.Id, m.DisplayName, [.. m.Efforts.Select(AiEnumNames.NameOf)], AiEnumNames.NameOf(m.DefaultEffort)))],
            e.Problem)),
        Options);

    public static IReadOnlyList<StoredHostEngine> Deserialize(string json) =>
        [.. (JsonSerializer.Deserialize<List<Stored>>(json, Options) ?? []).Select(s => new StoredHostEngine(
            AiEnumNames.ParseKind(s.Kind),
            s.Installed,
            s.Version,
            s.Supported,
            s.LoggedIn,
            HostEnums.ParseBilling(s.Billing),
            s.LoginId,
            [.. s.Models.Select(m => new AiModelOption(m.Id, m.DisplayName, [.. m.Efforts.Select(AiEnumNames.ParseEffort)])
            {
                DefaultEffort = AiEnumNames.ParseOptionalEffort(m.DefaultEffort),
            })],
            s.Problem))];

    public static string SerializeNames(IEnumerable<string> names) => JsonSerializer.Serialize(names, Options);

    public static IReadOnlyList<string> DeserializeNames(string json) => JsonSerializer.Deserialize<List<string>>(json, Options) ?? [];

    private sealed record Stored(
        string Kind,
        bool Installed,
        string? Version,
        bool Supported,
        bool LoggedIn,
        string Billing,
        Guid? LoginId,
        List<StoredModel> Models,
        string? Problem);

    private sealed record StoredModel(string Id, string DisplayName, List<string> Efforts, string? DefaultEffort);
}
