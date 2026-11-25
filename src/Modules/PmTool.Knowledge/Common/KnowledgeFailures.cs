using PmTool.Abstractions;

namespace PmTool.Knowledge.Common;

/// <summary>知識庫的失敗。代碼是對外合約（<see cref="KnowledgeErrorCodes"/>），訊息給人看。</summary>
internal static class KnowledgeFailures
{
    public static Failure Forbidden(string message) => new(FailureKind.Forbidden, KnowledgeErrorCodes.Forbidden, message);

    public static Failure SpaceNotFound(string? key) =>
        new(FailureKind.NotFound, KnowledgeErrorCodes.SpaceNotFound, $"找不到空間「{Shorten(key)}」。");

    public static Failure PageNotFound(int number) =>
        new(FailureKind.NotFound, KnowledgeErrorCodes.PageNotFound, $"找不到第 {number} 頁。");

    public static Failure SpaceArchived(string name) =>
        new(FailureKind.Conflict, KnowledgeErrorCodes.SpaceArchived, $"空間「{Shorten(name)}」已經封存（或它的專案已封存），只能看不能改。");

    public static Failure RestrictionOwnerRequired() =>
        new(FailureKind.Forbidden, KnowledgeErrorCodes.RestrictionOwnerRequired, "只有設定這個限制的人或空間的管理者能放寬（拿掉限制、加人、搬出有限制的範圍）。");

    public static Failure Conflict(string code, string message) => new(FailureKind.Conflict, code, message);

    public static Failure Invalid(string code, string path, string message) => Invalid([new FailureDetail(code, path, message)]);

    /// <summary>一次回報所有找到的問題；代碼與訊息用第一個問題的，其餘的在 Details 裡。</summary>
    public static Failure Invalid(IReadOnlyList<FailureDetail> details)
    {
        var first = details[0];
        var message = details.Count == 1 ? first.Message : $"{first.Message}（另外還有 {details.Count - 1} 個問題）";
        return new Failure(FailureKind.Invalid, first.Code, message) { Details = details };
    }

    /// <summary>使用者輸入放進訊息前截短：長度不受控的輸入原樣放進去，一個請求就能換到很大的回應。</summary>
    private static string Shorten(string? text)
    {
        const int max = 64;
        if (string.IsNullOrEmpty(text) || text.Length <= max)
        {
            return text ?? string.Empty;
        }

        // 不要從代理字元對的中間切開，切出來的半個字元寫不進 JSON。
        var length = char.IsHighSurrogate(text[max - 1]) ? max - 1 : max;
        return text[..length] + "…";
    }
}
