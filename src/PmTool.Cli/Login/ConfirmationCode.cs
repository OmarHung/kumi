using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PmTool.Cli.Login;

/// <summary>
/// 六碼確認碼：由 PKCE 的 challenge 算出，終端機印出來、使用者要在瀏覽器的同意頁輸入同一組。
/// 算法要和伺服器、網頁一致（kumi 不引用伺服器的合約，所以這裡自己有一份）。
/// </summary>
internal static class ConfirmationCode
{
    private const string Prefix = "kumi-cli-login-confirm\n";
    private const uint Modulus = 1_000_000;

    public static string Of(string challenge)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(Prefix).Concat(Encoding.ASCII.GetBytes(challenge)).ToArray());
        var number = BinaryPrimitives.ReadUInt32BigEndian(digest) % Modulus;
        return number.ToString("D6", CultureInfo.InvariantCulture);
    }

    public static string Display(string challenge)
    {
        var code = Of(challenge);
        return $"{code[..3]} {code[3..]}";
    }
}
