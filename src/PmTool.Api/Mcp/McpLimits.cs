namespace PmTool.Api.Mcp;

/// <summary>MCP 端點的上限。宣告成 public 是為了讓測試專案能引用同一個數字（和 ApiLimits 一樣）。</summary>
public static class McpLimits
{
    /// <summary>
    /// <c>/mcp</c> 的請求內文上限（位元組）。工具的參數都是短的 JSON（最大的是變更集），2 MB 綽綽有餘；
    /// 不明確設的話只剩 Kestrel 預設的 30 MB，登入的人一個請求就能讓伺服器讀 30 MB。
    /// </summary>
    public const int MaxRequestBytes = 2 * 1024 * 1024;

    /// <summary>每個人每分鐘的工具呼叫次數。「依人」的全域限流管的是請求數，一個工具呼叫可能牽動好幾個模組查詢，另外算。</summary>
    public const int ToolCallsPerMinute = 60;

    public const int ToolCallWindowSeconds = 60;

    public const int DefaultSearchLimit = 8;

    public const int MaxSearchLimit = 20;

    public const int DefaultListLimit = 50;

    public const int MaxListLimit = 200;

    /// <summary>get_item 帶回的下層項目數與活動筆數。</summary>
    public const int ChildrenInItem = 50;

    public const int ActivitiesInItem = 10;

    /// <summary>找最近的活動時最多翻幾頁：活動只能由舊到新讀，翻太多頁只會拖慢一個工具呼叫。</summary>
    public const int MaxActivityPages = 10;

    /// <summary>一個工具結果的字元上限。超過就改回錯誤，不回截斷的 JSON（半截的 JSON 比沒有更糟）。</summary>
    public const int MaxResultCharacters = 400_000;
}
