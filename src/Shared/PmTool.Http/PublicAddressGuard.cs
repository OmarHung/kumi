using System.Net;
using System.Net.Sockets;

namespace PmTool.Http;

/// <summary>連線的當下發現位址是內網或本機。呼叫端認得它，說成「這個部署不允許連」。</summary>
public sealed class BlockedAddressException(string host)
    : HttpRequestException($"{host} 是內網或本機的位址。")
{
    public string Host { get; } = host;
}

/// <summary>
/// 使用者填的網址會被伺服器拿去連（AI 的相容端點、OAuth 用戶端的 Client ID Metadata Document）：預設不准連私有網段。
/// 只檢查網址字串擋不住 DNS rebinding，所以連線的當下檢查解析出來的位址（<see cref="CreateHandler"/>）。
/// </summary>
public static class PublicAddressGuard
{
    /// <summary>loopback、私有位址、100.64/10、link-local（含雲端的中繼資料服務）、未指定位址、多播，一律擋。</summary>
    public static bool IsBlocked(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6
            ? IsBlockedV6(address)
            : IsBlockedV4(address.GetAddressBytes());
    }

    /// <summary>網址的主機是 IP 字面值而且被擋時回 true；主機名稱要等連線時才知道。</summary>
    public static bool IsBlockedLiteral(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var host = uri.Host.Trim('[', ']');
        return IPAddress.TryParse(host, out var address) && IsBlocked(address);
    }

    /// <summary>不跟隨轉址、不走系統的 proxy（走 proxy 時連的是 proxy，檢查就沒有意義）。</summary>
    public static SocketsHttpHandler CreateHandler(bool allowPrivateEndpoints, bool guarded)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        if (guarded && !allowPrivateEndpoints)
        {
            handler.ConnectCallback = ConnectGuardedAsync;
        }

        return handler;
    }

    public static async ValueTask<Stream> ConnectGuardedAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);

        // 只要有一個解析結果是被擋的就整個不連：不能讓對方挑一個通過檢查的、再連到另一個。
        if (addresses.Length == 0 || addresses.Any(IsBlocked))
        {
            throw new BlockedAddressException(host);
        }

        Exception? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException exception)
            {
                socket.Dispose();
                last = exception;
            }
            catch
            {
                // 被取消或其他例外：socket 還沒交給 NetworkStream，不放掉就漏掉一個連線。
                socket.Dispose();
                throw;
            }
        }

        throw new HttpRequestException("連不上。", last);
    }

    private static bool IsBlockedV4(byte[] b) =>
        b[0] == 0
        || b[0] == 10
        || b[0] == 127
        || (b[0] == 100 && b[1] is >= 64 and <= 127)
        || (b[0] == 169 && b[1] == 254)
        || (b[0] == 172 && b[1] is >= 16 and <= 31)
        || (b[0] == 192 && b[1] == 168)
        // 保留給協定、文件與基準測試的網段：不會是真的供應商。
        || (b[0] == 192 && b[1] == 0 && b[2] is 0 or 2)
        || (b[0] == 198 && b[1] is 18 or 19)
        || (b[0] == 198 && b[1] == 51 && b[2] == 100)
        || (b[0] == 203 && b[1] == 0 && b[2] == 113)
        || b[0] >= 224;

    private static bool IsBlockedV6(IPAddress address)
    {
        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal || address.IsIPv6Multicast)
        {
            return true;
        }

        // 內嵌 IPv4 的形式（NAT64、6to4、舊式的相容位址）：閘道會把它轉成裡面那個 IPv4，所以要拿裡面的位址再檢查一次，
        // 不然 64:ff9b::a9fe:a9fe 就能繞過 169.254.169.254 的封鎖。Teredo 與文件用的網段不可能是供應商，整段擋。
        var b = address.GetAddressBytes();
        if (b[0] == 0x20 && b[1] == 0x01 && ((b[2] == 0 && b[3] == 0) || (b[2] == 0x0d && b[3] == 0xb8)))
        {
            return true;
        }

        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b)
        {
            // 64:ff9b::/96 取最後四個位元組；64:ff9b:1::/48 是本地用的 NAT64，整段擋。
            return b[4..12].Any(x => x != 0) || IsBlockedV4(b[12..16]);
        }

        if (b[0] == 0x20 && b[1] == 0x02)
        {
            return IsBlockedV4(b[2..6]);
        }

        return b[..12].All(x => x == 0) && IsBlockedV4(b[12..16]);
    }
}
