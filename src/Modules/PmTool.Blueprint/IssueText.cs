using System.Globalization;

namespace PmTool.Blueprint;

/// <summary>
/// 把文件裡的文字放進問題訊息之前，一律先經過這裡。
/// key 與名稱來自使用者或語言模型，長度只受整份文件的上限限制；原樣放進每一則訊息，
/// 一份文件就能換到比它大很多倍的回應。
/// </summary>
internal static class IssueText
{
    /// <summary>合法的 key 最長 40 個字元，這個長度足夠完整顯示，超過的才會被截斷。</summary>
    public const int MaxQuotedLength = 64;

    private const string Open = "「";
    private const string Close = "」";
    private const string CutMark = "…";

    // 寫成數值，原始檔裡才不會出現替代字元本身。
    private const char Replacement = (char)0xFFFD;

    /// <summary>用名稱稱呼一個資源：訊息給看畫面的人讀，畫面上看到的是名稱。名稱是空的才退回 key。</summary>
    public static string QuoteName(string? name, string key) => Quote(string.IsNullOrWhiteSpace(name) ? key : name);

    public static string Quote(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Open + Close;
        }

        if (text.Length <= MaxQuotedLength)
        {
            return Open + Neutralize(text) + Close;
        }

        // 切點落在代理對中間會留下半個字元，所以整對一起捨棄。
        var cut = char.IsHighSurrogate(text[MaxQuotedLength - 1]) ? MaxQuotedLength - 1 : MaxQuotedLength;
        return Open + Neutralize(text[..cut]) + CutMark + Close;
    }

    /// <summary>
    /// 訊息會被印在終端機或畫面上。跳脫字元能改寫終端機的內容，方向控制字元能讓文字倒著顯示，
    /// 換行能讓一則訊息看起來像好幾則；這些字元一律換成替代字元。截斷之後才做，所以最多處理 MaxQuotedLength 個字元。
    /// </summary>
    private static string Neutralize(string text) =>
        text.Any(CanRewriteDisplay)
            ? string.Create(text.Length, text, static (target, source) =>
            {
                for (var index = 0; index < source.Length; index++)
                {
                    target[index] = CanRewriteDisplay(source[index]) ? Replacement : source[index];
                }
            })
            : text;

    private static bool CanRewriteDisplay(char character) =>
        char.GetUnicodeCategory(character) is UnicodeCategory.Control
            or UnicodeCategory.Format
            or UnicodeCategory.LineSeparator
            or UnicodeCategory.ParagraphSeparator;
}
