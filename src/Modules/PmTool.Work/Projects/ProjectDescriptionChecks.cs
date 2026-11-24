using System.Text.RegularExpressions;
using PmTool.Abstractions;
using PmTool.Work.Common;

namespace PmTool.Work.Projects;

/// <summary>專案說明與說明格式內容的檢查。專案說明的失敗路徑是 text，說明格式的是 body。</summary>
internal static class ProjectDescriptionChecks
{
    public const string TextPath = "text";

    private static readonly Regex PrivateKeyPattern = new(
        "-----BEGIN (?:[A-Z0-9]+ )*PRIVATE KEY(?: BLOCK)?-----", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PuttyPattern = new(
        "PuTTY-User-Key-File-[0-9]+:", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// 換行統一成 \n；整段空白是清掉（null）；其他照原樣，不去掉頭尾空白（縮排與換行都是內容）。
    /// 長度在換行統一之後算，和項目的說明一樣照 string.Length。
    /// </summary>
    public static Result<string?> Check(string? text, string path = TextPath, string subject = "專案說明")
    {
        var normalized = TextRules.NormalizeLineBreaks(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return (string?)null;
        }

        if (normalized.Length > ProjectDescriptionRules.MaxTextLength)
        {
            return Invalid(
                WorkErrorCodes.ProjectDescriptionTooLong,
                $"{subject}最多 {ProjectDescriptionRules.MaxTextLength} 個字，現在有 {normalized.Length} 個字。",
                path);
        }

        if (TextRules.HasForbiddenCharacters(normalized, allowLineBreaks: true))
        {
            return Invalid(
                WorkErrorCodes.ProjectDescriptionInvalid, $"{subject}不能有 NUL、換行與 Tab 以外的控制字元，或無效的字元。", path);
        }

        return ContainsPrivateKey(normalized)
            ? Invalid(WorkErrorCodes.ProjectDescriptionSecret, "說明裡有私鑰。私鑰不能存在這裡，請改寫密碼管理器裡的項目名稱。", path)
            : normalized;
    }

    /// <summary>
    /// 只認兩種，行內任何位置都算（Markdown 標記、單行貼上都擋）：「-----BEGIN …PRIVATE KEY-----」（含 PGP 的 PRIVATE KEY BLOCK），
    /// 以及 PuTTY 的檔頭。密碼字串猜不準，誤判的代價比漏掉高，交給格式的提醒。
    /// </summary>
    internal static bool ContainsPrivateKey(string text) => PrivateKeyPattern.IsMatch(text) || PuttyPattern.IsMatch(text);

    private static Failure Invalid(string code, string message, string path) => WorkFailures.Invalid(code, path, message);
}
