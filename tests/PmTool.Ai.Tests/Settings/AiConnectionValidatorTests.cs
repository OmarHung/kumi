using Microsoft.AspNetCore.DataProtection;
using PmTool.Ai.Settings;

namespace PmTool.Ai.Tests.Settings;

/// <summary>連線欄位的檢查（第 4.6 節的表）與金鑰加密。純邏輯，不碰資料庫。</summary>
[TestFixture]
public sealed class AiConnectionValidatorTests
{
    private static string[] PathsOf(AiConnectionInput input, bool allowPrivate = false) =>
        [.. AiConnectionValidator.Validate(input, null, allowPrivate).Failure?.Details.Select(d => d.Path) ?? []];

    [TestCase("")]
    [TestCase("   ")]
    public void Name_Empty_IsRequired(string name)
    {
        // Arrange
        var input = AiTestData.Anthropic(name);

        // Act
        var result = AiConnectionValidator.Validate(input, null, false);

        // Assert
        Assert.That(result.Failure!.Details.Single(), Is.EqualTo(new PmTool.Abstractions.FailureDetail(AiErrorCodes.FieldRequired, "name", "要填連線的名稱。")));
    }

    [Test]
    public void Name_TooLong_OrWithControlCharacters_IsInvalid()
    {
        // Arrange
        var tooLong = AiTestData.Anthropic(new string('a', AiRules.MaxNameLength + 1));
        var withNul = AiTestData.Anthropic("bad\0name");
        var withNewline = AiTestData.Anthropic("bad\nname");

        // Act & Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(PathsOf(tooLong), Is.EqualTo(new[] { "name" }));
            Assert.That(PathsOf(withNul), Is.EqualTo(new[] { "name" }));
            Assert.That(PathsOf(withNewline), Is.EqualTo(new[] { "name" }));
        }
    }

    [Test]
    public void Name_IsTrimmedAndKeyedCaseInsensitively()
    {
        // Arrange & Act
        var result = AiConnectionValidator.Validate(AiTestData.Anthropic("  Team Key  "), null, false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Name, Is.EqualTo("Team Key"));
            Assert.That(result.Value.NameKey, Is.EqualTo("TEAM KEY"));
        }
    }

    [TestCase("http://ollama.example.com:11434/v1", "http://ollama.example.com:11434/v1")]
    [TestCase("https://openrouter.ai/api/v1/", "https://openrouter.ai/api/v1")]
    [TestCase("  https://proxy.example.com//  ", "https://proxy.example.com")]
    public void BaseUrl_Valid_IsNormalizedWithoutTheTrailingSlash(string raw, string expected)
    {
        // Arrange
        var input = AiTestData.Compatible(baseUrl: raw);

        // Act
        var result = AiConnectionValidator.Validate(input, null, false);

        // Assert
        Assert.That(result.Value.BaseUrl, Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("ollama.example.com")]
    [TestCase("ftp://ollama.example.com/v1")]
    [TestCase("http://user:pass@ollama.example.com/v1")]
    [TestCase("http://ollama.example.com/v1?key=1")]
    [TestCase("http://ollama.example.com/v1#frag")]
    [TestCase("http://ollama.example.com/\0")]
    public void BaseUrl_Malformed_FailsOnTheBaseUrlPath(string raw)
    {
        // Arrange
        var input = AiTestData.Compatible(baseUrl: raw);

        // Act & Assert
        Assert.That(PathsOf(input), Is.EqualTo(new[] { "baseUrl" }));
    }

    [Test]
    public void BaseUrl_TooLong_IsInvalid()
    {
        // Arrange
        var input = AiTestData.Compatible(baseUrl: "http://example.com/" + new string('a', AiRules.MaxBaseUrlLength));

        // Act & Assert
        Assert.That(PathsOf(input), Is.EqualTo(new[] { "baseUrl" }));
    }

    [TestCase("http://127.0.0.1:11434/v1")]
    [TestCase("http://10.1.2.3/v1")]
    [TestCase("http://169.254.169.254/latest")]
    [TestCase("http://[::1]:8080/v1")]
    [TestCase("http://192.168.0.5/v1")]
    public void BaseUrl_BlockedIpLiteral_IsEndpointNotAllowed(string raw)
    {
        // Arrange
        var input = AiTestData.Compatible(baseUrl: raw);

        // Act
        var detail = AiConnectionValidator.Validate(input, null, allowPrivateEndpoints: false).Failure!.Details.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(detail.Code, Is.EqualTo(AiErrorCodes.EndpointNotAllowed));
            Assert.That(detail.Path, Is.EqualTo("baseUrl"));
        }
    }

    [Test]
    public void BaseUrl_BlockedIpLiteral_IsAcceptedWhenPrivateEndpointsAreAllowed()
    {
        // Arrange
        var input = AiTestData.Compatible(baseUrl: "http://10.1.2.3:11434/v1");

        // Act
        var result = AiConnectionValidator.Validate(input, null, allowPrivateEndpoints: true);

        // Assert
        Assert.That(result.Succeeded, Is.True);
    }

    [Test]
    public void BaseUrl_OnAFixedHostProvider_IsIgnoredAndStoredAsNull()
    {
        // Arrange
        var input = new AiConnectionInput("Anthropic", AiProviderKind.Anthropic, "http://evil.example.com", "sk-ant-key-0001", [], []);

        // Act
        var result = AiConnectionValidator.Validate(input, null, false);

        // Assert
        Assert.That(result.Value.BaseUrl, Is.Null);
    }

    [TestCase(AiProviderKind.Anthropic)]
    [TestCase(AiProviderKind.Openai)]
    [TestCase(AiProviderKind.Google)]
    public void ApiKey_FixedHostProvidersRequireOneWhenCreating(AiProviderKind kind)
    {
        // Arrange
        var input = new AiConnectionInput("x", kind, null, null, [], []);

        // Act & Assert
        Assert.That(PathsOf(input), Is.EqualTo(new[] { "apiKey" }));
    }

    [Test]
    public void ApiKey_CompatibleEndpointMayHaveNone()
    {
        // Arrange & Act
        var result = AiConnectionValidator.Validate(AiTestData.Compatible(apiKey: null), null, false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Value.KeyChange, Is.EqualTo(ApiKeyChange.Clear));
        }
    }

    [TestCase("has space")]
    [TestCase("tab\tkey")]
    [TestCase("nul\0key")]
    public void ApiKey_WithWhitespaceOrControlCharacters_IsInvalid(string key)
    {
        // Arrange
        var input = AiTestData.Anthropic(apiKey: key);

        // Act & Assert
        Assert.That(PathsOf(input), Is.EqualTo(new[] { "apiKey" }));
    }

    [Test]
    public void ApiKey_TooLong_IsInvalid_AndSurroundingWhitespaceIsTrimmed()
    {
        // Arrange
        var tooLong = AiTestData.Anthropic(apiKey: new string('k', AiRules.MaxApiKeyLength + 1));
        var padded = AiTestData.Anthropic(apiKey: "  sk-ant-key-0001\n");

        // Act
        var trimmed = AiConnectionValidator.Validate(padded, null, false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(PathsOf(tooLong), Is.EqualTo(new[] { "apiKey" }));
            Assert.That(trimmed.Value.NewApiKey, Is.EqualTo("sk-ant-key-0001"));
        }
    }

    [Test]
    public void Models_InvalidIdsAndTooMany_ReportTheirPaths()
    {
        // Arrange
        var input = AiTestData.Anthropic(
            enabled: ["ok", "has space", ""],
            custom: [.. Enumerable.Range(0, AiRules.MaxCustomModels + 1).Select(i => $"m{i}")]);

        // Act & Assert
        Assert.That(PathsOf(input), Is.EquivalentTo(new[] { "enabledModels[1]", "enabledModels[2]", "customModels" }));
    }

    [Test]
    public void Models_ANullElementFromTheJsonBody_IsInvalidNotACrash()
    {
        // Arrange
        var input = new AiConnectionInput("x", AiProviderKind.Anthropic, null, "sk-ant-key-0001", [null!], []);

        // Act & Assert
        Assert.That(PathsOf(input), Is.EqualTo(new[] { "enabledModels[0]" }));
    }

    [Test]
    public void Models_Duplicates_AreRemoved()
    {
        // Arrange & Act
        var result = AiConnectionValidator.Validate(AiTestData.Anthropic(enabled: ["a", "a", "b"], custom: ["c", "c"]), null, false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.EnabledModels, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(result.Value.CustomModels, Is.EqualTo(new[] { "c" }));
        }
    }

    [Test]
    public void ToString_NeverShowsTheKey()
    {
        // Arrange
        var input = AiTestData.Anthropic(apiKey: "sk-ant-key-0001");
        var validated = AiConnectionValidator.Validate(input, null, false).Value;

        // Act & Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(input.ToString(), Does.Not.Contain("sk-ant-key-0001"));
            Assert.That(validated.ToString(), Does.Not.Contain("sk-ant-key-0001"));
        }
    }

    [Test]
    public void Protector_RoundTrips_AndCannotReadWhatAnotherPurposeProtected()
    {
        // Arrange
        var provider = new EphemeralDataProtectionProvider();
        var protector = new AiApiKeyProtector(provider);
        var stored = protector.Protect("sk-ant-key-0001");
        var foreign = provider.CreateProtector("PmTool.Notifications.EmailSettings.Password.v1").Protect("sk-ant-key-0001");

        // Act
        var ok = protector.TryUnprotect(stored, out var plain);
        var wrongPurpose = protector.TryUnprotect(foreign, out _);
        var garbage = protector.TryUnprotect("not-a-payload", out _);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored, Does.Not.Contain("sk-ant-key-0001"));
            Assert.That(ok, Is.True);
            Assert.That(plain, Is.EqualTo("sk-ant-key-0001"));
            Assert.That(wrongPurpose, Is.False);
            Assert.That(garbage, Is.False);
        }
    }
}
