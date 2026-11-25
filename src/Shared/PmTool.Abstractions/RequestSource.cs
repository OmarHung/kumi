namespace PmTool.Abstractions;

/// <summary>
/// 目前這個請求的來源。模組不碰 HTTP，稽核與通知要記「從哪裡」時問它，
/// 位址就不必一路當參數傳進每一個方法。
/// </summary>
public interface IRequestSource
{
    /// <summary>沒有請求時（背景工作、遷移、測試）是 null。</summary>
    RequestSource? Current { get; }
}

/// <param name="Address">用戶端的位址，已經走過轉送標頭；IPv4-mapped 的位址轉回 IPv4。</param>
/// <param name="UserAgent">已經清理過、截到 <see cref="RequestSourceText.MaxUserAgentLength"/> 個字。</param>
public sealed record RequestSource(string? Address, string? UserAgent);

/// <summary>沒有請求可看的環境用它：永遠回 null。</summary>
public sealed class NullRequestSource : IRequestSource
{
    public static readonly NullRequestSource Instance = new();

    private NullRequestSource()
    {
    }

    public RequestSource? Current => null;
}

/// <summary>
/// 來源的文字要進資料庫：PostgreSQL 不收 NUL，也不收落單的代理字元；
/// 控制字元與零寬這類 Format 字元會讓紀錄在畫面上看起來和實際不同，所以一律換成空白。
/// </summary>
public static class RequestSourceText
{
    public const int MaxAddressLength = 64;

    public const int MaxUserAgentLength = 256;

    /// <summary>
    /// 清理並截斷；清完是空的回 null。截斷不會把一對代理字元切成兩半。
    /// </summary>
    public static string? Clean(string? value, int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var length = Math.Min(value.Length, maxLength);
        if (length < value.Length && char.IsHighSurrogate(value[length - 1]))
        {
            length--;
        }

        var cleaned = string.Create(length, value, static (span, source) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = IsUnsafe(source, i) ? ' ' : source[i];
            }
        }).Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }

    private static bool IsUnsafe(string text, int index)
    {
        var c = text[index];
        if (char.IsHighSurrogate(c))
        {
            return index + 1 >= text.Length || !char.IsLowSurrogate(text[index + 1]);
        }

        if (char.IsLowSurrogate(c))
        {
            return index == 0 || !char.IsHighSurrogate(text[index - 1]);
        }

        return char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.Control
            or System.Globalization.UnicodeCategory.Format
            or System.Globalization.UnicodeCategory.LineSeparator
            or System.Globalization.UnicodeCategory.ParagraphSeparator;
    }
}
