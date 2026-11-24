using PmTool.Abstractions;
using static PmTool.Work.Common.TextRules;

namespace PmTool.Work.Common;

/// <summary>模組回報的失敗。訊息給一般使用者看，要說得出為什麼被擋。</summary>
internal static class WorkFailures
{
    public static Failure Forbidden(string message) => new(FailureKind.Forbidden, WorkErrorCodes.Forbidden, message);

    public static Failure MissingPermission(string permission) =>
        Forbidden($"你在這個專案沒有{ActionOf(permission)}的權限。");

    /// <summary>
    /// 要看得到專案所有項目才能做的事（變更集會算出整個專案的影響數量與項目編號），只看自己項目的人做不了。
    /// 和沒有權限同一個代碼，訊息說明要的是哪一種讀取。
    /// </summary>
    public static Failure NeedsAllItems(string action) =>
        Forbidden($"{action}要看得到專案裡所有的項目；你的角色只看得到自己負責或建立的項目。");

    public static Failure ProjectArchived(string projectName) =>
        new(FailureKind.Conflict, WorkErrorCodes.ProjectArchived, $"專案{Quote(projectName)}已封存，只能看不能改。");

    /// <summary>
    /// 專案那一列等不到排他鎖（有項目一直在寫入）。沿用 changeset.busy：同樣是「等不到寫入做完、什麼都沒改、稍後再試」，
    /// 網頁與 CLI 對它已經有處理，不為封存另開一個代碼。
    /// </summary>
    public static Failure ProjectBusy(string action) =>
        new(FailureKind.Conflict, WorkErrorCodes.ChangeSetBusy, $"專案的項目正有人在修改，{action}等不到它們做完；這次什麼都沒有改，請稍後再試。");

    public static Failure ProjectNotFound(string? projectKey) =>
        new(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, $"找不到專案{Quote(projectKey)}。");

    public static Failure ItemNotFound(string? itemKey) =>
        new(FailureKind.NotFound, WorkErrorCodes.ItemNotFound, $"找不到項目{Quote(itemKey)}。");

    public static Failure AttachmentNotFound() =>
        new(FailureKind.NotFound, WorkErrorCodes.AttachmentNotFound, "找不到這個附件。");

    public static Failure AttachmentTooLarge() =>
        new(FailureKind.Invalid, WorkErrorCodes.AttachmentTooLarge, $"附件不能超過 {AttachmentRules.MaxBytes / (1024 * 1024)} MB。");

    public static Failure AttachmentLimitReached(string itemKey) =>
        new(FailureKind.Conflict, WorkErrorCodes.AttachmentLimitReached,
            $"項目{Quote(itemKey)}已經有 {AttachmentRules.MaxPerItem} 個附件，是上限；請先刪掉不用的。");

    public static Failure ProjectKeyTaken(string projectKey) =>
        new(FailureKind.Conflict, WorkErrorCodes.ProjectKeyTaken, $"專案代號{Quote(projectKey)}已經有專案在用了，請換一個。");

    /// <param name="parameter">出問題的查詢參數，名字見 <see cref="QueryPaths"/>。</param>
    public static Failure QueryInvalid(string parameter, string message) =>
        Invalid([new FailureDetail(WorkErrorCodes.QueryInvalid, parameter, message)]);

    public static Failure Invalid(string code, string path, string message) =>
        Invalid([new FailureDetail(code, path, message)]);

    /// <summary>
    /// 一次回報所有找到的問題，使用者才不用改一個、送一次、再看到下一個。
    /// 代碼與訊息用第一個問題的，其餘的在 Details 裡。
    /// </summary>
    public static Failure Invalid(IReadOnlyList<FailureDetail> details)
    {
        var first = details[0];
        var message = details.Count == 1 ? first.Message : $"{first.Message}（另外還有 {details.Count - 1} 個問題）";
        return new Failure(FailureKind.Invalid, first.Code, message) { Details = details };
    }

    private static string ActionOf(string permission) => permission switch
    {
        WorkPermissions.ItemRead => "查看項目",
        WorkPermissions.ItemCreate => "新增項目",
        WorkPermissions.ItemUpdate => "修改項目",
        WorkPermissions.ItemTransition => "改項目狀態",
        WorkPermissions.ItemDelete => "刪除項目",
        WorkPermissions.CommentCreate => "留言",
        WorkPermissions.ProjectManage => "管理專案成員",
        WorkPermissions.BlueprintRead => "查看專案設定",
        WorkPermissions.BlueprintPropose => "提出設定的修改",
        WorkPermissions.BlueprintApply => "核准套用設定的修改",
        _ => "做這件事",
    };
}
