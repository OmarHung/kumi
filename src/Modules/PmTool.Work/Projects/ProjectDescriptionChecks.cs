using PmTool.Abstractions;
using PmTool.Work.Common;

namespace PmTool.Work.Projects;

/// <summary>專案說明與說明格式內容的檢查。專案說明的失敗路徑是 text，說明格式的是 body。</summary>
internal static class ProjectDescriptionChecks
{
    public const string TextPath = "text";

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

        return SecretText.ContainsSecret(normalized)
            ? Invalid(WorkErrorCodes.ProjectDescriptionSecret, "說明裡有私鑰或存取權杖。請改寫密碼管理器裡的項目名稱。", path)
            : normalized;
    }

    private static Failure Invalid(string code, string message, string path) => WorkFailures.Invalid(code, path, message);
}
