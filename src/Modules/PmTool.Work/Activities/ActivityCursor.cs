using System.Buffers.Text;
using System.Globalization;
using System.Text;
using PmTool.Work.Items;
using PmTool.Work.Persistence;

namespace PmTool.Work.Activities;

/// <summary>上一頁最後一筆活動的位置：時間與編號。下一頁從它之後接著讀。</summary>
internal readonly record struct ActivityPosition(DateTimeOffset At, Guid Id);

/// <summary>
/// 活動的分頁游標，以鍵為準而不是位移量：翻頁之間有新的活動進來，也不會漏掉或重複。
/// 包一層 base64url，呼叫端把它當成不透明的字串，放進網址的查詢參數也不必再跳脫。
/// </summary>
internal static class ActivityCursor
{
    private const string Prefix = "a:";
    private const char Separator = ':';
    private const string IdFormat = "N";

    // 合法的游標只有七十幾個字元；先看長度，太長的不解碼。
    private const int MaxLength = 96;

    public static string Encode(ItemActivityEntity last) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{Prefix}{last.At.UtcTicks}{Separator}{last.Id.ToString(IdFormat)}")));

    /// <param name="position">第一頁（沒有游標）是 null。</param>
    public static bool TryDecode(string? cursor, out ActivityPosition? position)
    {
        position = null;
        if (cursor is null)
        {
            return true;
        }

        if (cursor.Length is 0 or > MaxLength || !Base64Url.IsValid(cursor))
        {
            return false;
        }

        var text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
        if (!text.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = text.AsSpan(Prefix.Length);
        var separator = rest.IndexOf(Separator);
        if (separator < 0 || !TryParseTicks(rest[..separator], out var ticks) || !TryParseId(rest[(separator + 1)..], out var id))
        {
            return false;
        }

        position = new ActivityPosition(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }

    // 數字解析會忽略尾端的 NUL，先確認全是數字，同一個位置只有一種寫法。
    private static bool TryParseTicks(ReadOnlySpan<char> text, out long ticks)
    {
        ticks = 0;
        return ItemKeys.IsAllAsciiDigits(text)
            && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ticks)
            && ticks <= DateTimeOffset.MaxValue.UtcTicks;
    }

    private static bool TryParseId(ReadOnlySpan<char> text, out Guid id) =>
        Guid.TryParseExact(text, IdFormat, out id) && text.SequenceEqual(id.ToString(IdFormat));
}
