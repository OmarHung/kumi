namespace PmTool.Identity;

public interface IIdentityBootstrapper
{
    /// <summary>
    /// 資料庫裡還沒有任何帳號時，建立第一個組織與它的擁有者；已經有帳號就什麼都不做。
    /// 回傳這一次有沒有建立。資料不合規定（例如密碼太短）時丟 <see cref="ArgumentException"/>，
    /// 因為那是部署設定寫錯了，要讓啟動直接失敗。
    /// </summary>
    Task<bool> EnsureFirstOwnerAsync(FirstOwner owner, CancellationToken cancellationToken);
}
