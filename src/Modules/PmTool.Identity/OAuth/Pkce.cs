using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace PmTool.Identity.OAuth;

/// <summary>PKCE（RFC 7636），只收 S256。</summary>
internal static class Pkce
{
    public const string Method = "S256";

    private const int MinLength = 43;

    private const int MaxLength = 128;

    /// <summary>code_challenge：43～128 個 base64url 字元。</summary>
    public static bool IsValidChallenge(string? challenge) =>
        challenge is { Length: >= MinLength and <= MaxLength } && challenge.All(IsBase64UrlCharacter);

    /// <summary>code_verifier：43～128 個「不保留字元」（RFC 7636 4.1）。</summary>
    public static bool IsValidVerifier(string? verifier) =>
        verifier is { Length: >= MinLength and <= MaxLength } && verifier.All(c => IsBase64UrlCharacter(c) || c is '.' or '~');

    public static bool Verify(string verifier, string challenge)
    {
        var computed = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(challenge));
    }

    private static bool IsBase64UrlCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_';
}
