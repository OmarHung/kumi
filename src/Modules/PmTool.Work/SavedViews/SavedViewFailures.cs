using PmTool.Abstractions;
using static PmTool.Work.Common.TextRules;

namespace PmTool.Work.SavedViews;

internal static class SavedViewFailures
{
    /// <summary>別人的檢視與不存在的檢視回一樣的錯誤，不透露它存在。</summary>
    public static Failure NotFound() =>
        new(FailureKind.NotFound, WorkErrorCodes.SavedViewNotFound, "找不到這個檢視。");

    public static Failure NameTaken(string name) =>
        new(FailureKind.Conflict, WorkErrorCodes.SavedViewNameTaken, $"你已經有一個叫{Quote(name)}的檢視了（不分大小寫），請換一個名稱。");

    public static Failure LimitReached() => new(
        FailureKind.Conflict,
        WorkErrorCodes.SavedViewLimitReached,
        $"一個專案最多存 {SavedViewRules.MaxPerProject} 個檢視，請先刪掉用不到的。");

    public static Failure MyWorkLimitReached() => new(
        FailureKind.Conflict,
        WorkErrorCodes.SavedViewLimitReached,
        $"「我的工作」最多存 {SavedViewRules.MaxMyWork} 個檢視，請先刪掉用不到的。");
}
