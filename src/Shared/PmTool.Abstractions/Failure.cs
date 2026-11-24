namespace PmTool.Abstractions;

/// <summary>失敗的種類，決定對外回什麼狀態。規則擋下來的操作是 Invalid，不是例外。</summary>
public enum FailureKind
{
    Invalid,
    NotFound,
    Conflict,
    Forbidden,
}

/// <summary>失敗的細項，例如哪一個欄位的值不對。Path 用文件裡的屬性名稱，根是空字串。</summary>
public sealed record FailureDetail(string Code, string Path, string Message);

/// <summary>模組回給呼叫端的失敗。Code 給程式判斷，是對外合約；Message 給人看，用繁體中文。</summary>
public sealed record Failure(FailureKind Kind, string Code, string Message)
{
    public IReadOnlyList<FailureDetail> Details { get; init; } = [];
}
