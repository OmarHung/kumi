using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace PmTool.Identity.OAuth;

/// <summary>導回網址的規定（<c>internal/HANDOFF-mcp.md</c> 第 2 節）與組出導回網址。</summary>
internal static class OAuthRedirects
{
    /// <summary>
    /// 合規的導回網址：絕對網址、沒有帳密與片段；https，或指到這台電腦的 http（RFC 8252 7.3）。
    /// 自訂的 scheme（<c>myapp://</c>）不收：規格要的是 https 或本機。
    /// </summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        if (!OAuthText.IsUsable(value, OAuthRules.MaxRedirectUriLength) || value.Contains('#', StringComparison.Ordinal))
        {
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) || parsed.UserInfo.Length > 0 || parsed.Host.Length == 0)
        {
            return false;
        }

        var allowed = parsed.Scheme == Uri.UriSchemeHttps || (parsed.Scheme == Uri.UriSchemeHttp && IsLoopback(parsed));
        uri = allowed ? parsed : null;
        return allowed;
    }

    /// <summary>127.0.0.1、::1、localhost（RFC 8252 7.3 的三種本機寫法）。</summary>
    public static bool IsLoopback(Uri uri)
    {
        var host = uri.Host.Trim('[', ']');
        return host is "127.0.0.1" or "::1" || host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>沒有經過驗證的用戶端（動態註冊的）被擋下導回外部網址時的說明。</summary>
    public const string UnverifiedClientMessage = "沒有經過驗證的應用只能導回這台電腦上的程式（127.0.0.1、[::1]、localhost）。";

    /// <summary>合規、而且指到這台電腦。</summary>
    public static bool IsLoopbackRedirect(string? value) => TryParse(value, out var uri) && IsLoopback(uri);

    /// <summary>
    /// 請求的導回網址是不是登記的其中一個。本機的不看連接埠（原生程式每次開的埠不同）；
    /// 其他的要一個字都不差，不做任何正規化，網址的比對寬鬆一點就是開放轉址。
    /// </summary>
    public static bool Matches(IEnumerable<string> registered, Uri requested)
    {
        var isLoopback = IsLoopback(requested);
        foreach (var candidate in registered)
        {
            if (string.Equals(candidate, requested.OriginalString, StringComparison.Ordinal))
            {
                return true;
            }

            if (isLoopback && Uri.TryCreate(candidate, UriKind.Absolute, out var other) && IsLoopback(other)
                && LoopbackKey(other) == LoopbackKey(requested))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 主機與埠，同意頁用來告訴使用者授權之後會到哪裡。主機一律用 punycode（<c>xn--…</c>）：
    /// 西里爾字母的 а 與拉丁字母的 a 在畫面上一模一樣，顯示 Unicode 形式的話，使用者看不出來是假的網域。
    /// </summary>
    public static string HostOf(Uri uri)
    {
        // IdnHost 的 IPv6 位址沒有方括號，加回來才和原本的寫法（與加上埠之後）一樣。
        var host = uri.HostNameType == UriHostNameType.IPv6 ? $"[{uri.IdnHost}]" : uri.IdnHost;
        return uri.IsDefaultPort ? host : $"{host}:{uri.Port}";
    }

    /// <summary>在導回網址後面加查詢參數（網址本來就有查詢字串時接在後面）。值是 null 的略過。</summary>
    public static string Append(string redirectUri, IEnumerable<(string Name, string? Value)> parameters)
    {
        var builder = new StringBuilder(redirectUri);
        var separator = redirectUri.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        foreach (var (name, value) in parameters)
        {
            if (value is null)
            {
                continue;
            }

            builder.Append(separator).Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(value));
            separator = '&';
        }

        return builder.ToString();
    }

    private static string LoopbackKey(Uri uri) => $"{uri.Scheme}://{uri.Host}{uri.PathAndQuery}";
}
