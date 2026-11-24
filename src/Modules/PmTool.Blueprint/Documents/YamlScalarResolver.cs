using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace PmTool.Blueprint.Documents;

/// <summary>
/// 依 YAML 1.2 core schema 決定純量的型別。只有沒加引號的純量要判斷；有引號的，以及 | 與 &gt; 區塊，一律是字串。
/// yes、no、on、off 是 YAML 1.1 的布林寫法，1.2 已經拿掉，這裡照 1.2 當字串。
/// </summary>
internal static partial class YamlScalarResolver
{
    public static JsonNode? Resolve(Scalar scalar) =>
        scalar.Style == ScalarStyle.Plain ? ResolvePlain(scalar.Value) : JsonValue.Create(scalar.Value);

    private static JsonNode? ResolvePlain(string text) => text switch
    {
        "" or "~" or "null" or "Null" or "NULL" => null,
        "true" or "True" or "TRUE" => JsonValue.Create(true),
        "false" or "False" or "FALSE" => JsonValue.Create(false),
        _ => ResolveNumber(text) ?? JsonValue.Create(text),
    };

    /// <summary>
    /// 不是數字，或是 JSON 表示不了的數字，回傳 null，由呼叫端當成字串。
    /// 整數放不進 long、浮點數算出來是無限大，都屬於後者；core schema 的 .inf 與 .nan 也刻意不認，
    /// 因為 JSON 沒有無限大與 NaN，放進樹裡會讓 ToJsonString 丟例外。
    /// </summary>
    private static JsonValue? ResolveNumber(string text)
    {
        if (DecimalInteger().IsMatch(text))
        {
            return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
                ? JsonValue.Create(value)
                : null;
        }

        if (OctalInteger().IsMatch(text))
        {
            return ParseOctal(text.AsSpan(2)) is { } octal ? JsonValue.Create(octal) : null;
        }

        if (HexInteger().IsMatch(text))
        {
            return ulong.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var hex)
                && hex <= long.MaxValue
                ? JsonValue.Create((long)hex)
                : null;
        }

        if (FloatingPoint().IsMatch(text))
        {
            var number = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            return double.IsFinite(number) ? JsonValue.Create(number) : null;
        }

        return null;
    }

    private static long? ParseOctal(ReadOnlySpan<char> digits)
    {
        const int bitsPerDigit = 3;
        long value = 0;
        foreach (var digit in digits)
        {
            // 再左移三位就會超過 long.MaxValue。
            if (value > long.MaxValue >> bitsPerDigit)
            {
                return null;
            }

            value = (value << bitsPerDigit) | (long)(digit - '0');
        }

        return value;
    }

    [GeneratedRegex(@"\A[-+]?[0-9]+\z")]
    private static partial Regex DecimalInteger();

    [GeneratedRegex(@"\A0o[0-7]+\z")]
    private static partial Regex OctalInteger();

    [GeneratedRegex(@"\A0x[0-9a-fA-F]+\z")]
    private static partial Regex HexInteger();

    [GeneratedRegex(@"\A[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?\z", RegexOptions.ExplicitCapture)]
    private static partial Regex FloatingPoint();
}
