namespace PmTool.Identity.Persistence;

/// <summary>
/// <c>identity.data_protection_keys</c> 的一列：一把加密工作階段 Cookie 的金鑰（Data Protection 的 XML）。
/// 所有容器共用同一份，換容器、多開容器都解得開彼此發的 Cookie。
/// 沒有 organization_id：它是整套系統的設定，不屬於哪個組織，和遷移紀錄表一樣是例外。
/// </summary>
internal sealed record DataProtectionKeyRow
{
    /// <summary>資料庫產生的流水號，只用來保持讀出來的順序。</summary>
    public int Id { get; init; }

    /// <summary>Data Protection 給的名稱（金鑰的編號），查問題時看得出是哪一把。</summary>
    public required string FriendlyName { get; init; }

    /// <summary>金鑰的 XML。設了憑證時是加密過的。</summary>
    public required string Xml { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
