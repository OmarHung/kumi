using System.Globalization;

namespace PmTool.Blueprint.Schema;

/// <summary>組 JSON Pointer（RFC 6901）。根節點是空字串。</summary>
internal static class JsonPointer
{
    public static string Append(string pointer, string propertyName) => pointer + "/" + Escape(propertyName);

    public static string Append(string pointer, int index) =>
        pointer + "/" + index.ToString(CultureInfo.InvariantCulture);

    // ~ 要先換：先換 / 的話，產生的 ~1 會被當成 ~ 再換一次。
    public static string Escape(string segment) =>
        segment.AsSpan().IndexOfAny('~', '/') < 0
            ? segment
            : segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
