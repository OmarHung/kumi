using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

/// <summary>
/// 上雲用的設定（轉送的標頭、HTTPS、資料庫角色、遷移、金鑰的憑證）寫錯時讓啟動直接失敗，訊息說得出是哪個設定：
/// 設定錯了要在部署時就知道，不要帶著錯的設定上線，等到使用者被登出、限流失靈才發現。
/// </summary>
[TestFixture]
public sealed class DeploymentSettingsTests
{
    private const string TransportSecurityCategory = "PmTool.Api.Security.TransportSecurity";
    private const string KeysNotEncryptedCategory = "PmTool.Api.Security.KeyProtection";

    private static IEnumerable<TestCaseData> InvalidSettings()
    {
        yield return Invalid("ForwardedHeaders:Enabled", ("ForwardedHeaders:Enabled", "true"))
            .SetName("Startup_ForwardedHeadersEnabledWithoutAnyTrustedProxy_Fails");
        yield return Invalid("ForwardedHeaders:Enabled", ("ForwardedHeaders:Enabled", "yes"))
            .SetName("Startup_ForwardedHeadersEnabledNotABoolean_Fails");
        yield return Invalid(
                "ForwardedHeaders:KnownProxies", ("ForwardedHeaders:Enabled", "true"), ("ForwardedHeaders:KnownProxies:0", "proxy.example"))
            .SetName("Startup_KnownProxyNotAnAddress_Fails");
        yield return Invalid(
                "ForwardedHeaders:KnownNetworks", ("ForwardedHeaders:Enabled", "true"), ("ForwardedHeaders:KnownNetworks:0", "10.0.0.0"))
            .SetName("Startup_KnownNetworkWithoutPrefixLength_Fails");
        yield return Invalid(
                "ForwardedHeaders:KnownNetworks", ("ForwardedHeaders:Enabled", "true"), ("ForwardedHeaders:KnownNetworks:0", "10.0.0.0/33"))
            .SetName("Startup_KnownNetworkPrefixTooLong_Fails");
        yield return Invalid(
                "ForwardedHeaders:KnownNetworks", ("ForwardedHeaders:Enabled", "true"), ("ForwardedHeaders:KnownNetworks:0", "10.0.0.1/8"))
            .SetName("Startup_KnownNetworkWithHostBits_Fails");
        yield return Invalid(
                "ForwardedHeaders:TrustAllProxies", ("ForwardedHeaders:Enabled", "true"), ("ForwardedHeaders:TrustAllProxies", "always"))
            .SetName("Startup_TrustAllProxiesNotABoolean_Fails");
        foreach (var limit in new[] { "0", "11", "two", "1.5", "-1", "+2", "99999999999" })
        {
            yield return Invalid("ForwardedHeaders:ForwardLimit", ("ForwardedHeaders:ForwardLimit", limit))
                .SetName($"Startup_ForwardLimit_{limit}_Fails");
        }

        foreach (var (name, host) in new[]
        {
            ("WithScheme", "https://pm.example.com"),
            ("WithPath", "pm.example.com/app"),
            ("WithSpace", "pm example.com"),
            ("WithCredentials", "user@pm.example.com"),
            ("PortTooBig", "pm.example.com:99999"),
            ("PortZero", "pm.example.com:0"),
            ("PortNotANumber", "pm.example.com:https"),
            ("StartsWithADot", ".example.com"),
        })
        {
            yield return Invalid("Security:PublicHost", ("Security:PublicHost", host))
                .SetName($"Startup_PublicHost{name}_Fails");
        }

        yield return Invalid("Security:RequireHttps", ("Security:RequireHttps", "sometimes"))
            .SetName("Startup_RequireHttpsNotABoolean_Fails");
        yield return Invalid("Database:RuntimeRole", ("Database:RuntimeRole", "Pm-Tool"))
            .SetName("Startup_RuntimeRoleNotAValidName_Fails");
        yield return Invalid("Database:RuntimeRole", ("Database:RuntimeRole", "app\"; DROP SCHEMA work; --"))
            .SetName("Startup_RuntimeRoleWithSql_Fails");
        yield return Invalid("Database:MigrateOnStartup", ("Database:MigrateOnStartup", "later"))
            .SetName("Startup_MigrateOnStartupNotABoolean_Fails");
        yield return Invalid("DataProtection:CertificatePath", ("DataProtection:CertificatePath", "/nonexistent/example.pfx"))
            .SetName("Startup_CertificateFileMissing_Fails");
        yield return Invalid("DataProtection:PreviousCertificatePaths", ("DataProtection:PreviousCertificatePaths:0", "/nonexistent/old.pfx"))
            .SetName("Startup_PreviousCertificatesWithoutACurrentOne_Fails");
    }

    [TestCaseSource(nameof(InvalidSettings))]
    public void Startup_InvalidSetting_FailsNamingTheSetting(string setting, IReadOnlyDictionary<string, string?> settings)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { Settings = settings });

        // Act
        TestDelegate start = () => factory.CreateClient().Dispose();

        // Assert
        Assert.That(start, Throws.InvalidOperationException.With.Message.Contains(setting));
    }

    [Test]
    public void Startup_ValidProxySettingsInEveryAcceptedForm_Starts()
    {
        // Arrange：IPv4 與 IPv6、多個值（Key__0）與只有一個值（Key）的寫法。
        var settings = new Dictionary<string, string?>
        {
            ["ForwardedHeaders:Enabled"] = "TRUE",
            ["ForwardedHeaders:KnownProxies"] = "192.0.2.1",
            ["ForwardedHeaders:KnownNetworks:0"] = "10.0.0.0/8",
            ["ForwardedHeaders:KnownNetworks:1"] = "2001:db8::/32",
            ["ForwardedHeaders:KnownNetworks:2"] = " ",
            ["Security:RequireHttps"] = " true ",
            ["ForwardedHeaders:ForwardLimit"] = "2",
            ["Security:PublicHost"] = "pm.example.com:8443",
            ["Database:RuntimeRole"] = "pmtool_app",
            ["Database:MigrateOnStartup"] = "",
        };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { Settings = settings });

        // Act
        TestDelegate start = () => factory.CreateClient().Dispose();

        // Assert
        Assert.That(start, Throws.Nothing);
    }

    [TestCase("true", "", 1, TestName = "Startup_RequireHttpsWithoutForwardedHeaders_WarnsOnce")]
    [TestCase("true", "enabled", 0, TestName = "Startup_RequireHttpsWithForwardedHeaders_DoesNotWarn")]
    [TestCase("false", "", 0, TestName = "Startup_NoRequireHttps_DoesNotWarn")]
    public void Startup_RequireHttps_WarnsOnlyWhenForwardedHeadersAreNotEnabled(string requireHttps, string forwarded, int expected)
    {
        // Arrange：放在代理後面卻沒讀轉送的標頭，每個請求看起來都是 http，會無限轉址；api 自己提供 HTTPS 時卻是合法的，所以只警告。
        var settings = new Dictionary<string, string?> { ["Security:RequireHttps"] = requireHttps };
        if (forwarded.Length > 0)
        {
            settings["ForwardedHeaders:Enabled"] = "true";
            settings["ForwardedHeaders:TrustAllProxies"] = "true";
        }

        // Act
        var warnings = StartAndCollectTransportWarnings(settings);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(warnings, Has.Count.EqualTo(expected));
            Assert.That(warnings.All(line => line.Contains("ForwardedHeaders:")), Is.True);
        }
    }

    [TestCase(true, 1, TestName = "Startup_TrustAllProxiesWithKnownProxies_WarnsOnce")]
    [TestCase(false, 0, TestName = "Startup_TrustAllProxiesAlone_DoesNotWarn")]
    public void Startup_TrustAllProxies_WarnsWhenKnownProxiesAreAlsoSet(bool withKnownProxies, int expected)
    {
        // Arrange：TrustAllProxies 時 KnownProxies／KnownNetworks 被忽略；設的人多半以為只信任那幾個。
        var settings = new Dictionary<string, string?>
        {
            ["ForwardedHeaders:Enabled"] = "true",
            ["ForwardedHeaders:TrustAllProxies"] = "true",
        };
        if (withKnownProxies)
        {
            settings["ForwardedHeaders:KnownProxies:0"] = "192.0.2.1";
        }

        // Act
        var warnings = StartAndCollectTransportWarnings(settings);

        // Assert
        Assert.That(warnings, Has.Count.EqualTo(expected));
    }

    [TestCase("Production", null, 1, TestName = "Startup_ProductionWithoutPublicHost_WarnsOnce")]
    [TestCase("Production", "pm.example.com", 0, TestName = "Startup_ProductionWithPublicHost_DoesNotWarn")]
    [TestCase("Development", null, 0, TestName = "Startup_DevelopmentWithoutPublicHost_DoesNotWarn")]
    public void Startup_PublicHost_WarnsOnlyInProductionWhenItIsMissing(string environment, string? publicHost, int expected)
    {
        // Arrange：沒設的話 OAuth 的 issuer 來自 Host 標頭；本機的 8080 也是 Production，所以只警告、不讓啟動失敗。
        var settings = new Dictionary<string, string?>();
        if (publicHost is not null)
        {
            settings["Security:PublicHost"] = publicHost;
        }

        // Act
        var warnings = StartAndCollectTransportWarnings(settings, environment, defaultPublicHost: false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(warnings, Has.Count.EqualTo(expected));
            Assert.That(warnings.All(line => line.Contains("Security:PublicHost")), Is.True);
        }
    }

    // 其他的警告測試不關心 PublicHost：預設補上一個，才不會多出「沒設 PublicHost」那一則。
    private static IReadOnlyList<string> StartAndCollectTransportWarnings(
        IReadOnlyDictionary<string, string?> given, string environment = "Production", bool defaultPublicHost = true)
    {
        var settings = new Dictionary<string, string?>(given);
        if (defaultPublicHost)
        {
            settings.TryAdd("Security:PublicHost", "pm.example.com");
        }

        var logs = new CapturingLoggerProvider();
        var options = new ApiFactoryOptions
        {
            Environment = environment,
            Settings = settings,
            ConfigureServices = services => services.AddSingleton<ILoggerProvider>(logs),
        };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), options);
        factory.CreateClient().Dispose();
        return [.. logs.Entries
            .Where(entry => entry.Category == TransportSecurityCategory && entry.Level == LogLevel.Warning)
            .Select(entry => entry.Message)];
    }

    [Test]
    public void Startup_RequireCertificateWithoutACertificatePath_Fails()
    {
        // Arrange：要求憑證卻沒給，金鑰會以明文存進資料庫；與其帶著上線，不如啟動就失敗。
        var settings = new Dictionary<string, string?> { ["DataProtection:RequireCertificate"] = "true" };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { Settings = settings });

        // Act
        TestDelegate start = () => factory.CreateClient().Dispose();

        // Assert
        Assert.That(start, Throws.InvalidOperationException
            .With.Message.Contains("DataProtection:RequireCertificate")
            .And.Message.Contains("DataProtection:CertificatePath"));
    }

    [Test]
    public void Startup_RequireCertificateNotABoolean_Fails()
    {
        // Arrange
        var settings = new Dictionary<string, string?> { ["DataProtection:RequireCertificate"] = "sure" };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { Settings = settings });

        // Act
        TestDelegate start = () => factory.CreateClient().Dispose();

        // Assert
        Assert.That(start, Throws.InvalidOperationException.With.Message.Contains("DataProtection:RequireCertificate"));
    }

    [Test]
    public void Startup_RequireCertificateWithACertificate_Starts()
    {
        // Arrange
        using var certificates = new TestCertificates();
        var settings = new Dictionary<string, string?>
        {
            ["DataProtection:RequireCertificate"] = "true",
            ["DataProtection:CertificatePath"] = certificates.Create("example-keys"),
            ["DataProtection:CertificatePassword"] = TestCertificates.Password,
        };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { Settings = settings });

        // Act
        TestDelegate start = () => factory.CreateClient().Dispose();

        // Assert
        Assert.That(start, Throws.Nothing);
    }

    [Test]
    public void Startup_ProductionRequireHttpsWithoutAPublicHost_Fails()
    {
        // Arrange：一律走 HTTPS 的正式環境沒有對外的主機名稱，OAuth 的 issuer 與轉址只能信請求的 Host 標頭。
        var settings = new Dictionary<string, string?> { ["Security:RequireHttps"] = "true" };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { Settings = settings });

        // Act
        TestDelegate start = () => factory.CreateClient().Dispose();

        // Assert
        Assert.That(start, Throws.InvalidOperationException
            .With.Message.Contains("Security:PublicHost")
            .And.Message.Contains("Security:RequireHttps"));
    }

    [TestCase("Staging", null, TestName = "Startup_StagingRequireHttpsWithoutAPublicHost_Starts")]
    [TestCase("Development", null, TestName = "Startup_DevelopmentRequireHttpsWithoutAPublicHost_Starts")]
    [TestCase("Production", "pm.example.com", TestName = "Startup_ProductionRequireHttpsWithAPublicHost_Starts")]
    public void Startup_RequireHttps_OnlyNeedsAPublicHostInProduction(string environment, string? publicHost)
    {
        // Arrange
        var settings = new Dictionary<string, string?> { ["Security:RequireHttps"] = "true" };
        if (publicHost is not null)
        {
            settings["Security:PublicHost"] = publicHost;
        }

        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { Environment = environment, Settings = settings });

        // Act
        TestDelegate start = () => factory.CreateClient().Dispose();

        // Assert
        Assert.That(start, Throws.Nothing);
    }

    [Test]
    public void Startup_ProductionWithoutRequireHttps_StartsWithoutAPublicHost()
    {
        // Arrange：本機的 8080 沒有設 RequireHttps，不能被新的檢查擋下來。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());

        // Act
        TestDelegate start = () => factory.CreateClient().Dispose();

        // Assert
        Assert.That(start, Throws.Nothing);
    }

    [Test]
    public void Startup_ProductionWithoutACertificate_WarnsHowToFixIt()
    {
        // Arrange
        var logs = new CapturingLoggerProvider();
        var options = new ApiFactoryOptions { ConfigureServices = services => services.AddSingleton<ILoggerProvider>(logs) };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), options);

        // Act
        factory.CreateClient().Dispose();
        var warning = logs.Entries.Single(entry => entry.Category == KeysNotEncryptedCategory && entry.Level == LogLevel.Warning).Message;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(warning, Does.Contain("DataProtection:CertificatePath"));
            Assert.That(warning, Does.Contain("DataProtection:RequireCertificate"));
            Assert.That(warning, Does.Contain("AI 供應商的金鑰與寄信密碼"));
        }
    }

    [Test]
    public void Startup_CertificatePasswordWrong_FailsWithoutLoggingThePassword()
    {
        // Arrange
        using var certificates = new TestCertificates();
        var settings = new Dictionary<string, string?>
        {
            ["DataProtection:CertificatePath"] = certificates.Create("example-keys"),
            ["DataProtection:CertificatePassword"] = "wrong-password-5e1d",
        };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { Settings = settings });

        // Act
        TestDelegate start = () => factory.CreateClient().Dispose();

        // Assert
        Assert.That(start, Throws.InvalidOperationException
            .With.Message.Contains("DataProtection:CertificatePassword")
            .And.Message.Not.Contains("wrong-password-5e1d"));
    }

    [Test]
    public void Startup_CertificateWithoutAPrivateKey_Fails()
    {
        // Arrange：只有公鑰的話，加密得了，卻解不開自己存的金鑰。
        using var certificates = new TestCertificates();
        var settings = new Dictionary<string, string?>
        {
            ["DataProtection:CertificatePath"] = certificates.CreateWithoutPrivateKey("example-public"),
            ["DataProtection:CertificatePassword"] = TestCertificates.Password,
        };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { Settings = settings });

        // Act
        TestDelegate start = () => factory.CreateClient().Dispose();

        // Assert
        Assert.That(start, Throws.InvalidOperationException.With.Message.Contains("私鑰"));
    }

    [Test]
    public void Startup_PreviousCertificateMissing_Fails()
    {
        // Arrange
        using var certificates = new TestCertificates();
        var settings = new Dictionary<string, string?>
        {
            ["DataProtection:CertificatePath"] = certificates.Create("example-current"),
            ["DataProtection:CertificatePassword"] = TestCertificates.Password,
            ["DataProtection:PreviousCertificatePaths:0"] = Path.Combine(certificates.Directory, "missing.pfx"),
        };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { Settings = settings });

        // Act
        TestDelegate start = () => factory.CreateClient().Dispose();

        // Assert
        Assert.That(start, Throws.InvalidOperationException.With.Message.Contains("DataProtection:PreviousCertificatePaths"));
    }

    [TestCase(true, TestName = "Startup_ProductionWithACertificate_DoesNotWarnAboutTheKeys")]
    [TestCase(false, TestName = "Startup_ProductionWithoutACertificate_WarnsThatTheKeysAreNotEncrypted")]
    public void Startup_Production_WarnsOnlyWithoutACertificate(bool withCertificate)
    {
        // Arrange：沒設憑證照樣能用，只是金鑰是明文；正式環境記一筆警告，部署的人才看得到。
        using var certificates = new TestCertificates();
        var settings = withCertificate
            ? new Dictionary<string, string?>
            {
                ["DataProtection:CertificatePath"] = certificates.Create("example-keys"),
                ["DataProtection:CertificatePassword"] = TestCertificates.Password,
            }
            : [];
        var logs = new CapturingLoggerProvider();
        var options = new ApiFactoryOptions
        {
            Settings = settings,
            ConfigureServices = services => services.AddSingleton<ILoggerProvider>(logs),
        };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), options);

        // Act
        factory.CreateClient().Dispose();
        var warnings = logs.Entries.Where(entry => entry.Category == KeysNotEncryptedCategory && entry.Level == LogLevel.Warning);

        // Assert
        Assert.That(warnings.Count(), Is.EqualTo(withCertificate ? 0 : 1));
    }

    [Test]
    public void Startup_DevelopmentWithoutACertificate_DoesNotWarn()
    {
        // Arrange：本機開發不需要憑證，警告只是雜訊。
        var logs = new CapturingLoggerProvider();
        var options = new ApiFactoryOptions
        {
            Environment = "Development",
            ConfigureServices = services => services.AddSingleton<ILoggerProvider>(logs),
        };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), options);

        // Act
        factory.CreateClient().Dispose();

        // Assert
        Assert.That(logs.Entries.Where(entry => entry.Category == KeysNotEncryptedCategory), Is.Empty);
    }

    [Test]
    public void Startup_ProductionSettings_WriteJsonLogsWithTimeZone()
    {
        // Arrange：雲端與 compose 都靠一行一筆的 JSON 挑出錯誤；寫在 appsettings.Production.json，不必各自設定。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());

        // Act
        var configuration = factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(configuration["Logging:Console:FormatterName"], Is.EqualTo("json"));
            Assert.That(configuration["Logging:Console:FormatterOptions:IncludeScopes"], Is.EqualTo("True").IgnoreCase);
            Assert.That(configuration["Logging:Console:FormatterOptions:TimestampFormat"], Is.EqualTo("yyyy-MM-dd'T'HH:mm:ss.fffzzz"));
        }
    }

    private static TestCaseData Invalid(string setting, params (string Key, string Value)[] values) =>
        new(setting, values.ToDictionary(value => value.Key, value => (string?)value.Value));
}
