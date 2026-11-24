using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Blueprint;
using PmTool.Work.Blueprints;

namespace PmTool.Work.Templates;

/// <param name="Problems">每一項是一句完整的話，寫出模板的 key、第幾筆、哪個欄位。有任何一項就不能用這份範例。</param>
internal sealed record ExampleReadResult(IReadOnlyList<ExampleItem> Items, IReadOnlyList<string> Problems)
{
    public IReadOnlyList<ExampleCycle> Cycles { get; init; } = [];

    public IReadOnlyList<ExampleMilestone> Milestones { get; init; } = [];

    public static ExampleReadResult Failed(params string[] problems) => new([], problems);
}

/// <summary>
/// 讀範例檔（格式見 internal/HANDOFF-template-library.md 第 2.1 節與 internal/HANDOFF-pm3a-cycles-milestones.md 第 2.6 節）並整份驗證。
/// 先檢查形狀（有哪些鍵、型別對不對），形狀沒問題才檢查內容（<see cref="ExampleChecker"/>），免得形狀錯誤引出一串連帶的問題。
/// </summary>
internal static class ExampleReader
{
    public const int MaxItems = 50;

    /// <summary>週期、里程碑各自最多幾筆：範例只是示意，多了多半是寫錯。</summary>
    public const int MaxPlanningEntries = 20;

    private const string ItemsKey = "items";
    private const string CyclesKey = "cycles";
    private const string MilestonesKey = "milestones";

    private static readonly string[] TopLevelKeys = [ItemsKey, CyclesKey, MilestonesKey];

    public static ExampleReadResult Read(string templateKey, string yaml, ProjectSchema schema, DateOnly today)
    {
        var parsed = BlueprintDocumentParser.Parse(yaml, DocumentFormat.Yaml);
        if (!parsed.Succeeded)
        {
            return ExampleReadResult.Failed(ExampleProblems.File(templateKey, $"不是合法的 YAML：{parsed.Issues[0].Message}"));
        }

        if (parsed.Root is not JsonObject root || root.Any(pair => !TopLevelKeys.Contains(pair.Key)) || root[ItemsKey] is not JsonArray list)
        {
            return ExampleReadResult.Failed(ExampleProblems.File(
                templateKey, $"最上層只能有 {string.Join("、", TopLevelKeys)}，其中 {ItemsKey} 一定要有，而且要是一個清單。"));
        }

        if (list.Count is 0 or > MaxItems)
        {
            return ExampleReadResult.Failed(ExampleProblems.File(templateKey, $"範例要有至少一筆、最多 {MaxItems} 筆，現在有 {list.Count} 筆。"));
        }

        var problems = new List<string>();
        var cycles = ReadEntries(templateKey, root, CyclesKey, problems, (position, element) => ReadCycle(templateKey, position, element, problems));
        var milestones = ReadEntries(templateKey, root, MilestonesKey, problems, (position, element) => ReadMilestone(templateKey, position, element, problems));
        var items = ReadItems(templateKey, list, problems);
        if (problems.Count == 0)
        {
            problems.AddRange(ExamplePlanningChecker.Check(templateKey, cycles, milestones, today));
            problems.AddRange(ExampleChecker.Check(templateKey, schema, items, today, cycles, milestones));
        }

        return problems.Count > 0
            ? ExampleReadResult.Failed([.. problems])
            : new ExampleReadResult(items, []) { Cycles = cycles, Milestones = milestones };
    }

    private static List<ExampleItem> ReadItems(string templateKey, JsonArray list, List<string> problems)
    {
        var items = new List<ExampleItem>();
        for (var index = 0; index < list.Count; index++)
        {
            var reader = new ExampleItemReader(templateKey, index + 1);
            if (reader.Read(JsonSerializer.SerializeToElement(list[index])) is { } item)
            {
                items.Add(item);
            }

            problems.AddRange(reader.Problems);
        }

        return items;
    }

    /// <summary>可以沒有這個清單；有的話要是清單、不超過上限，每一筆交給 <paramref name="read"/>。</summary>
    private static List<T> ReadEntries<T>(
        string templateKey, JsonObject root, string key, List<string> problems, Func<int, JsonElement, T?> read)
        where T : class
    {
        var entries = new List<T>();
        if (root[key] is null)
        {
            return entries;
        }

        if (root[key] is not JsonArray list)
        {
            problems.Add(ExampleProblems.File(templateKey, $"{key} 要是一個清單。"));
            return entries;
        }

        if (list.Count > MaxPlanningEntries)
        {
            problems.Add(ExampleProblems.File(templateKey, $"{key} 最多 {MaxPlanningEntries} 筆，現在有 {list.Count} 筆。"));
            return entries;
        }

        for (var index = 0; index < list.Count; index++)
        {
            if (read(index + 1, JsonSerializer.SerializeToElement(list[index])) is { } entry)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    private static ExampleCycle? ReadCycle(string templateKey, int position, JsonElement element, List<string> problems)
    {
        var reader = new ExampleCycleReader(templateKey, position);
        var cycle = reader.Read(element);
        problems.AddRange(reader.Problems);
        return cycle;
    }

    private static ExampleMilestone? ReadMilestone(string templateKey, int position, JsonElement element, List<string> problems)
    {
        var reader = new ExampleMilestoneReader(templateKey, position);
        var milestone = reader.Read(element);
        problems.AddRange(reader.Problems);
        return milestone;
    }
}

internal static class ExampleProblems
{
    public static string File(string templateKey, string message) => $"模板 {templateKey} 的範例檔：{message}";

    public static string Item(string templateKey, int position, string path, string message) =>
        $"模板 {templateKey} 的範例第 {position} 筆（{path}）：{message}";

    public static string Cycle(string templateKey, int position, string path, string message) =>
        $"模板 {templateKey} 的範例週期第 {position} 筆（{path}）：{message}";

    public static string Milestone(string templateKey, int position, string path, string message) =>
        $"模板 {templateKey} 的範例里程碑第 {position} 筆（{path}）：{message}";
}
