namespace PmTool.Work;

/// <summary>
/// 跨專案的項目清單（「我的工作」的四種版面）要看哪些專案。條件本身用 <see cref="WorkItemQuery"/>，和單一專案的清單同一套。
/// </summary>
/// <param name="ProjectKeys">
/// 只看這幾個專案（不分大小寫）。空的是呼叫者看得到的所有專案。看不到、不存在、封存的專案照樣不列，不算錯：
/// 不透露別人的專案存不存在。最多 <see cref="MaxProjects"/> 個。
/// </param>
public sealed record CrossProjectScope(IReadOnlyList<string> ProjectKeys)
{
    public const int MaxProjects = 20;

    public static CrossProjectScope All { get; } = new([]);
}

/// <summary>
/// 呼叫者看得到、沒有封存的專案用到的一種項目類型。各專案的藍圖各自定義類型，同一個 key 在不同專案可能叫不同的名字，
/// 所以依 key 合併、名字都列出來：跨專案的篩選送的是 key。
/// </summary>
/// <param name="Names">這個 key 在各專案的名稱，去掉重複，照第一次出現的順序（專案依代號排）。</param>
/// <param name="ProjectKeys">用到這個類型的專案代號，照代號排。</param>
public sealed record ItemTypeSummary(string Key, IReadOnlyList<string> Names, IReadOnlyList<string> ProjectKeys);
