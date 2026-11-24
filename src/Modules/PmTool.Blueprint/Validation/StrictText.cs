using System.Text.RegularExpressions;

namespace PmTool.Blueprint.Validation;

/// <summary>key、幣別、網址這類「整段都要合格」的文字檢查。</summary>
internal static class StrictText
{
    private static readonly Regex Key = new(BlueprintVocabulary.KeyPattern, RegexOptions.None);
    private static readonly Regex CurrencyCode = new(BlueprintVocabulary.CurrencyCodePattern, RegexOptions.None);

    public static bool IsKey(string text) => IsWholeMatch(Key, text);

    public static bool IsCurrencyCode(string text) => IsWholeMatch(CurrencyCode, text);

    public static bool IsAbsoluteHttpUrl(string text)
    {
        // Uri 會默默去掉前後的空白、把中間的空白改寫成 %20，檢查通過的網址就不是文件裡寫的那一個。
        if (text.Length == 0 || text.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            return false;
        }

        // 在 Unix 上 "/path" 也算絕對 URI（file 協定），所以一定要再看協定與主機。
        return Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && uri.Host.Length > 0;
    }

    private static bool IsWholeMatch(Regex regex, string text)
    {
        // .NET 的 $ 也會配到結尾換行的前面，"abc\n" 會通過 ^...$；配到的範圍必須正好是整個字串。
        var match = regex.Match(text);
        return match.Success && match.Index == 0 && match.Length == text.Length;
    }
}
