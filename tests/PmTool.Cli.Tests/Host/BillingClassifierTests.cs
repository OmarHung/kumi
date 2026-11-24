using PmTool.Cli.Host;

namespace PmTool.Cli.Tests.Host;

[TestFixture]
public sealed class BillingClassifierTests
{
    [TestCase("""{"loggedIn": true, "authMethod": "claude.ai", "subscriptionType": "max"}""", Billing.Subscription)]
    [TestCase("""{"loggedIn": true, "authMethod": "api_key", "apiKeySource": "/login managed key"}""", Billing.Api)]
    [TestCase("""{"loggedIn": true, "authMethod": "oauth"}""", Billing.Unknown)]
    [TestCase("""{"loggedIn": true, "authMethod": "bedrock"}""", Billing.Unknown)]
    [TestCase("""{"loggedIn": true}""", Billing.Unknown)]
    public void Claude_ReadsTheBillingFromTheAuthMethod(string json, string expected)
    {
        // Act
        var status = BillingClassifier.Claude(0, json);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(status.LoggedIn, Is.True);
            Assert.That(status.Billing, Is.EqualTo(expected));
        }
    }

    [Test]
    public void Claude_WhenLoggedInIsFalse_IsLoggedOutWhateverTheMethodSays()
    {
        // Act
        var status = BillingClassifier.Claude(1, """{"loggedIn": false, "authMethod": "api_key"}""");

        // Assert
        Assert.That(status, Is.EqualTo(LoginStatus.LoggedOut));
    }

    [TestCase(0, "not json", true)]
    [TestCase(1, "not json", false)]
    [TestCase(0, "[]", true)]
    public void Claude_WhenTheOutputIsNotAnAuthObject_TrustsOnlyTheExitCodeAndNeverShareableBilling(int exit, string output, bool loggedIn)
    {
        // Act
        var status = BillingClassifier.Claude(exit, output);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(status.LoggedIn, Is.EqualTo(loggedIn));
            Assert.That(status.Billing, Is.EqualTo(Billing.Unknown));
        }
    }

    [Test]
    public void Claude_FingerprintDiffersWhenTheAccountDiffersAndNeverContainsTheAccountText()
    {
        // Act
        var first = BillingClassifier.Claude(0, """{"loggedIn": true, "authMethod": "api_key", "orgId": "org-1", "email": "a@example.com"}""");
        var same = BillingClassifier.Claude(0, """{"loggedIn": true, "authMethod": "api_key", "orgId": "org-1", "email": "a@example.com"}""");
        var other = BillingClassifier.Claude(0, """{"loggedIn": true, "authMethod": "api_key", "orgId": "org-2", "email": "a@example.com"}""");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Fingerprint, Is.EqualTo(same.Fingerprint));
            Assert.That(first.Fingerprint, Is.Not.EqualTo(other.Fingerprint));
            Assert.That(first.Fingerprint, Does.Not.Contain("example.com").And.Not.Contain("org-1"));
        }
    }

    [TestCase("Logged in using ChatGPT", 0, true, Billing.Subscription)]
    [TestCase("Logged in using an API key - sk-proj-***abcd", 0, true, Billing.Api)]
    [TestCase("Logged in somehow", 0, true, Billing.Unknown)]
    [TestCase("Not logged in", 1, false, Billing.Unknown)]
    [TestCase("Logged in using an API key", 1, false, Billing.Unknown)]
    public void Codex_ReadsTheBillingFromTheStatusMessage(string output, int exit, bool loggedIn, string billing)
    {
        // Act
        var status = BillingClassifier.Codex(exit, output);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(status.LoggedIn, Is.EqualTo(loggedIn));
            Assert.That(status.Billing, Is.EqualTo(billing));
        }
    }

    [Test]
    public void Problem_ForSubscriptionAndUnknownBilling_TellsTheOwnerHowToFixItWithoutSecrets()
    {
        // Act
        var subscription = HostEngineProbe.ProblemOf("claude_code", new LoginStatus(true, Billing.Subscription, null));
        var unknown = HostEngineProbe.ProblemOf("codex", new LoginStatus(true, Billing.Unknown, null));
        var api = HostEngineProbe.ProblemOf("codex", new LoginStatus(true, Billing.Api, null));
        var loggedOut = HostEngineProbe.ProblemOf("claude_code", LoginStatus.LoggedOut);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(subscription, Does.Contain("訂閱").And.Contain("Anthropic Console"));
            Assert.That(unknown, Does.Contain("OpenAI API 金鑰"));
            Assert.That(api, Is.Null);
            Assert.That(loggedOut, Does.Contain("kumi host login claude-code"));
        }
    }

    [Test]
    public void CodexFingerprint_IgnoresRefreshedTokensButNoticesAnotherAccountOrKey()
    {
        // Arrange
        static string Of(string json)
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            return BillingClassifier.CodexFingerprint(document.RootElement)!;
        }

        var apiKey = Of("""{"auth_mode":"apikey","OPENAI_API_KEY":"sk-example-1","tokens":null,"last_refresh":"2026-11-01T00:00:00Z"}""");
        var refreshed = Of("""{"auth_mode":"apikey","OPENAI_API_KEY":"sk-example-1","tokens":null,"last_refresh":"2026-11-24T00:00:00Z"}""");
        var otherKey = Of("""{"auth_mode":"apikey","OPENAI_API_KEY":"sk-example-2","tokens":null}""");
        var chatgpt = Of("""{"auth_mode":"chatgpt","OPENAI_API_KEY":null,"tokens":{"access_token":"a","refresh_token":"r","account_id":"acct-1"}}""");
        var chatgptRefreshed = Of("""{"auth_mode":"chatgpt","OPENAI_API_KEY":null,"tokens":{"access_token":"b","refresh_token":"s","account_id":"acct-1"}}""");

        // Assert
        Assert.That(refreshed, Is.EqualTo(apiKey));
        Assert.That(otherKey, Is.Not.EqualTo(apiKey));
        Assert.That(chatgptRefreshed, Is.EqualTo(chatgpt));
        Assert.That(apiKey, Does.Not.Contain("sk-example"));
    }
}
