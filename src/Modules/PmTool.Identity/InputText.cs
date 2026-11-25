using System.Buffers;
using System.Globalization;
using System.Text;

namespace PmTool.Identity;

internal static class InputText
{
    /// <summary>
    /// 有沒有存不進資料庫、或不該出現在名稱與 Email 裡的字元。NUL 存不進也查不了 PostgreSQL 的 text；
    /// 落單的代理字元轉不成 UTF-8，Unicode 正規化也會丟例外；其他控制字元（包含換行）只會造成顯示問題。
    /// </summary>
    public static bool HasForbiddenCharacters(string text)
    {
        var rest = text.AsSpan();
        while (!rest.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(rest, out var rune, out var consumed) != OperationStatus.Done || Rune.IsControl(rune))
            {
                return true;
            }

            rest = rest[consumed..];
        }

        return false;
    }

    /// <summary>
    /// 填充字：不是 Format 類，畫面上卻是空白，可以拿來做出看起來一樣的名稱。
    /// 高棉文的固有母音（U+17B4、U+17B5）單獨出現時顯示不出來；U+180E（蒙古文母音分隔符）舊版 Unicode 當空白，
    /// 新版改成 Format，明列在這裡讓兩種版本的執行環境結果一致。
    /// </summary>
    private static readonly HashSet<int> FillerCodePoints = [0x115F, 0x1160, 0x3164, 0xFFA0, 0x2800, 0x034F, 0x17B4, 0x17B5, 0x180E];

    /// <summary>
    /// 變體選擇符：附在前一個字元後面、改變它的字形，本身看不見。U+FE0F（emoji 的圖形樣式）要留給 emoji，不算在內。
    /// </summary>
    private static bool IsVariationSelector(Rune rune) =>
        rune.Value is >= 0xFE00 and <= 0xFE0E or >= 0xE0100 and <= 0xE01EF
            or >= 0x180B and <= 0x180D or 0x180F; // 蒙古文的自由變體選擇符

    /// <summary>
    /// 看不見的格式字元：雙向控制與零寬字元（Format）、行與段落分隔、填充字、變體選擇符。
    /// 名稱欄位（人、電腦、權杖、OAuth 應用）和 Email 共用同一份定義，兩邊才不會一邊擋、一邊放。
    /// </summary>
    public static bool IsInvisibleFormatting(Rune rune) =>
        Rune.GetUnicodeCategory(rune) is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
        || FillerCodePoints.Contains(rune.Value)
        || IsVariationSelector(rune);

    /// <summary>
    /// 看不見或會改變旁邊字元顯示的字元：雙向控制與零寬字元（Format）、行與段落分隔、填充字，
    /// 變體選擇符，以及開頭的組合字元（它會黏到前一個欄位或標籤上）。用在 Email、顯示名稱、電腦名稱與權杖名稱。
    /// </summary>
    public static bool HasInvisibleCharacters(string text)
    {
        var first = true;
        foreach (var rune in text.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (IsInvisibleFormatting(rune)
                || (first && category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark))
            {
                return true;
            }

            first = false;
        }

        return false;
    }
}
