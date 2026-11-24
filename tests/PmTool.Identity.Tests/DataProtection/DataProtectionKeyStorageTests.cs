using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PmTool.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace PmTool.Identity.Tests.DataProtection;

/// <summary>
/// 工作階段 Cookie 的金鑰存在 identity.data_protection_keys。每個容器是一個實例，同一個資料庫：
/// 一個實例加密的東西，另一個實例要解得開；設了憑證時存的是加密過的金鑰，換憑證之後舊的金鑰還解得開。
/// 觀察的是 Data Protection 的公開介面（加解密、金鑰的存放庫），不直接查資料表。
/// </summary>
[TestFixture]
public sealed class DataProtectionKeyStorageTests
{
    private const string ApplicationName = "pm-tool-tests";
    private const string Purpose = "tests";
    private const string Payload = "範例內容";

    [Test]
    public async Task Protect_InOneInstance_UnprotectsInAnotherInstance()
    {
        // Arrange：兩個容器共用同一個資料庫。
        var database = await PostgresServer.CreateDatabaseAsync();
        await using var first = await StartAsync(database);
        await using var second = await StartAsync(database);

        // Act
        var protectedPayload = Protector(first).Protect(Payload);
        var unprotected = Protector(second).Unprotect(protectedPayload);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(unprotected, Is.EqualTo(Payload));
            Assert.That(StoredKeys(second), Has.Count.EqualTo(1), "第二個實例用第一個存的金鑰，沒有自己再產生一把");
        }
    }

    [Test]
    public async Task Protect_AfterARestart_StillUnprotects()
    {
        // Arrange：容器重啟，新的程序從資料庫讀回金鑰。
        var database = await PostgresServer.CreateDatabaseAsync();
        string protectedPayload;
        await using (var before = await StartAsync(database))
        {
            protectedPayload = Protector(before).Protect(Payload);
        }

        await using var after = await StartAsync(database);

        // Act
        var unprotected = Protector(after).Unprotect(protectedPayload);

        // Assert
        Assert.That(unprotected, Is.EqualTo(Payload));
    }

    [Test]
    public async Task Keys_WithACertificate_AreStoredEncrypted()
    {
        // Arrange
        var database = await PostgresServer.CreateDatabaseAsync();
        using var certificate = TestCertificates.Create("example-keys");
        await using var host = await StartAsync(database, builder => builder.ProtectKeysWithCertificate(certificate));

        // Act
        _ = Protector(host).Protect(Payload);
        var stored = StoredKeys(host);

        // Assert：金鑰本身（masterKey）不以明文出現，只有用憑證加密過的內容。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored, Is.Not.Empty);
            Assert.That(stored.Select(key => HasElement(key, "encryptedSecret")), Is.All.True);
            Assert.That(stored.Select(key => HasElement(key, "masterKey")), Is.All.False);
        }
    }

    [Test]
    public async Task Keys_WithACertificate_CannotBeUsedWithoutIt()
    {
        // Arrange：資料庫外流時，拿到的金鑰沒有憑證就解不開。
        var database = await PostgresServer.CreateDatabaseAsync();
        using var certificate = TestCertificates.Create("example-keys");
        await using var owner = await StartAsync(database, builder => builder.ProtectKeysWithCertificate(certificate));
        var protectedPayload = Protector(owner).Protect(Payload);
        await using var withoutCertificate = await StartAsync(database);

        // Act & Assert
        Assert.That(() => Protector(withoutCertificate).Unprotect(protectedPayload), Throws.InstanceOf<CryptographicException>());
    }

    [Test]
    public async Task Keys_AfterTheCertificateIsReplaced_OldKeysStillUnprotectWithThePreviousCertificate()
    {
        // Arrange：換憑證時，舊的憑證只拿來解開舊的金鑰。
        var database = await PostgresServer.CreateDatabaseAsync();
        using var previous = TestCertificates.Create("example-previous");
        using var current = TestCertificates.Create("example-current");
        await using var before = await StartAsync(database, builder => builder.ProtectKeysWithCertificate(previous));
        var protectedPayload = Protector(before).Protect(Payload);
        await using var after = await StartAsync(
            database, builder => builder.ProtectKeysWithCertificate(current).UnprotectKeysWithAnyCertificate(current, previous));
        await using var currentOnly = await StartAsync(database, builder => builder.ProtectKeysWithCertificate(current));

        // Act
        var unprotected = Protector(after).Unprotect(protectedPayload);

        // Assert
        Assert.That(unprotected, Is.EqualTo(Payload));
        Assert.That(() => Protector(currentOnly).Unprotect(protectedPayload), Throws.InstanceOf<CryptographicException>(), "對照組：少了舊憑證就解不開");
    }

    [Test]
    public async Task GetAllElements_OneRowIsNotXml_SkipsItAndWarnsWithoutLoggingItsContent()
    {
        // Arrange：一列壞掉（人為改過、寫到一半）不能讓每個帶 Cookie 的請求都 500。
        var database = await PostgresServer.CreateDatabaseAsync();
        await using var host = await StartAsync(database);
        var repository = host.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository!;
        repository.StoreElement(new XElement("key", "good"), "key-good");
        await using (var db = await host.CreateDbContextAsync())
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO identity.data_protection_keys (friendly_name, xml, created_at) VALUES ('key-bad', '<secret-material', now())");
        }

        var logs = new RecordingLogger();
        var factory = host.Services.GetRequiredService<IDbContextFactory<IdentityDbContext>>();

        // Act
        var elements = new DatabaseXmlRepository(factory, TimeProvider.System, logs).GetAllElements();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(elements.Select(element => element.Value), Does.Contain("good"));
            Assert.That(elements, Has.Count.EqualTo(StoredKeys(host).Count));
            Assert.That(logs.Messages, Has.Count.EqualTo(1));
            Assert.That(logs.Levels, Is.EqualTo(new[] { LogLevel.Warning }));
            Assert.That(logs.Messages[0], Does.Not.Contain("secret-material"));
        }
    }

    [Test]
    public async Task StoreElement_FriendlyNameIsNull_StoresTheKeyUnderAGeneratedName()
    {
        // Arrange
        var database = await PostgresServer.CreateDatabaseAsync();
        await using var host = await StartAsync(database);
        var repository = host.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository!;

        // Act
        repository.StoreElement(new XElement("key", "unnamed"), null!);

        // Assert
        Assert.That(repository.GetAllElements().Select(element => element.Value), Does.Contain("unnamed"));
    }

    private sealed class RecordingLogger : ILogger<DatabaseXmlRepository>
    {
        public List<string> Messages { get; } = [];

        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Levels.Add(logLevel);
            Messages.Add(formatter(state, exception));
        }
    }

    private static Task<IdentityTestHost> StartAsync(string database, Action<IDataProtectionBuilder>? protect = null) =>
        IdentityTestHost.StartAsync(database, services =>
        {
            var builder = services.AddDataProtection().SetApplicationName(ApplicationName).PersistKeysToIdentityModule();
            protect?.Invoke(builder);
        });

    private static IDataProtector Protector(IdentityTestHost host) =>
        host.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose);

    private static IReadOnlyCollection<XElement> StoredKeys(IdentityTestHost host) =>
        host.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository!.GetAllElements();

    private static bool HasElement(XElement key, string localName) =>
        key.DescendantsAndSelf().Any(element => element.Name.LocalName == localName);
}
