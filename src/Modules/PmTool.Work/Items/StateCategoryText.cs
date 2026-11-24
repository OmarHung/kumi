using PmTool.Blueprint.Model;

namespace PmTool.Work.Items;

/// <summary>狀態類別在資料表裡存成和藍圖文件一樣的 snake_case 文字，直接查資料表也看得懂。</summary>
internal static class StateCategoryText
{
    public const string Backlog = "backlog";
    public const string NotStarted = "not_started";
    public const string InProgress = "in_progress";
    public const string Done = "done";
    public const string Canceled = "canceled";

    /// <summary>「結束了」的類別，「我的工作」不列這些。</summary>
    public static readonly IReadOnlyList<string> Closed = [Done, Canceled];

    public static string ToText(StateCategory category) => category switch
    {
        StateCategory.Backlog => Backlog,
        StateCategory.NotStarted => NotStarted,
        StateCategory.InProgress => InProgress,
        StateCategory.Done => Done,
        StateCategory.Canceled => Canceled,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "不認得的狀態類別。"),
    };

    public static StateCategory Parse(string text) => text switch
    {
        Backlog => StateCategory.Backlog,
        NotStarted => StateCategory.NotStarted,
        InProgress => StateCategory.InProgress,
        Done => StateCategory.Done,
        Canceled => StateCategory.Canceled,
        _ => throw new InvalidOperationException($"資料表裡有不認得的狀態類別：{text}。"),
    };
}
