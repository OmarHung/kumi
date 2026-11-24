using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace PmTool.Work.Items;

/// <summary>
/// 分頁游標：包起來的位移量。包一層是為了讓呼叫端把它當成不透明的字串，之後換成以鍵為準的游標也不影響合約。
/// 用 base64url，放進網址的查詢參數不必再跳脫。
/// </summary>
internal static class ItemCursor
{
    private const string Prefix = "o:";

    // 合法的游標只有十幾個字元；先看長度，太長的不解碼。
    private const int MaxLength = 32;

    public static string Encode(int offset) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(Prefix + offset.ToString(CultureInfo.InvariantCulture)));

    public static bool TryDecode(string? cursor, out int offset)
    {
        offset = 0;
        if (cursor is null)
        {
            return true;
        }

        if (cursor.Length is 0 or > MaxLength || !Base64Url.IsValid(cursor))
        {
            return false;
        }

        // 數字解析會忽略尾端的 NUL，先確認位移量全是數字，同一個位置只有一種寫法。
        var text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
        return text.StartsWith(Prefix, StringComparison.Ordinal)
            && ItemKeys.IsAllAsciiDigits(text.AsSpan(Prefix.Length))
            && int.TryParse(text.AsSpan(Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out offset);
    }
}
