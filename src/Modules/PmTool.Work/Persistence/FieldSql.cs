namespace PmTool.Work.Persistence;

/// <summary>
/// 只能放進 LINQ 查詢的資料庫函式：從自訂欄位的 jsonb 取值，翻成 SQL 在 <see cref="WorkDbContext"/> 註冊。
/// 不能直接呼叫；key 一律是參數，不拼進 SQL。
/// </summary>
internal static class FieldSql
{
    /// <summary>jsonb 的值。jsonb 自己有排序（數字依數值、字串依資料庫的定序、false 在 true 前），排序不必知道型別。</summary>
    public static string? Json(string fields, string key) => throw new NotSupportedException("只能用在查詢裡。");

    /// <summary>值的文字（字串沒有引號）；單選的值用它對照選項。</summary>
    public static string? Text(string fields, string key) => throw new NotSupportedException("只能用在查詢裡。");
}
