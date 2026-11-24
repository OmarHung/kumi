using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PmTool.Identity.Tests.DataProtection;

/// <summary>測試裡產生的自簽憑證，只存在記憶體裡，不寫進任何檔案。</summary>
internal static class TestCertificates
{
    private const int RsaKeyBits = 2048;

    public static X509Certificate2 Create(string name)
    {
        using var rsa = RSA.Create(RsaKeyBits);
        var request = new CertificateRequest($"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

        // 匯出成 PFX 再讀回來：和正式環境從檔案讀憑證走同一條路，私鑰也才拿得來解密（有些平台剛產生的不行）。
        var password = Guid.NewGuid().ToString("N");
        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx, password), password);
    }
}
