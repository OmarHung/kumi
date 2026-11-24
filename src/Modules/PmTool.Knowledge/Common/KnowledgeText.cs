using System.Globalization;
using System.Text.RegularExpressions;
using PmTool.Abstractions;

namespace PmTool.Knowledge.Common;

/// <summary>
/// 空間與頁面的文字輸入檢查。每個模組各有一份文字規則是這個 repo 的慣例（身分、AI 也是）。
/// 會進資料庫的字串一律擋 NUL 與控制字元：PostgreSQL 不收，沒擋就是 500。
/// </summary>
internal static partial class KnowledgeText
{
    private static readonly Regex PrivateKeyPattern = PrivateKeyRegex();

    private static readonly Regex PuttyPattern = PuttyRegex();

    /// <summary>代號：去掉頭尾空白、轉大寫，要整段都合 <see cref="KnowledgeRules.SpaceKeyPattern"/>。</summary>
    public static bool TryNormalizeKey(string? key, out string normalized)
    {
        normalized = key?.Trim().ToUpperInvariant() ?? string.Empty;

        // .NET 的 $ 也會配到結尾換行的前面，所以再確認配到的範圍正好是整個字串。
        var match = KeyRegex().Match(normalized);
        return match.Success && match.Index == 0 && match.Length == normalized.Length;
    }

    /// <summary>名稱：去掉頭尾空白，1 到 <paramref name="maxLength"/> 個字，沒有換行、控制字元與看不見的格式字元。</summary>
    public static Result<string> CheckName(string? name, int maxLength, string path, string requiredCode, string tooLongCode, string subject)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || HasUnsafeNameCharacters(trimmed))
        {
            return KnowledgeFailures.Invalid(requiredCode, path, $"{subject}不能空白，也不能有換行、控制字元或看不見的格式字元。");
        }

        return trimmed.Length > maxLength
            ? KnowledgeFailures.Invalid(tooLongCode, path, $"{subject}最多 {maxLength} 個字，現在有 {trimmed.Length} 個字。")
            : trimmed;
    }

    /// <summary>空間的簡介：null 或整段空白是沒有；其他換行統一，擋太長與換行、Tab 以外的控制字元。</summary>
    public static Result<string?> CheckDescription(string? description)
    {
        var normalized = NormalizeLineBreaks(description);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return (string?)null;
        }

        normalized = normalized.Trim();
        if (normalized.Length > KnowledgeRules.MaxSpaceDescriptionLength)
        {
            return KnowledgeFailures.Invalid(
                KnowledgeErrorCodes.SpaceDescriptionInvalid,
                "description",
                $"簡介最多 {KnowledgeRules.MaxSpaceDescriptionLength} 個字，現在有 {normalized.Length} 個字。");
        }

        return HasForbiddenCharacters(normalized, allowLineBreaks: true)
            ? KnowledgeFailures.Invalid(
                KnowledgeErrorCodes.SpaceDescriptionInvalid, "description", "簡介不能有 NUL、換行與 Tab 以外的控制字元，或無效的字元。")
            : normalized;
    }

    /// <summary>頁面的內文：換行統一成 \n，不去掉頭尾空白（縮排是內容）。私鑰一律擋下，知識庫是全組織看得到、搜尋得到的地方。</summary>
    public static Result<string> CheckBody(string? body)
    {
        var normalized = NormalizeLineBreaks(body) ?? string.Empty;
        if (normalized.Length > KnowledgeRules.MaxPageBodyLength)
        {
            return KnowledgeFailures.Invalid(
                KnowledgeErrorCodes.PageBodyTooLong,
                "body",
                $"內容最多 {KnowledgeRules.MaxPageBodyLength} 個字，現在有 {normalized.Length} 個字。");
        }

        if (HasForbiddenCharacters(normalized, allowLineBreaks: true))
        {
            return KnowledgeFailures.Invalid(
                KnowledgeErrorCodes.PageBodyInvalid, "body", "內容不能有 NUL、換行與 Tab 以外的控制字元，或無效的字元。");
        }

        return ContainsPrivateKey(normalized)
            ? KnowledgeFailures.Invalid(KnowledgeErrorCodes.PageBodySecret, "body", "內容裡有私鑰。私鑰不能存在知識庫，請改寫密碼管理器裡的項目名稱。")
            : normalized;
    }

    /// <summary>標題與內文的問題一次回齊。<paramref name="title"/> 或 <paramref name="body"/> 是 null 表示不檢查那一項。</summary>
    public static Result<(string? Title, string? Body)> CheckContent(string? title, string? body, bool checkTitle, bool checkBody)
    {
        var problems = new List<FailureDetail>();
        string? checkedTitle = null;
        string? checkedBody = null;
        if (checkTitle)
        {
            var result = CheckName(
                title, KnowledgeRules.MaxPageTitleLength, "title", KnowledgeErrorCodes.PageTitleRequired, KnowledgeErrorCodes.PageTitleTooLong, "標題");
            if (result.Failure is { } failure)
            {
                problems.AddRange(failure.Details);
            }
            else
            {
                checkedTitle = result.Value;
            }
        }

        if (checkBody)
        {
            var result = CheckBody(body);
            if (result.Failure is { } failure)
            {
                problems.AddRange(failure.Details);
            }
            else
            {
                checkedBody = result.Value;
            }
        }

        return problems.Count > 0 ? KnowledgeFailures.Invalid(problems) : (checkedTitle, checkedBody);
    }

    /// <summary>
    /// 只認兩種，行內任何位置都算：「-----BEGIN …PRIVATE KEY-----」（含 PGP 的 PRIVATE KEY BLOCK），以及 PuTTY 的檔頭。
    /// 密碼字串猜不準，誤判的代價比漏掉高。
    /// </summary>
    internal static bool ContainsPrivateKey(string text) => PrivateKeyPattern.IsMatch(text) || PuttyPattern.IsMatch(text);

    /// <summary>落單的代理字元轉不成 UTF-8、NUL 存不進 PostgreSQL；其他控制字元出現在名稱裡只會造成顯示問題。</summary>
    internal static bool HasForbiddenCharacters(string text, bool allowLineBreaks)
    {
        for (var index = 0; index < text.Length; index++)
        {
            var c = text[index];
            if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                index++;
            }
            else if (char.IsSurrogate(c) || (char.IsControl(c) && !(allowLineBreaks && c is '\n' or '\r' or '\t')))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>名稱不能用的字元：上面那些，再加上看不見的格式字元（零寬空格、右到左覆寫這類），選單裡會出現看起來空白或順序顛倒的名稱。</summary>
    internal static bool HasUnsafeNameCharacters(string name) =>
        HasForbiddenCharacters(name, allowLineBreaks: false) || name.Any(c => char.GetUnicodeCategory(c) == UnicodeCategory.Format);

    /// <summary>
    /// \r\n 與單獨的 \r 換成 \n：網頁的文字框送出的是 \n，其他用戶端可能送 \r\n；不統一的話，內容相同也會被當成有改，長度也會多算。
    /// </summary>
    internal static string? NormalizeLineBreaks(string? text) =>
        text?.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    [GeneratedRegex(KnowledgeRules.SpaceKeyPattern, RegexOptions.CultureInvariant)]
    private static partial Regex KeyRegex();

    [GeneratedRegex("-----BEGIN (?:[A-Z0-9]+ )*PRIVATE KEY(?: BLOCK)?-----", RegexOptions.CultureInvariant)]
    private static partial Regex PrivateKeyRegex();

    [GeneratedRegex("PuTTY-User-Key-File-[0-9]+:", RegexOptions.CultureInvariant)]
    private static partial Regex PuttyRegex();
}
