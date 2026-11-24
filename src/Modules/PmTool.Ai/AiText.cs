namespace PmTool.Ai;

/// <summary>文字的小工具：存進資料庫與顯示的字串都不能留下半個字（孤立的代理）。</summary>
internal static class AiText
{
    /// <summary>
    /// 取前 <paramref name="maxLength"/> 個字元；切點落在代理對（emoji 這類）中間時往前退一格。
    /// 孤立的高代理不是合法的 UTF-8，Npgsql 寫進 text 欄位會丟例外，整則回覆就存不下來。
    /// </summary>
    public static string Cut(string text, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(maxLength);
        if (text.Length <= maxLength)
        {
            return text;
        }

        var end = maxLength > 0 && char.IsHighSurrogate(text[maxLength - 1]) ? maxLength - 1 : maxLength;
        return text[..end];
    }

    /// <summary>總長不超過 <paramref name="maxLength"/>，太長就在結尾加「…」。</summary>
    public static string Ellipsize(string text, int maxLength) =>
        text.Length <= maxLength ? text : Cut(text, maxLength - 1) + "…";
}
