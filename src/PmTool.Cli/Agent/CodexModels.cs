using System.Text.Json;
using PmTool.Cli.Api;

namespace PmTool.Cli.Agent;

/// <summary>
/// Codex 能選的模型：讀它自己存的 <c>models_cache.json</c>（內部格式，沒有公開保證）。讀不懂就只列 <c>default</c>：
/// 不指定模型，由 Codex 自己的設定決定，永遠不會錯。
/// </summary>
internal static class CodexModels
{
    private static readonly HashSet<string> KnownEfforts = ["low", "medium", "high", "xhigh", "max"];

    // 伺服器最多收 MaxLocalEngineModels（20）個，default 佔一個。
    private const int MaxModels = 19;

    public static IReadOnlyList<EngineModel> Read(Func<string, string?> environment, Action<string>? log = null)
    {
        var home = environment("CODEX_HOME") is { Length: > 0 } configured
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        var fallback = new EngineModel(EngineModel.DefaultId, "預設", []);
        if (Processes.BoundedFile.TryReadText(Path.Combine(home, "models_cache.json"), log) is not { } text)
        {
            return [fallback];
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var models = document.RootElement.List("models")
                .Where(model => model.Text("visibility") != "hide" && model.Text("slug") is { Length: > 0 })
                .OrderBy(model => model.Number("priority") ?? long.MaxValue)
                .Select(ToModel)
                .Take(MaxModels)
                .ToList();
            // 預設的思考深度取各模型的交集：不指定模型時，Codex 用哪個不一定，只列每個模型都有的，才不會讓使用者選到不能用的。
            var common = models.Count == 0 ? [] : models.Select(model => model.Efforts).Aggregate((left, right) => [.. left.Intersect(right)]);
            // default 實際用哪個模型不確定（取決於使用者的 Codex 設定），不猜思考深度。
            return [fallback with { Efforts = common }, .. models];
        }
        catch (JsonException)
        {
            return [fallback];
        }
    }

    private static EngineModel ToModel(JsonElement model)
    {
        var slug = model.Text("slug")!;
        var efforts = model.List("supported_reasoning_levels")
            .Select(level => level.Text("effort"))
            .Where(effort => effort is not null && KnownEfforts.Contains(effort))
            .Select(effort => effort!)
            .Distinct()
            .ToArray();
        // 快取裡的 default_reasoning_level：不在這個模型自己的選項裡（例如 ultra 被略過）就不帶。
        var defaultEffort = model.Text("default_reasoning_level") is { } level && efforts.Contains(level) ? level : null;
        return new EngineModel(slug, model.Text("display_name") is { Length: > 0 } name ? name : slug, efforts, defaultEffort);
    }
}
