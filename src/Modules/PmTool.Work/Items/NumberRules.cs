using System.Globalization;
using System.Text.Json;

namespace PmTool.Work.Items;

/// <summary>
/// 數字類的值（估計、數字欄位、金額欄位）的範圍與正規化，範圍見 <see cref="WorkRules.MaxNumberMagnitude"/>。
/// 通過的值去掉多餘的 0 與指數寫法（1e2 變成 100、2.50 變成 2.5），存進去的、回給呼叫端的、之後讀回來的都一樣。
/// </summary>
internal static class NumberRules
{
    /// <summary>放進訊息的上限，例如 99,999,999,999.9999。</summary>
    public static readonly string MaxText = WorkRules.MaxNumberMagnitude.ToString(
        "N" + WorkRules.MaxNumberDecimalPlaces.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    // 整數部分超過這麼多位一定超過上限，不必組出 decimal；也讓組出來的位數在 decimal 能精確表示的範圍內。
    private static readonly int MaxIntegerDigits =
        decimal.Truncate(WorkRules.MaxNumberMagnitude).ToString(CultureInfo.InvariantCulture).Length;

    private static readonly char[] ExponentMarks = ['e', 'E'];

    /// <param name="number">ValueKind 是 Number 的 JSON 值。</param>
    public static bool TryNormalize(JsonElement number, out decimal value) => TryNormalize(number.GetRawText(), out value);

    /// <summary>decimal 的寫法不會用指數，但可能帶著尾端的 0（3.50 與 3.5 相等，寫出來卻不同）。</summary>
    public static bool TryNormalize(decimal number, out decimal value) =>
        TryNormalize(number.ToString(CultureInfo.InvariantCulture), out value);

    /// <summary>
    /// 看數字的寫法判斷，不先轉成 decimal 或 double：太小的數轉完會默默變成 0、位數太多會被四捨五入，
    /// 轉完就看不出原本超出了範圍；資料庫的 numeric 又存不下太大或太小的指數。
    /// </summary>
    private static bool TryNormalize(string text, out decimal value)
    {
        value = 0;
        if (!TrySplit(text, out var negative, out var significant, out var power))
        {
            return false;
        }

        if (significant.Length == 0)
        {
            return true;
        }

        if (-power > WorkRules.MaxNumberDecimalPlaces || significant.Length + power > MaxIntegerDigits)
        {
            return false;
        }

        var magnitude = decimal.Parse(Positional(significant, (int)power), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        value = negative ? -magnitude : magnitude;
        return magnitude <= WorkRules.MaxNumberMagnitude;
    }

    /// <summary>
    /// 把 JSON 數字的寫法拆成「有效數字 × 10 的 power 次方」。有效數字去掉頭尾的 0，值是 0 時是空字串。
    /// 指數超出 int 的數字不可能在範圍內，直接當成不通過。
    /// </summary>
    private static bool TrySplit(string text, out bool negative, out string significant, out long power)
    {
        negative = text.StartsWith('-');
        significant = string.Empty;
        power = 0;
        var unsigned = negative ? text[1..] : text;
        var mark = unsigned.IndexOfAny(ExponentMarks);
        var exponent = 0;
        if (mark >= 0 && !int.TryParse(unsigned.AsSpan(mark + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent))
        {
            return false;
        }

        var mantissa = mark < 0 ? unsigned : unsigned[..mark];
        var dot = mantissa.IndexOf('.', StringComparison.Ordinal);
        var digits = (dot < 0 ? mantissa : mantissa.Remove(dot, 1)).TrimStart('0');
        significant = digits.TrimEnd('0');
        var fractionLength = dot < 0 ? 0 : mantissa.Length - dot - 1;
        power = (long)exponent - fractionLength + (digits.Length - significant.Length);
        return true;
    }

    /// <summary>有效數字與次方寫回一般的寫法，例如 ("12345", -4) 是 1.2345、("5", 2) 是 500。</summary>
    private static string Positional(string significant, int power)
    {
        if (power >= 0)
        {
            return significant + new string('0', power);
        }

        // 小數點前面至少留一位，0.0005 這種才寫得出來。
        var padded = significant.PadLeft(1 - power, '0');
        return padded.Insert(padded.Length + power, ".");
    }
}
