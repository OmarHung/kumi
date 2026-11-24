using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PmTool.Api.Tests.Support;

/// <summary>
/// 測試裡產生的自簽憑證，寫成 PFX 放在暫存目錄（不在 repo 裡），測試結束時連目錄一起刪掉。
/// </summary>
internal sealed class TestCertificates : IDisposable
{
    public const string Password = "example-pfx-password";

    private const int RsaKeyBits = 2048;

    public string Directory { get; } = ApiFactory.CreateTemporaryDirectory();

    /// <summary>產生一張有私鑰的憑證，回傳 PFX 檔的路徑。</summary>
    public string Create(string name) => Write(name, includePrivateKey: true);

    /// <summary>產生一張只有公鑰的 PFX：讀得進來，但解不開用它加密的金鑰。</summary>
    public string CreateWithoutPrivateKey(string name) => Write(name, includePrivateKey: false);

    public void Dispose()
    {
        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }

    private string Write(string name, bool includePrivateKey)
    {
        using var rsa = RSA.Create(RsaKeyBits);
        var request = new CertificateRequest($"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        using var exported = includePrivateKey ? null : X509CertificateLoader.LoadCertificate(created.RawData);
        var path = Path.Combine(Directory, $"{name}.pfx");
        File.WriteAllBytes(path, (exported ?? created).Export(X509ContentType.Pfx, Password));
        return path;
    }
}
