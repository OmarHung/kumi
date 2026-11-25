using System.Globalization;
using PmTool.Abstractions;
using PmTool.Blueprint;
using PmTool.Blueprint.Model;
using PmTool.Work.Common;
using static PmTool.Work.Common.TextRules;

namespace PmTool.Work.ChangeSets;

/// <summary>變更集的失敗與擋住套用的原因。訊息給一般使用者看：說清楚是哪些資料、要先做什麼。</summary>
internal static class ChangeSetFailures
{
    /// <summary>擋住套用的原因指向 <see cref="ChangeSetDetail.Impacts"/> 裡的哪一項，畫面才能把原因標在那一項旁邊。</summary>
    public const string ImpactsPath = "impacts";

    /// <summary>擋住失敗的轉換時，訊息最多列出幾個項目編號；其餘的只說還有幾個。</summary>
    public const int MaxListedItems = 5;

    /// <summary>變更集一律要看得到專案所有的項目：影響的數量與轉換失敗的項目編號不能給只看自己項目的人。</summary>
    public static Failure NeedsAllItems() => WorkFailures.NeedsAllItems("查看、提出或套用設定的修改（變更集）");

    public static Failure NotFound(long number) =>
        new(FailureKind.NotFound, WorkErrorCodes.ChangeSetNotFound, $"找不到變更集 #{Plain(number)}。");

    public static Failure NotPending(long number, ChangeSetStatus status) =>
        new(FailureKind.Conflict, WorkErrorCodes.ChangeSetNotPending, $"變更集 #{Plain(number)} {StatusWords(status)}，不能再套用或拒絕。");

    public static Failure Outdated(long number, long baseVersion, long currentVersion) =>
        new(
            FailureKind.Conflict,
            WorkErrorCodes.ChangeSetOutdated,
            $"變更集 #{Plain(number)} 是照第 {Plain(baseVersion)} 版的設定寫的，但目前已經是第 {Plain(currentVersion)} 版，"
            + "所以它已經過期了；請照目前的設定重新提出。");

    public static Failure Busy(long number) =>
        new(
            FailureKind.Conflict,
            WorkErrorCodes.ChangeSetBusy,
            $"專案的項目正有人在修改，變更集 #{Plain(number)} 等不到它們做完；這次什麼都沒有改，請稍後再試。");

    public static Failure TooManyPending() =>
        new(
            FailureKind.Conflict,
            WorkErrorCodes.ChangeSetTooManyPending,
            $"這個專案已經有 {Count(WorkRules.MaxPendingChangeSetsPerProject)} 份等待核准的變更集，請先套用或拒絕一些。");

    public static Failure CannotReject() =>
        WorkFailures.Forbidden("只有提出這份變更集的人，或有核准套用權限的人，可以拒絕它。");

    /// <summary>驗證或計畫的問題照原樣放進 Details：代碼是 IssueCodes 的，路徑指向變更集文件。</summary>
    public static Failure Invalid(IReadOnlyList<FailureDetail> issues)
    {
        var more = issues.Count > 1 ? $"（另外還有 {Count(issues.Count - 1)} 個問題）" : string.Empty;
        return new Failure(FailureKind.Invalid, WorkErrorCodes.ChangeSetInvalid, $"變更集沒有通過檢查：{issues[0].Message}{more}")
        {
            Details = issues,
        };
    }

    public static Failure Invalid(ValidationReport report) =>
        Invalid([.. report.Issues.Select(i => new FailureDetail(i.Code, i.Path, i.Message))]);

    public static Failure ApplyBlocked(long number, IReadOnlyList<FailureDetail> blockers)
    {
        var more = blockers.Count > 1 ? $"（另外還有 {Count(blockers.Count - 1)} 個原因）" : string.Empty;
        return new Failure(
            FailureKind.Conflict, WorkErrorCodes.ChangeSetApplyBlocked, $"變更集 #{Plain(number)} 現在還不能套用：{blockers[0].Message}{more}")
        {
            Details = blockers,
        };
    }

    public static FailureDetail ItemsOfRemovedType(int impact, string typeName, long count) =>
        Blocker(
            WorkErrorCodes.ChangeSetItemsOfRemovedType,
            impact,
            $"{Quote(typeName)}底下還有 {Count(count)} 個項目，要先處理才能刪除這個類型。");

    public static FailureDetail ItemsUnderRemovedParent(int impact, string typeName, string parentName, long count) =>
        Blocker(
            WorkErrorCodes.ChangeSetItemsUnderRemovedParent,
            impact,
            $"還有 {Count(count)} 個{Quote(typeName)}掛在{Quote(parentName)}底下，要先把它們移開，才能取消這個上層關係。");

    public static FailureDetail MembersWithRemovedRole(int impact, string roleName, long count) =>
        Blocker(
            WorkErrorCodes.ChangeSetMembersWithRemovedRole,
            impact,
            $"還有 {Count(count)} 位成員的角色是{Quote(roleName)}，要先換成別的角色，才能刪除這個角色。");

    /// <param name="itemKeys">轉不過去的項目編號，依編號排序。</param>
    public static FailureDetail ConversionFailed(int impact, FieldDefinition field, FieldType toType, IReadOnlyList<string> itemKeys)
    {
        var listed = string.Join("、", itemKeys.Take(MaxListedItems));
        var rest = itemKeys.Count > MaxListedItems ? $" 等 {Count(itemKeys.Count)} 個項目" : string.Empty;
        return Blocker(
            WorkErrorCodes.ChangeSetConversionFailed,
            impact,
            $"{Quote(field.Name)}有 {Count(itemKeys.Count)} 個項目的值{ConversionProblem(toType)}（{listed}{rest}）。"
            + "請先改好這些值，或把轉不過去時的處理改成清空。");
    }

    public static FailureDetail TooManyItems(long count, int limit) =>
        new(
            WorkErrorCodes.ChangeSetTooManyItems,
            string.Empty,
            $"這次套用會改到 {Count(count)} 個項目，超過一次最多 {Count(limit)} 個的上限；請把修改拆成幾份變更集，分次套用。");

    private static FailureDetail Blocker(string code, int impact, string message) =>
        new(code, string.Create(CultureInfo.InvariantCulture, $"{ImpactsPath}/{impact}"), message);

    private static string ConversionProblem(FieldType toType) => toType switch
    {
        FieldType.Number => "不是數字，或超出數字的範圍",
        FieldType.Date => "不是 YYYY-MM-DD 格式的日期",
        FieldType.SingleSelect => "選了不只一個選項，轉不成單選",
        FieldType.Text => $"有換行或超過 {Count(WorkRules.MaxTextFieldLength)} 個字，轉不成單行文字",
        _ => "轉不過去",
    };

    private static string StatusWords(ChangeSetStatus status) => status switch
    {
        ChangeSetStatus.Applied => "已經套用了",
        ChangeSetStatus.Rejected => "已經被拒絕了",
        _ => "已經過期了（別的變更集先套用了）",
    };

    private static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Plain(long value) => value.ToString(CultureInfo.InvariantCulture);
}
