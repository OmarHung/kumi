using System.Net;
using System.Net.Sockets;
using PmTool.Abstractions;

namespace PmTool.Api.Security;

/// <summary>
/// 身分模組問「這個請求從哪裡來」時的答案。位址已經走過轉送標頭的處理（<c>UseForwardedHeaders</c>），
/// 所以是用戶端的，不是代理的。型別是 public 的，API 的測試專案直接測它。
/// </summary>
public sealed class HttpRequestSource(IHttpContextAccessor accessor) : IRequestSource
{
    public RequestSource? Current => accessor.HttpContext is { } context ? From(context) : null;

    private static RequestSource From(HttpContext context) => new(
        RequestSourceText.Clean(AddressOf(context.Connection.RemoteIpAddress), RequestSourceText.MaxAddressLength),
        RequestSourceText.Clean(context.Request.Headers.UserAgent.ToString(), RequestSourceText.MaxUserAgentLength));

    /// <summary>雙堆疊的監聽會把 IPv4 的用戶端報成 ::ffff:a.b.c.d；記下來的要和 IPv4 的寫法一樣，才查得到、比得起來。</summary>
    private static string? AddressOf(IPAddress? address) =>
        address is null
            ? null
            : (address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
}
