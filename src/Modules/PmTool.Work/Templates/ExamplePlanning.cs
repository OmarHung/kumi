using System.Text.Json;

namespace PmTool.Work.Templates;

/// <summary>範例檔裡的一個週期。日期是相對建立當天的天數，建立專案時才換成真正的日期。</summary>
/// <param name="Position">第幾筆，從 1 開始；錯誤訊息用它指出是哪一筆。</param>
/// <param name="Ref">給範例項目的 cycle 參照，只在同一個檔案裡有意義，不存進資料庫。</param>
internal sealed record ExampleCycle(int Position, string Ref, string Name, int StartInDays, int EndInDays)
{
    public ResolvedCycle Resolve(DateOnly today) => new(Name.Trim(), today.AddDays(StartInDays), today.AddDays(EndInDays));
}

internal sealed record ExampleMilestone(int Position, string Ref, string Name, int DueInDays, string? Description)
{
    public ResolvedMilestone Resolve(DateOnly today) =>
        new(Name.Trim(), today.AddDays(DueInDays), Planning.PlanningChecks.NormalizeDescription(Description));
}

internal sealed record ResolvedCycle(string Name, DateOnly StartDate, DateOnly EndDate);

internal sealed record ResolvedMilestone(string Name, DateOnly DueDate, string? Description);

/// <summary>範例檔裡的週期、里程碑與項目，已經通過讀取與驗證。</summary>
internal sealed record ExampleSet(
    IReadOnlyList<ExampleCycle> Cycles, IReadOnlyList<ExampleMilestone> Milestones, IReadOnlyList<ExampleItem> Items)
{
    public static ExampleSet Empty { get; } = new([], [], []);
}

internal sealed class ExampleCycleReader(string templateKey, int position) : ExampleShapeReader
{
    private static readonly string[] KnownKeys = ["ref", "name", "startInDays", "endInDays"];

    public ExampleCycle? Read(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            Add("(這一筆)", "每一筆要是一組欄位。");
            return null;
        }

        RejectUnknownKeys(element, KnownKeys);
        var reference = Text(element, "ref", required: true);
        var name = Text(element, "name", required: true);
        var start = Days(element, "startInDays", required: true);
        var end = Days(element, "endInDays", required: true);
        return Problems.Count == 0 ? new ExampleCycle(position, reference!, name!, start!.Value, end!.Value) : null;
    }

    protected override string Describe(string path, string message) => ExampleProblems.Cycle(templateKey, position, path, message);
}

internal sealed class ExampleMilestoneReader(string templateKey, int position) : ExampleShapeReader
{
    private static readonly string[] KnownKeys = ["ref", "name", "dueInDays", "description"];

    public ExampleMilestone? Read(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            Add("(這一筆)", "每一筆要是一組欄位。");
            return null;
        }

        RejectUnknownKeys(element, KnownKeys);
        var reference = Text(element, "ref", required: true);
        var name = Text(element, "name", required: true);
        var due = Days(element, "dueInDays", required: true);
        var description = Text(element, "description", required: false);
        return Problems.Count == 0 ? new ExampleMilestone(position, reference!, name!, due!.Value, description) : null;
    }

    protected override string Describe(string path, string message) => ExampleProblems.Milestone(templateKey, position, path, message);
}
