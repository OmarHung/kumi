using System.Text.Json;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.Settings;

/// <summary><c>ai.connections.models</c>（jsonb）的讀寫：<c>[{"id","displayName","efforts":["low",…],"enabled","custom"}]</c>。</summary>
internal static class AiModelsJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(IEnumerable<AiModel> models) => JsonSerializer.Serialize(
        models.Select(m => new Stored(m.Id, m.DisplayName, [.. m.Efforts.Select(AiEnumNames.NameOf)], m.Enabled, m.Custom)), Options);

    public static IReadOnlyList<AiModel> Deserialize(string json) =>
        [.. (JsonSerializer.Deserialize<List<Stored>>(json, Options) ?? [])
            .Select(s => new AiModel(s.Id, s.DisplayName, [.. s.Efforts.Select(AiEnumNames.ParseEffort)], s.Enabled, s.Custom))];

    private sealed record Stored(string Id, string DisplayName, List<string> Efforts, bool Enabled, bool Custom);
}
