using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace PmTool.Identity.Sessions;

/// <summary>工作階段的權杖：32 個亂數位元組的 base64url。資料庫與快取只認它的 SHA-256。</summary>
internal static class SessionToken
{
    private const int TokenBytes = 32;

    /// <summary>32 個位元組的 base64url 不補等號，固定是 43 個字。</summary>
    private const int TokenLength = 43;

    /// <summary>開一個新的權杖，回傳明文與雜湊。明文只交給呼叫端一次，這裡不留。</summary>
    public static (string Token, byte[] Hash) Create()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenBytes);
        return (Base64Url.EncodeToString(bytes), SHA256.HashData(bytes));
    }

    /// <summary>
    /// 權杖不可能是我們發的（長度不對、不是 base64url）就回 false，不送進資料庫：
    /// 其他字元（例如 NUL）會讓查詢出錯，太長的字串也不值得算雜湊。
    /// 先逐字檢查再解碼，因為解碼器會略過空白，同一個權杖就會有好幾種寫法。
    /// </summary>
    public static bool TryHash(string token, [NotNullWhen(true)] out byte[]? hash)
    {
        hash = null;
        if (token.Length != TokenLength || !token.All(IsBase64UrlCharacter))
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[TokenBytes];
        if (!TryDecode(token, bytes))
        {
            return false;
        }

        hash = SHA256.HashData(bytes);
        return true;
    }

    /// <summary>
    /// 43 個字的最後一個字只用到 4 個位元，剩下 2 個位元不是 0 的字串不是我們編得出來的。
    /// <see cref="Base64Url.TryDecodeFromChars(ReadOnlySpan{char}, Span{byte}, out int)"/> 遇到它會丟 FormatException，
    /// 而不是回 false；存取權杖是任何人都能放在 Authorization 標頭送來的，不接住就是 500。
    /// </summary>
    private static bool TryDecode(string token, Span<byte> bytes)
    {
        try
        {
            return Base64Url.TryDecodeFromChars(token, bytes, out var written) && written == TokenBytes;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>快取的鍵。用雜湊而不是明文：記憶體傾印裡也不會留下能用的權杖。</summary>
    public static string CacheKeyOf(byte[] hash) => Convert.ToHexString(hash);

    /// <summary>
    /// 存取權杖的快取鍵。權杖的本體和工作階段的權杖同樣是 32 個位元組，<c>pmt_</c> 加某個工作階段的權杖
    /// 會算出同一個雜湊；加前綴讓兩種權杖永遠不會共用同一筆。
    /// </summary>
    public static string AccessTokenCacheKeyOf(byte[] hash) => "pat:" + CacheKeyOf(hash);

    private static bool IsBase64UrlCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_';
}
