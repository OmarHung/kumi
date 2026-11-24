namespace PmTool.Identity;

/// <summary>
/// <see cref="Microsoft.AspNetCore.Identity.PasswordHasher{TUser}"/> 要一個使用者型別，但根本不看它。
/// 用一個固定的物件，查無此 Email 時也能用同一個雜湊器、同樣的參數算一次，花的時間才會一樣。
/// </summary>
internal sealed class PasswordCredential
{
    public static readonly PasswordCredential Instance = new();

    private PasswordCredential()
    {
    }
}
