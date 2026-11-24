namespace PmTool.Blueprint;

public sealed record ValidationReport(IReadOnlyList<ValidationIssue> Issues, bool Truncated = false)
{
    /// <summary>單次回報的問題數上限，避免一份壞掉的文件產生過大的回應。</summary>
    public const int MaxIssues = 200;

    public bool IsValid => Issues.Count == 0;

    public static ValidationReport From(IEnumerable<ValidationIssue> issues)
    {
        var limited = issues.Take(MaxIssues + 1).ToArray();
        return limited.Length > MaxIssues
            ? new ValidationReport(limited[..MaxIssues], Truncated: true)
            : new ValidationReport(limited);
    }
}
