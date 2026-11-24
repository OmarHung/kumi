namespace PmTool.Notifications;

/// <summary>通知模組回的失敗代碼。代碼是對外合約，不能改名。</summary>
public static class NotificationErrorCodes
{
    /// <summary>和工作引擎的清單查詢用同一個代碼，網頁只要認一種。</summary>
    public const string QueryInvalid = "query.invalid";

    /// <summary>不是專案成員，不能關注這個專案的項目（只有成員收得到通知）。</summary>
    public const string NotProjectMember = "subscription.not_member";

    /// <summary>寄信設定只有組織管理員能讀、能改（403）。</summary>
    public const string EmailForbidden = "email.forbidden";

    /// <summary>寄信設定的內容不對（422）。細項的代碼是下面四個，路徑是欄位名稱。</summary>
    public const string EmailSettingsInvalid = "email.settings_invalid";

    /// <summary>細項：開著寄信（或寄測試信）時這個欄位一定要填。</summary>
    public const string EmailFieldRequired = "email.field_required";

    /// <summary>細項：格式不對、太長、有控制字元。</summary>
    public const string EmailFieldInvalid = "email.field_invalid";

    /// <summary>細項：連接埠不在 <see cref="EmailRules.AllowedPorts"/> 裡。</summary>
    public const string EmailPortNotAllowed = "email.port_not_allowed";

    /// <summary>細項：不加密時設了帳號或密碼（會用明文送出）。</summary>
    public const string EmailInsecureCredentials = "email.insecure_credentials";

    /// <summary>測試信寄不出去（422），訊息說明原因。</summary>
    public const string EmailSendFailed = "email.send_failed";
}
