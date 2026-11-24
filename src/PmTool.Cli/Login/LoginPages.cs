using System.Text;

namespace PmTool.Cli.Login;

/// <summary>迴路收到請求時回給瀏覽器的小頁面：全部內嵌，不載入任何外部資源。</summary>
internal static class LoginPages
{
    public const string SecurityHeaders =
        "Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'\r\n"
        + "Referrer-Policy: no-referrer\r\n"
        + "Cache-Control: no-store\r\n"
        + "Connection: close\r\n";

    public static readonly string Success = Page("完成了", "可以關掉這個分頁，回到終端機。");

    public static readonly string Denied = Page("已拒絕", "沒有登入。可以關掉這個分頁，回到終端機。");

    public static readonly string Stale = Page("這是舊的登入頁", "請回到終端機重新執行登入指令，再用新開的頁面。");

    public static readonly string NotFound = Page("找不到", "這裡沒有東西。");

    private static string Page(string title, string message) =>
        $"<!doctype html><html lang=\"zh-Hant\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
        + $"<title>Kumi：{title}</title><style>body{{font-family:system-ui,sans-serif;display:grid;place-items:center;min-height:100vh;margin:0}}"
        + $"main{{max-width:28rem;padding:1.5rem;text-align:center}}h1{{font-size:1.25rem}}p{{color:#555}}</style></head>"
        + $"<body><main><h1>{title}</h1><p>{message}</p></main></body></html>";

    public static byte[] Response(int status, string reason, string html)
    {
        var body = Encoding.UTF8.GetBytes(html);
        var head = $"HTTP/1.1 {status} {reason}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\n{SecurityHeaders}\r\n";
        return [.. Encoding.ASCII.GetBytes(head), .. body];
    }
}
