using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace PmTool.Notifications.Email;

/// <summary>
/// SMTP 密碼加密後才存。金鑰與資料放在同一個資料庫（和 Cookie 同一套），所以這擋的是「只外流了一張表或一份記錄」，
/// 不是整個資料庫外流。purpose 是固定字串：換掉它，之前存的密碼全部解不開。
/// </summary>
internal sealed class EmailPasswordProtector(IDataProtectionProvider provider)
{
    private const string Purpose = "PmTool.Notifications.EmailSettings.Password.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(string password) => _protector.Protect(password);

    /// <summary>金鑰不見了（資料庫換了、金鑰被清掉）或資料壞了時回 false，呼叫端請管理員重新輸入。</summary>
    public bool TryUnprotect(string protectedPassword, out string password)
    {
        try
        {
            password = _protector.Unprotect(protectedPassword);
            return true;
        }
        catch (CryptographicException)
        {
            password = string.Empty;
            return false;
        }
    }
}
