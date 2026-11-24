using System.Diagnostics.CodeAnalysis;
using PmTool.Identity.Sessions;

namespace PmTool.Identity.AccessTokens;

/// <summary>權杖的明文：<c>pmt_</c> 加上和工作階段一樣的 32 位元組亂數 base64url。資料庫與快取只認 SHA-256。</summary>
internal static class AccessTokenSecret
{
    /// <summary>畫面上顯示的開頭字數（含前綴）：認得出是哪一把，不足以猜出它。</summary>
    public const int DisplayPrefixLength = 8;

    public static (string Secret, byte[] Hash) Create()
    {
        var (body, hash) = SessionToken.Create();
        return (AccessTokenRules.Prefix + body, hash);
    }

    /// <summary>前綴或內容不對（不可能是我們發的）就回 false，不送進資料庫。</summary>
    public static bool TryHash(string secret, [NotNullWhen(true)] out byte[]? hash)
    {
        hash = null;
        return secret.StartsWith(AccessTokenRules.Prefix, StringComparison.Ordinal)
            && SessionToken.TryHash(secret[AccessTokenRules.Prefix.Length..], out hash);
    }
}
