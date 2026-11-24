using System.Text.Json;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.LocalAgents;

/// <summary><c>ai.local_engines.models</c>（jsonb）的讀寫：<c>[{"id","displayName","efforts":["low",…]}]</c>。</summary>
internal static class LocalModelsJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(IEnumerable<AiModelOption> models) => JsonSerializer.Serialize(
        models.Select(m => new Stored(m.Id, m.DisplayName, [.. m.Efforts.Select(AiEnumNames.NameOf)], m.DefaultEffort is { } d ? AiEnumNames.NameOf(d) : null)), Options);

    public static IReadOnlyList<AiModelOption> Deserialize(string json) =>
        [.. (JsonSerializer.Deserialize<List<Stored>>(json, Options) ?? [])
            .Select(s => new AiModelOption(s.Id, s.DisplayName, [.. s.Efforts.Select(AiEnumNames.ParseEffort)])
            {
                DefaultEffort = AiEnumNames.ParseOptionalEffort(s.DefaultEffort),
            })];

    private sealed record Stored(string Id, string DisplayName, List<string> Efforts, string? DefaultEffort = null);
}
