namespace PmTool.Identity;

public interface IAuthenticationService
{
    /// <summary>
    /// 用 Email 與密碼確認身分。Email 不存在、密碼不對、帳號停用都回傳 null，
    /// 而且要花差不多的時間：呼叫端不該分得出是哪一種，否則就能拿來探測哪些 Email 有帳號。
    /// </summary>
    Task<AuthenticatedUser?> SignInAsync(string email, string password, CancellationToken cancellationToken);
}
