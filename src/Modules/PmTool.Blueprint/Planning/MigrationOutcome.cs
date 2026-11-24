namespace PmTool.Blueprint.Planning;

/// <summary>一組護欄檢查的結果：已經交代清楚、套用時要做的事，以及沒交代清楚而擋下計畫的問題。</summary>
internal sealed record MigrationOutcome(IReadOnlyList<DataImpact> Impacts, IReadOnlyList<ValidationIssue> Issues)
{
    public static MigrationOutcome Of(DataImpact impact) => new([impact], []);

    public static MigrationOutcome Of(IEnumerable<DataImpact> impacts) => new([.. impacts], []);

    public static MigrationOutcome Of(ValidationIssue issue) => new([], [issue]);

    public static MigrationOutcome Of(IEnumerable<ValidationIssue> issues) => new([], [.. issues]);

    public static MigrationOutcome Combine(params IEnumerable<MigrationOutcome> outcomes)
    {
        var all = outcomes.ToArray();
        return new MigrationOutcome([.. all.SelectMany(outcome => outcome.Impacts)], [.. all.SelectMany(outcome => outcome.Issues)]);
    }
}
