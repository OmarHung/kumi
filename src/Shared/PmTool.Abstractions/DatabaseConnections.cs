namespace PmTool.Abstractions;

/// <summary>
/// 模組連資料庫用的設定。執行時只用 <see cref="Runtime"/>（只能讀寫資料的角色）；遷移用 <see cref="Migrations"/>
/// （資料表的擁有者），沒給時和執行時是同一個。分成兩個角色，應用程式被攻破時才改不了資料表結構，
/// 之後加列層級安全也要靠「執行的角色不是擁有者」。
/// </summary>
/// <param name="Runtime">執行時的連線字串。</param>
/// <param name="Migrations">遷移用的連線字串；null 表示和 <paramref name="Runtime"/> 相同（本機開發、測試）。</param>
/// <param name="RuntimeRole">
/// 遷移完要授予讀寫權限的角色名稱（也就是 <paramref name="Runtime"/> 登入的角色）；
/// null 表示不授權，執行與遷移是同一個角色時用。
/// </param>
public sealed record DatabaseConnections(string Runtime, string? Migrations = null, string? RuntimeRole = null)
{
    /// <summary>遷移實際要用的連線字串。</summary>
    public string MigrationsConnection => Migrations ?? Runtime;
}
