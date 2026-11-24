using System.Collections.Concurrent;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace PmTool.Identity.Tests;

/// <summary>
/// 包住真的雜湊器並記下每次呼叫。時間差不多這件事用碼錶測會不穩，
/// 改成確認「每一種失敗都算了一次雜湊」；也從這裡看得到存進資料庫的雜湊長什麼樣子。
/// </summary>
internal sealed class RecordingPasswordHasher(PasswordHasherOptions? options = null) : IPasswordHasher<PasswordCredential>
{
    private readonly PasswordHasher<PasswordCredential> _inner = new(Options.Create(options ?? new PasswordHasherOptions()));
    private readonly ConcurrentQueue<string> _producedHashes = new();
    private readonly ConcurrentQueue<string> _verifiedHashes = new();

    /// <summary>算出來的雜湊，依呼叫的順序。</summary>
    public IReadOnlyList<string> ProducedHashes => [.. _producedHashes];

    /// <summary>拿來比對的雜湊（也就是從資料庫讀出來的），依呼叫的順序。</summary>
    public IReadOnlyList<string> VerifiedHashes => [.. _verifiedHashes];

    public string HashPassword(PasswordCredential user, string password)
    {
        var hash = _inner.HashPassword(user, password);
        _producedHashes.Enqueue(hash);
        return hash;
    }

    public PasswordVerificationResult VerifyHashedPassword(PasswordCredential user, string hashedPassword, string providedPassword)
    {
        _verifiedHashes.Enqueue(hashedPassword);
        return _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
    }
}
