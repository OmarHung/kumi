using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace PmTool.Identity.Tests;

/// <summary>
/// 包住真的雜湊器。上了閘之後，比對某一個密碼時會停在半路（已經從資料庫讀到雜湊、還沒比完），
/// 測試趁這時候改密碼或停用帳號，製造「驗證的同時帳號被改了」的順序；不用碼錶，所以每次都一樣。
/// </summary>
internal sealed class GatedPasswordHasher : IPasswordHasher<PasswordCredential>, IDisposable
{
    // 卡住的話測試失敗就好，不要讓整個測試程序停在那裡。
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(30);

    private readonly PasswordHasher<PasswordCredential> _inner = new(Options.Create(new PasswordHasherOptions()));
    private readonly ManualResetEventSlim _release = new(initialState: false);
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string? _gatedPassword;

    /// <summary>比對到被上閘的密碼、停下來的那一刻完成。</summary>
    public Task Entered => _entered.Task;

    /// <summary>之後第一次比對這個密碼時停下來，等 <see cref="Release"/>；只攔一次。</summary>
    public void Arm(string password) => _gatedPassword = password;

    public void Release() => _release.Set();

    public string HashPassword(PasswordCredential user, string password) => _inner.HashPassword(user, password);

    public PasswordVerificationResult VerifyHashedPassword(PasswordCredential user, string hashedPassword, string providedPassword)
    {
        // 只攔第一個比對：同一個密碼之後再比對（例如另一個請求也要驗它）不必等，測試才能在閘門關著時做別的事。
        if (providedPassword == _gatedPassword && Interlocked.CompareExchange(ref _gatedPassword, null, providedPassword) == providedPassword)
        {
            _entered.TrySetResult();
            if (!_release.Wait(MaxWait))
            {
                throw new TimeoutException("測試一直沒有放行停在半路的密碼比對。");
            }
        }

        return _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
    }

    public void Dispose() => _release.Dispose();
}
