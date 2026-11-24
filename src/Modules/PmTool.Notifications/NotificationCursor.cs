using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace PmTool.Notifications;

/// <summary>
/// 收件匣分頁的游標：上一頁最後一則的（時刻、編號）。對外是不透明的字串（網頁原樣送回來），
/// 裡面的格式之後可以換，所以只認帶版本字首的。
/// </summary>
internal readonly record struct NotificationCursor(DateTimeOffset CreatedAt, Guid Id)
{
    private const string Version = "n1";

    private const char Separator = '.';

    /// <summary>DateTime.MaxValue 的刻度數；超過的建不出時刻。</summary>
    private const long MaxTicks = 3_155_378_975_999_999_999L;

    public string Encode() =>
        Base64Url.EncodeToString(Encoding.ASCII.GetBytes(
            $"{Version}{Separator}{CreatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}{Separator}{Id:N}"));

    public static bool TryDecode(string? text, out NotificationCursor cursor)
    {
        cursor = default;
        if (string.IsNullOrEmpty(text) || !Base64Url.IsValid(text))
        {
            return false;
        }

        var parts = Encoding.ASCII.GetString(Base64Url.DecodeFromChars(text)).Split(Separator);
        if (parts is not [Version, var ticksText, var idText]
            || !long.TryParse(ticksText, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks is < 0 or > MaxTicks
            || !Guid.TryParseExact(idText, "N", out var id))
        {
            return false;
        }

        cursor = new NotificationCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }
}
