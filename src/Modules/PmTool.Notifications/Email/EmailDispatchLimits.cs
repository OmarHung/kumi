namespace PmTool.Notifications.Email;

/// <summary>
/// 寄送的時間上限。做成可以換的註冊：測試換成很短的時間，不必真的等上好幾分鐘。
/// </summary>
/// <param name="OrganizationBudget">一個組織一輪裡，連線與寄信最多花這麼久。</param>
internal sealed record EmailDispatchLimits(TimeSpan OrganizationBudget)
{
    public static EmailDispatchLimits Default { get; } = new(EmailRules.MaxOrganizationDispatchTime);

    /// <summary>記在「最近的錯誤」裡的說明。</summary>
    public string ExceededMessage => OrganizationBudget >= TimeSpan.FromMinutes(1) && OrganizationBudget.Seconds == 0
        ? $"這一輪寄送超過 {(int)OrganizationBudget.TotalMinutes} 分鐘，下一輪再試"
        : $"這一輪寄送超過 {OrganizationBudget.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} 秒，下一輪再試";
}
