using PmTool.Blueprint;

namespace PmTool.Api.Endpoints;

internal sealed record ValidationIssueData(string Code, string Path, string Message);

/// <summary>驗證結果在 API 上的形狀。另外定義而不直接輸出模組的型別，模組加欄位時對外的回應才不會跟著變。</summary>
internal sealed record ValidationResultData(bool Valid, IReadOnlyList<ValidationIssueData> Issues, bool Truncated)
{
    public static ValidationResultData From(ValidationReport report) => new(
        report.IsValid,
        report.Issues.Select(issue => new ValidationIssueData(issue.Code, issue.Path, issue.Message)).ToArray(),
        report.Truncated);
}
