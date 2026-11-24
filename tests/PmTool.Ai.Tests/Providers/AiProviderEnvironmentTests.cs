using PmTool.Ai.Providers;

namespace PmTool.Ai.Tests.Providers;

/// <summary>
/// 主機的環境變數不能影響我們建立的 client：SDK 會自己讀 ANTHROPIC_BASE_URL、GOOGLE_GENAI_USE_VERTEXAI 這類變數，
/// 環境剛好設了，組織的金鑰就會送到別的主機。這裡把它們全設成「壞」的值，確認請求的主機與金鑰還是我們給的。
/// 環境變數是整個程序共用的，所以這個類別不跟別的測試平行跑，測完還原。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class AiProviderEnvironmentTests
{
    private const string Evil = "https://evil.example.com";

    private static readonly string[] HostileVariables =
    [
        "ANTHROPIC_BASE_URL", "ANTHROPIC_AUTH_TOKEN", "ANTHROPIC_API_KEY", "ANTHROPIC_CUSTOM_HEADERS", "ANTHROPIC_PROFILE",
        "ANTHROPIC_CONFIG_DIR", "ANTHROPIC_IDENTITY_TOKEN", "ANTHROPIC_OPEN_TELEMETRY", "ANTHROPIC_ORGANIZATION_ID",
        "ANTHROPIC_WORKSPACE_ID", "ANTHROPIC_SERVICE_ACCOUNT_ID", "ANTHROPIC_FEDERATION_RULE_ID",
        "OPENAI_BASE_URL", "OPENAI_API_KEY", "OPENAI_API_BASE",
        "GOOGLE_GENAI_USE_VERTEXAI", "GOOGLE_GENAI_USE_ENTERPRISE", "GOOGLE_CLOUD_PROJECT", "GOOGLE_CLOUD_LOCATION",
        "GOOGLE_API_KEY", "GEMINI_API_KEY", "GOOGLE_GEMINI_BASE_URL", "GOOGLE_VERTEX_BASE_URL",
    ];

    private Dictionary<string, string?> _saved = [];

    [SetUp]
    public void PoisonTheEnvironment()
    {
        _saved = HostileVariables.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        foreach (var name in HostileVariables)
        {
            var value = name switch
            {
                _ when name.EndsWith("BASE_URL", StringComparison.Ordinal) || name == "OPENAI_API_BASE" => Evil,
                "GOOGLE_GENAI_USE_VERTEXAI" or "GOOGLE_GENAI_USE_ENTERPRISE" => "true",
                "ANTHROPIC_CUSTOM_HEADERS" => "X-Evil: evil-header",
                "ANTHROPIC_CONFIG_DIR" => "/nonexistent-evil",
                _ => "evil-" + name.ToLowerInvariant(),
            };
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    [TearDown]
    public void RestoreTheEnvironment()
    {
        foreach (var (name, value) in _saved)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    [TestCase(AiProviderKind.Anthropic, "api.anthropic.com", "sk-ant-ours")]
    [TestCase(AiProviderKind.Openai, "api.openai.com", "sk-openai-ours")]
    [TestCase(AiProviderKind.Google, "generativelanguage.googleapis.com", "AIza-ours")]
    public async Task Discover_HostileEnvironment_StillGoesToTheProviderHostWithOurKey(AiProviderKind kind, string host, string key)
    {
        // Arrange
        var handler = new RecordingHandler(_ => RecordingHandler.Json("""{"data":[],"models":[],"has_more":false}"""));
        using var gateway = new AiProviderGateway(new AiModuleOptions(), handler);

        // Act
        await gateway.DiscoverAsync(new AiEndpoint(kind, null, key), CancellationToken.None);

        // Assert
        AssertOnlyOurs(handler, host, key);
    }

    [TestCase(AiProviderKind.Anthropic, "api.anthropic.com", "sk-ant-ours", "claude-opus-5-5")]
    [TestCase(AiProviderKind.Openai, "api.openai.com", "sk-openai-ours", "gpt-5.2")]
    [TestCase(AiProviderKind.Google, "generativelanguage.googleapis.com", "AIza-ours", "gemini-3-pro")]
    public async Task Chat_HostileEnvironment_StillGoesToTheProviderHostWithOurKey(
        AiProviderKind kind, string host, string key, string model)
    {
        // Arrange
        var handler = new RecordingHandler(_ => RecordingHandler.EventStream("data: [DONE]\n\n"));
        using var gateway = new AiProviderGateway(new AiModuleOptions(), handler);
        using var client = gateway.CreateChatClient(new AiEndpoint(kind, null, key), model);

        // Act
        try
        {
            await foreach (var _ in client.GetStreamingResponseAsync(
                [new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "hi")], AiRequestOptions.Create(kind, model, null)))
            {
            }
        }
        catch (Exception)
        {
            // 假的回應不是各家的格式，讀不下去沒關係；要看的是送出去的請求。
        }

        // Assert
        AssertOnlyOurs(handler, host, key);
    }

    private static void AssertOnlyOurs(RecordingHandler handler, string host, string key)
    {
        var sent = handler.Requests;
        Assert.That(sent, Is.Not.Empty);
        foreach (var request in sent)
        {
            var everything = request.Uri + " " + string.Join(" ", request.Headers.Select(h => $"{h.Key}={h.Value}")) + " " + request.Body;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(request.Uri.Host, Is.EqualTo(host));
                Assert.That(everything, Does.Not.Contain("evil"));
                Assert.That(everything, Does.Contain(key));
            }
        }
    }
}
