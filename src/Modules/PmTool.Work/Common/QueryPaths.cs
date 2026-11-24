namespace PmTool.Work.Common;

/// <summary>
/// 查詢條件的問題在 Details 裡的路徑。和 API 的查詢參數同名：用戶端要知道是哪個參數錯了，
/// 才能把問題標在對應的篩選上。模組不認得 HTTP，所以名字在這裡另寫一份，改參數名稱時兩邊要一起改。
/// </summary>
internal static class QueryPaths
{
    public const string Limit = "limit";
    public const string Cursor = "cursor";
    public const string Kind = "kind";
    public const string Type = "type";
    public const string State = "state";
    public const string StateCategory = "stateCategory";
    public const string Assignee = "assignee";
    public const string Search = "q";
    public const string Sort = "sort";
    public const string Parent = "parent";
    public const string Priority = "priority";
    public const string NoDue = "noDue";
    public const string Cycle = "cycle";
    public const string Milestone = "milestone";
}
