using System.Security.Cryptography;
using System.Text;

namespace PmTool.Cli.Login;

/// <summary>PKCE（RFC 7636）的 S256：verifier 只留在這台電腦，伺服器只看得到 challenge，碼被別人攔走也換不到權杖。</summary>
internal static class Pkce
{
    private const int RandomBytes = 32;

    /// <summary>32 個亂數位元組的 base64url；state 與 verifier 都用它。</summary>
    public static string RandomToken() => Base64Url(RandomNumberGenerator.GetBytes(RandomBytes));

    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
