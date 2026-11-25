using System.Net;
using PmTool.Api.RateLimiting;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 瀏覽器登入的來源鍵：按「允許」的瀏覽器與兌換的 kumi 要從同一個網路位址連到伺服器，碼才換得到。
/// 同一台電腦上瀏覽器可能走 <c>::1</c>、kumi 走 <c>127.0.0.1</c>，所以 loopback 一律算同一個來源；
/// 其他沿用限流的算法（IPv4 整個位址、IPv6 取 /64）。
/// 沒有遠端位址時回空字串，身分模組把它當成不合格的來源（核准與兌換都拒絕），不會因為兩邊都沒有位址就比對成功。
/// 來源取自連線的遠端位址：放在反向代理後面時要設好轉送標頭（<c>ForwardedHeaders</c>），不然所有人的來源都是代理的位址。
/// </summary>
public static class CliLoginOrigin
{
    public const string Loopback = "loopback";

    public static string KeyOf(IPAddress? address)
    {
        if (address is not null && IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address))
        {
            return Loopback;
        }

        return address is null ? string.Empty : ApiRateLimiting.AddressKeyOf(address);
    }
}
