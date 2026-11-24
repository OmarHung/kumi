namespace PmTool.Work.ChangeSets;

/// <summary>
/// 套用的上限。正式環境一律是 <see cref="WorkRules.MaxItemsPerApply"/> 與 <see cref="ApplyLockTimeoutSeconds"/>；做成可以注入的值，
/// 是為了讓測試用幾個項目就測得到上限的邊界、不必真的建五千個項目，等鎖也不必真的等五秒。
/// </summary>
internal sealed record ChangeSetLimits(int MaxItemsPerApply)
{
    /// <summary>
    /// 套用最多等鎖幾秒。套用要獨佔專案那一列，項目的寫入先拿共用鎖；寫入一直進來的話，排在後面的套用可能永遠等不到，
    /// 這段時間裡專案也不能有新的寫入。等不到就放棄，請使用者稍後再試。
    /// </summary>
    public const int ApplyLockTimeoutSeconds = 5;

    public static readonly ChangeSetLimits Default = new(WorkRules.MaxItemsPerApply);

    public TimeSpan ApplyLockTimeout { get; init; } = TimeSpan.FromSeconds(ApplyLockTimeoutSeconds);
}
