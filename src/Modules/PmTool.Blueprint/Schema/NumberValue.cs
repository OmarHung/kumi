using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PmTool.Blueprint.Schema;

/// <summary>
/// 一個有限的 JSON 數值。沒有小數部分而且落在 Int64 範圍內的數一律存成整數，其他存成 double。
/// 這樣 12、12.0、1.2e1，或背後是 long、double 的 12，都會得到相等的值。
/// </summary>
/// <remarks>
/// integer 的定義是「JSON 數字、沒有小數部分、落在 Int64 範圍內」：Schema 裡的 integer 會綁到模型的 long，
/// 通過結構檢查的值必須保證綁得進去。
/// </remarks>
internal readonly record struct NumberValue
{
    // 2^63 用 double 可以精確表示，而 long.MaxValue 轉成 double 會進位成 2^63，所以上限只能用「小於」來比。
    private const double TwoToThe63 = 9223372036854775808.0;

    // long.MinValue 的絕對值，比 long.MaxValue 多 1。
    private const ulong Int64MinMagnitude = 9223372036854775808;

    // Int64 最多 19 位十進位數。
    private const int MaxInt64Digits = 19;

    // 文件最長只有二十幾萬個字元，數字的位數不可能比這多；指數的絕對值超過這個數，小數點一定落在所有數字之外很遠的地方。
    private const int MaxExponentWorthChecking = 10_000_000;

    private NumberValue(bool isInteger, long integer, double real)
    {
        IsInteger = isInteger;
        Integer = integer;
        Real = real;
    }

    public bool IsInteger { get; }

    public long Integer { get; }

    public double Real { get; }

    public static NumberValue FromInt64(long value) => new(isInteger: true, value, real: 0);

    /// <summary>NaN 與無限大不是 JSON 數值，讀不出來。</summary>
    public static bool TryFromDouble(double value, out NumberValue number)
    {
        if (!double.IsFinite(value))
        {
            number = default;
            return false;
        }

        number = Math.Floor(value) == value && value >= -TwoToThe63 && value < TwoToThe63
            ? FromInt64((long)value)
            : new NumberValue(isInteger: false, integer: 0, value);
        return true;
    }

    public static bool TryRead(JsonElement element, out NumberValue number)
    {
        number = default;
        if (element.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        if (element.TryGetInt64(out var integer))
        {
            number = FromInt64(integer);
            return true;
        }

        // 12.0、1.2e1 這種寫法 TryGetInt64 讀不出來，要看原始的文字才能精確判斷。
        if (TryReadExactInt64(element.GetRawText(), out integer))
        {
            number = FromInt64(integer);
            return true;
        }

        // 剩下的是有小數或超出 Int64 的數，只需要知道它是不是有限值。1e400 會讀成無限大，要擋下來。
        if (!element.TryGetDouble(out var real) || !double.IsFinite(real))
        {
            return false;
        }

        number = new NumberValue(isInteger: false, integer: 0, real);
        return true;
    }

    /// <summary>
    /// 依 JSON 數字的原始文字判斷它是不是落在 Int64 範圍內的整數值。不經過 decimal 或 double：兩者的位數有限，
    /// 0.00000000000000000000000000001 會被進位成 0，9223372036854775806.99999999999 會被進位成整數。
    /// </summary>
    /// <remarks>
    /// 把整數部分與小數部分的數字接成一串，小數點依指數移到第 pointPosition 位之後：
    /// 小數點右邊不能有不是 0 的數字，左邊去掉開頭的 0 之後最多 19 位，再確認沒有超出範圍。
    /// 傳進來的一定是合法的 JSON 數字（已經由 JSON 解析器讀過）。
    /// </remarks>
    internal static bool TryReadExactInt64(ReadOnlySpan<char> raw, out long value)
    {
        value = 0;
        var negative = raw[0] == '-';
        var unsigned = negative ? raw[1..] : raw;
        var exponentAt = unsigned.IndexOfAny('e', 'E');
        var mantissa = exponentAt < 0 ? unsigned : unsigned[..exponentAt];
        var pointAt = mantissa.IndexOf('.');
        var whole = pointAt < 0 ? mantissa : mantissa[..pointAt];
        var fraction = pointAt < 0 ? [] : mantissa[(pointAt + 1)..];

        var first = IndexOfFirstNonZeroDigit(whole, fraction);
        if (first < 0)
        {
            // 0、-0.0、0e999 都是 0，指數是多少都一樣。
            return true;
        }

        if (!TryReadExponent(exponentAt < 0 ? [] : unsigned[(exponentAt + 1)..], out var exponent))
        {
            return false;
        }

        var pointPosition = whole.Length + exponent;
        var last = IndexOfLastNonZeroDigit(whole, fraction);
        if (last >= pointPosition || pointPosition - first > MaxInt64Digits)
        {
            return false;
        }

        ulong magnitude = 0;
        for (var index = first; index < pointPosition; index++)
        {
            // 超過原本位數的部分是指數補上的 0。19 位十進位數放得進 ulong，不會溢位。
            var digit = index < whole.Length + fraction.Length ? DigitAt(whole, fraction, index) : 0;
            magnitude = (magnitude * 10) + (ulong)digit;
        }

        if (magnitude > (negative ? Int64MinMagnitude : (ulong)long.MaxValue))
        {
            return false;
        }

        value = negative ? unchecked(-(long)magnitude) : (long)magnitude;
        return true;
    }

    /// <summary>
    /// JsonNode.Parse 的值背後是 JsonElement；讀 YAML 建出來的值背後是 long 或 double。
    /// CLR 型別的 TryGetValue 只有型別完全相同才會成功（背後是 int 的值拿不出 long），所以逐一試。
    /// </summary>
    public static bool TryRead(JsonValue value, out NumberValue number)
    {
        if (value.TryGetValue<JsonElement>(out var element))
        {
            return TryRead(element, out number);
        }

        if (value.TryGetValue<long>(out var integer))
        {
            number = FromInt64(integer);
            return true;
        }

        if (value.TryGetValue<double>(out var real))
        {
            return TryFromDouble(real, out number);
        }

        // float 的 NaN 寫不成 JSON 文字，不能交給下面的通用做法。
        if (value.TryGetValue<float>(out var single))
        {
            return TryFromDouble(single, out number);
        }

        return TryRead(JsonElements.From(value), out number);
    }

    /// <summary>
    /// 沒寫指數就是 0。對不是 0 的數來說，指數大到超過 <see cref="MaxExponentWorthChecking"/> 就一定超出範圍或帶小數，
    /// 直接回傳 false；這也讓呼叫端把指數和位數相加時不會溢位。
    /// </summary>
    private static bool TryReadExponent(ReadOnlySpan<char> text, out int exponent)
    {
        exponent = 0;
        return text.IsEmpty
            || (int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent)
                && Math.Abs((long)exponent) <= MaxExponentWorthChecking);
    }

    private static int DigitAt(ReadOnlySpan<char> whole, ReadOnlySpan<char> fraction, int index) =>
        (index < whole.Length ? whole[index] : fraction[index - whole.Length]) - '0';

    private static int IndexOfFirstNonZeroDigit(ReadOnlySpan<char> whole, ReadOnlySpan<char> fraction)
    {
        var inWhole = whole.IndexOfAnyExcept('0');
        if (inWhole >= 0)
        {
            return inWhole;
        }

        var inFraction = fraction.IndexOfAnyExcept('0');
        return inFraction < 0 ? -1 : whole.Length + inFraction;
    }

    private static int IndexOfLastNonZeroDigit(ReadOnlySpan<char> whole, ReadOnlySpan<char> fraction)
    {
        var inFraction = fraction.LastIndexOfAnyExcept('0');
        return inFraction >= 0 ? whole.Length + inFraction : whole.LastIndexOfAnyExcept('0');
    }

    public override string ToString() =>
        IsInteger
            ? Integer.ToString(CultureInfo.InvariantCulture)
            : Real.ToString("R", CultureInfo.InvariantCulture);
}
