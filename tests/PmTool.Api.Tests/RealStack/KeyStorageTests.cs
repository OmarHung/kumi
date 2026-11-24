using System.Net;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 工作階段 Cookie 的金鑰存在資料庫（身分模組）：換容器、多開容器都解得開彼此發的 Cookie；
/// 設了 DataProtection:CertificatePath 時金鑰用憑證加密，換憑證時舊的放在 PreviousCertificatePaths。
/// 每個 factory 代表一個容器，共用同一個資料庫。
/// </summary>
[TestFixture]
public sealed class KeyStorageTests
{
    private static readonly Dictionary<string, string?> NoSettings = [];

    [Test]
    public async Task Session_AfterTheContainerIsReplaced_IsStillSignedIn()
    {
        // Arrange：不必掛資料卷，新的容器從資料庫讀回金鑰。
        var database = await RealStackDatabase.CreateDatabaseAsync();
        var cookie = await SignInAsync(database, NoSettings);

        // Act
        var status = await StatusWithCookieAsync(database, NoSettings, cookie);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Session_AfterTheCertificateIsReplaced_StillSignedInWithThePreviousCertificateConfigured()
    {
        // Arrange
        using var certificates = new TestCertificates();
        var previous = certificates.Create("example-previous");
        var current = certificates.Create("example-current");
        var database = await RealStackDatabase.CreateDatabaseAsync();
        var cookie = await SignInAsync(database, WithCertificate(previous));

        // Act
        var rotated = await StatusWithCookieAsync(
            database, WithCertificate(current, ("DataProtection:PreviousCertificatePaths:0", previous)), cookie);
        var withoutPrevious = await StatusWithCookieAsync(database, WithCertificate(current), cookie);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rotated, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(withoutPrevious, Is.EqualTo(HttpStatusCode.Unauthorized), "對照組：少了舊憑證就解不開舊的金鑰");
        }
    }

    private static Dictionary<string, string?> WithCertificate(string path, params (string Key, string Value)[] more)
    {
        var settings = new Dictionary<string, string?>
        {
            ["DataProtection:CertificatePath"] = path,
            ["DataProtection:CertificatePassword"] = TestCertificates.Password,
        };
        foreach (var (key, value) in more)
        {
            settings[key] = value;
        }

        return settings;
    }

    private static async Task<string> SignInAsync(string database, IReadOnlyDictionary<string, string?> settings)
    {
        using var factory = new RealStackFactory(database, Options(settings));
        using var client = Sessions.CreateReplayClient(factory);
        return await RealStackSteps.SignInForCookieAsync(client, RealStackFactory.OwnerEmail, RealStackFactory.OwnerPassword);
    }

    private static async Task<HttpStatusCode> StatusWithCookieAsync(
        string database, IReadOnlyDictionary<string, string?> settings, string cookie)
    {
        using var factory = new RealStackFactory(database, Options(settings));
        using var client = Sessions.CreateReplayClient(factory);
        return await Sessions.SendWithCookieAsync(client, Sessions.Route, cookie);
    }

    private static RealStackOptions Options(IReadOnlyDictionary<string, string?> settings) =>
        new() { Settings = settings, UseEphemeralDataProtection = false };
}
