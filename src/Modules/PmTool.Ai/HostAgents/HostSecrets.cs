using System.Security.Cryptography;
using System.Text;

namespace PmTool.Ai.HostAgents;

/// <summary>
/// 機器憑證（<c>kha_</c>）、工具授權（<c>kjt_</c>）與一次性的配對碼、登入碼。資料庫只存 SHA-256（hex）。
/// 碼是 12 個 Crockford Base32 的字，顯示成 <c>XXXX-XXXX-XXXX</c>。
/// </summary>
internal static class HostSecrets
{
    public const string CredentialPrefix = "kha_";

    public const string ToolTokenPrefix = "kjt_";

    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private const int CodeLength = 12;

    private const int TokenBytes = 32;

    private const int MaxTokenLength = 100;

    public static string NewToken(string prefix) => prefix + Base64Url(RandomNumberGenerator.GetBytes(TokenBytes));

    public static string Hash(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

    public static string NewCode()
    {
        var chars = new char[CodeLength];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        var text = new string(chars);
        return $"{text[..4]}-{text[4..8]}-{text[8..]}";
    }

    /// <summary>轉大寫、去掉 <c>-</c> 與空白、<c>O→0</c>、<c>I/L→1</c>；長度或字元不對是 null。</summary>
    public static string? NormalizeCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var builder = new StringBuilder(CodeLength);
        foreach (var c in raw.ToUpperInvariant())
        {
            if (c == '-' || char.IsWhiteSpace(c))
            {
                continue;
            }

            builder.Append(c switch { 'O' => '0', 'I' or 'L' => '1', _ => c });
        }

        var text = builder.ToString();
        return text.Length == CodeLength && text.All(c => Alphabet.Contains(c, StringComparison.Ordinal)) ? text : null;
    }

    public static string? HashCode(string? raw) => NormalizeCode(raw) is { } code ? Hash(code) : null;

    /// <summary>字首、長度、字元都像一把憑證；不像的連資料庫都不必查。</summary>
    public static bool LooksLikeToken(string? token, string prefix) =>
        token is { Length: > 0 and <= MaxTokenLength }
        && token.StartsWith(prefix, StringComparison.Ordinal)
        && token.Length > prefix.Length
        && token.Skip(prefix.Length).All(c => c is '-' or '_' or (>= '0' and <= '9') or (>= 'a' and <= 'z') or (>= 'A' and <= 'Z'));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
