using PmTool.Work.Persistence;

namespace PmTool.Work.History;

/// <summary>
/// 一次匯入的請求裡，活動編號的產生器。時間軸依（時間、編號）排序，而來源（monday）的時間只到秒，
/// 同一秒的「建立」與留言、兩則留言要照匯入的順序排。<see cref="Guid.CreateVersion7(DateTimeOffset)"/> 同一毫秒內的編號是隨機的，
/// 不保證遞增；這裡把 UUIDv7 的 12 位元計數欄位（rand_a）換成同一毫秒內的流水號，同一時間的編號就嚴格遞增。
/// 「建立」用 0 到 2047，其他活動用 2048 到 4095：項目與它的留言分在不同的請求匯入（項目先），
/// 同一時間的建立活動仍然排在留言前面。流水號用完就把編號的毫秒往後挪（只影響這幾筆同時間的活動之間的先後）。
/// </summary>
internal sealed class HistoryActivityIds
{
    private const int CreatedStart = 0;
    private const int OthersStart = 2048;
    private const int RangeSize = 2048;
    private const int VersionAndHighBits = 0x70;
    private const int HighBitsMask = 0x0F;
    private const int ByteMask = 0xFF;
    private const int ByteBits = 8;

    private readonly Dictionary<(long Millisecond, bool IsCreated), int> _next = [];

    /// <summary>換上這個請求裡下一個同時間遞增的編號。</summary>
    public ItemActivityEntity Assign(ItemActivityEntity activity)
    {
        var isCreated = activity.Kind == "created";
        var start = isCreated ? CreatedStart : OthersStart;
        var millisecond = activity.At.ToUnixTimeMilliseconds();
        while (true)
        {
            var counter = _next.GetValueOrDefault((millisecond, isCreated), start);
            if (counter < start + RangeSize)
            {
                _next[(millisecond, isCreated)] = counter + 1;
                return activity with { Id = Build(millisecond, counter) };
            }

            millisecond++;
        }
    }

    private static Guid Build(long millisecond, int counter)
    {
        var bytes = Guid.CreateVersion7(DateTimeOffset.FromUnixTimeMilliseconds(millisecond)).ToByteArray(bigEndian: true);
        bytes[6] = (byte)(VersionAndHighBits | ((counter >> ByteBits) & HighBitsMask));
        bytes[7] = (byte)(counter & ByteMask);
        return new Guid(bytes, bigEndian: true);
    }
}
