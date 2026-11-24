using System.Net;
using PmTool.Ai.Providers;

namespace PmTool.Ai.Tests.Providers;

/// <summary>讀模型清單（第 4.2 節）：假的回應餵給三家 SDK 與相容端點，確認請求、過濾、排序與失敗說明。</summary>
[TestFixture]
public sealed class AiDiscoveryWireTests
{
    private static string AnthropicModel(string id, string name, string? effort) =>
        $$"""{"id":"{{id}}","type":"model","display_name":"{{name}}","created_at":"2026-01-01T00:00:00Z"{{(effort is null ? "" : $",\"capabilities\":{{\"effort\":{effort}}}")}}}""";

    private static AiProviderGateway GatewayFor(RecordingHandler handler, AiModuleOptions? options = null) => new(options ?? new AiModuleOptions(), handler);

    [Test]
    public async Task Anthropic_ReadsEveryPageAndTheEffortCapabilities()
    {
        // Arrange
        const string allEfforts = """{"supported":true,"low":{"supported":true},"medium":{"supported":true},"high":{"supported":true},"xhigh":{"supported":true},"max":{"supported":true}}""";
        const string noMax = """{"supported":true,"low":{"supported":true},"medium":{"supported":true},"high":{"supported":true},"xhigh":{"supported":false},"max":{"supported":false}}""";
        var handler = new RecordingHandler(request => request.Uri.Query.Contains("after_id=m1", StringComparison.Ordinal)
            ? RecordingHandler.Json($$"""{"data":[{{AnthropicModel("claude-haiku-4", "Claude Haiku 4", null)}}],"has_more":false,"first_id":"m3","last_id":"m3"}""")
            : RecordingHandler.Json($$"""{"data":[{{AnthropicModel("claude-opus-5-5", "Claude Opus 5.5", allEfforts)}},{{AnthropicModel("claude-sonnet-4", "Claude Sonnet 4", noMax)}}],"has_more":true,"first_id":"m1","last_id":"m1"}"""));
        using var gateway = GatewayFor(handler);

        // Act
        var models = await gateway.DiscoverAsync(new AiEndpoint(AiProviderKind.Anthropic, null, "sk-ant-test"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(handler.Requests, Has.Count.EqualTo(2));
            Assert.That(handler.Requests[0].Uri.AbsolutePath, Is.EqualTo("/v1/models"));
            Assert.That(models.Select(m => m.Id), Is.EqualTo(new[] { "claude-haiku-4", "claude-opus-5-5", "claude-sonnet-4" }));
            Assert.That(models.Single(m => m.Id == "claude-opus-5-5").DisplayName, Is.EqualTo("Claude Opus 5.5"));
            Assert.That(models.Single(m => m.Id == "claude-opus-5-5").Efforts, Is.EqualTo(new[] { AiEffort.Low, AiEffort.Medium, AiEffort.High, AiEffort.Xhigh, AiEffort.Max }));
            Assert.That(models.Single(m => m.Id == "claude-sonnet-4").Efforts, Is.EqualTo(new[] { AiEffort.Low, AiEffort.Medium, AiEffort.High }));
            Assert.That(models.Single(m => m.Id == "claude-haiku-4").Efforts, Is.Empty);
        }
    }

    [Test]
    public async Task OpenAi_KeepsOnlyChatModelsWithoutTheExcludedKinds()
    {
        // Arrange
        string[] ids =
        [
            "gpt-5.2", "gpt-5-mini", "gpt-4o", "chatgpt-4o-latest", "o3", "o4-mini", "o1-pro",
            "gpt-4o-audio-preview", "gpt-realtime", "gpt-4o-transcribe", "gpt-4o-mini-tts", "gpt-image-1", "gpt-4o-search-preview",
            "text-embedding-3-large", "omni-moderation-latest", "dall-e-3", "whisper-1", "davinci-002",
        ];
        var json = $$"""{"object":"list","data":[{{string.Join(",", ids.Select(id => $$"""{"id":"{{id}}","object":"model","created":1,"owned_by":"openai"}"""))}}]}""";
        var handler = new RecordingHandler(_ => RecordingHandler.Json(json));
        using var gateway = GatewayFor(handler);

        // Act
        var models = await gateway.DiscoverAsync(new AiEndpoint(AiProviderKind.Openai, null, "sk-openai-test"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(handler.Single.Uri.AbsolutePath, Is.EqualTo("/v1/models"));
            Assert.That(models.Select(m => m.Id), Is.EqualTo(new[] { "chatgpt-4o-latest", "gpt-4o", "gpt-5-mini", "gpt-5.2", "o1-pro", "o3", "o4-mini" }));
            Assert.That(models.Single(m => m.Id == "gpt-5.2").Efforts, Is.EqualTo(new[] { AiEffort.None, AiEffort.Low, AiEffort.Medium, AiEffort.High, AiEffort.Xhigh }));
            Assert.That(models.Single(m => m.Id == "gpt-4o").Efforts, Is.Empty);
            Assert.That(models.Single(m => m.Id == "gpt-4o").DisplayName, Is.EqualTo("gpt-4o"));
        }
    }

    [Test]
    public async Task Google_KeepsGenerateContentModelsAndStripsTheModelsPrefix()
    {
        // Arrange
        const string json = """
            {"models":[
              {"name":"models/gemini-3-pro","displayName":"Gemini 3 Pro","supportedGenerationMethods":["generateContent","countTokens"],"thinking":true},
              {"name":"models/gemini-2.5-flash","displayName":"Gemini 2.5 Flash","supportedGenerationMethods":["generateContent"],"thinking":true},
              {"name":"models/text-embedding-004","displayName":"Embedding","supportedGenerationMethods":["embedContent"]},
              {"name":"models/gemini-3-flash-lite","supportedGenerationMethods":["generateContent"]}
            ]}
            """;
        var handler = new RecordingHandler(_ => RecordingHandler.Json(json));
        using var gateway = GatewayFor(handler);

        // Act
        var models = await gateway.DiscoverAsync(new AiEndpoint(AiProviderKind.Google, null, "AIza-test"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(handler.Requests[0].Uri.Host, Is.EqualTo("generativelanguage.googleapis.com"));
            Assert.That(models.Select(m => m.Id), Is.EqualTo(new[] { "gemini-2.5-flash", "gemini-3-flash-lite", "gemini-3-pro" }));
            Assert.That(models.Single(m => m.Id == "gemini-3-pro").DisplayName, Is.EqualTo("Gemini 3 Pro"));
            Assert.That(models.Single(m => m.Id == "gemini-3-pro").Efforts, Is.EqualTo(new[] { AiEffort.Low, AiEffort.Medium, AiEffort.High }));
            Assert.That(models.Single(m => m.Id == "gemini-2.5-flash").Efforts, Is.Empty);
            Assert.That(models.Single(m => m.Id == "gemini-3-flash-lite").DisplayName, Is.EqualTo("gemini-3-flash-lite"));
            Assert.That(models.Single(m => m.Id == "gemini-3-flash-lite").Efforts, Is.Empty);
        }
    }

    [Test]
    public async Task Compatible_ReadsModelsFromTheBaseUrlKeepsEverythingAndSendsNoEfforts()
    {
        // Arrange
        var handler = new RecordingHandler(_ => RecordingHandler.Json(
            """{"object":"list","data":[{"id":"llama3:8b","object":"model","created":1,"owned_by":"library"},{"id":"embed-model","object":"model","created":1,"owned_by":"library"}]}"""));
        using var gateway = GatewayFor(handler);

        // Act
        var models = await gateway.DiscoverAsync(
            new AiEndpoint(AiProviderKind.OpenaiCompatible, "http://ollama.example.com:11434/v1", null), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(handler.Single.Uri.ToString(), Is.EqualTo("http://ollama.example.com:11434/v1/models"));
            Assert.That(models.Select(m => m.Id), Is.EqualTo(new[] { "embed-model", "llama3:8b" }));
            Assert.That(models.SelectMany(m => m.Efforts), Is.Empty);
        }
    }

    [Test]
    public async Task Compatible_NotFound_IsAnEmptyCatalogNotAFailure()
    {
        // Arrange
        var handler = new RecordingHandler(_ => RecordingHandler.Json("""{"error":{"message":"not found"}}""", HttpStatusCode.NotFound));
        using var gateway = GatewayFor(handler);

        // Act
        var models = await gateway.DiscoverAsync(
            new AiEndpoint(AiProviderKind.OpenaiCompatible, "http://ollama.example.com/v1", null), CancellationToken.None);

        // Assert
        Assert.That(models, Is.Empty);
    }

    [Test]
    public async Task OpenAi_NotFound_IsAFailureWithTheNotFoundReason()
    {
        // Arrange
        var handler = new RecordingHandler(_ => RecordingHandler.Json("""{"error":{"message":"nope"}}""", HttpStatusCode.NotFound));
        using var gateway = GatewayFor(handler);

        // Act & Assert
        var failure = Assert.ThrowsAsync<AiProviderException>(() =>
            gateway.DiscoverAsync(new AiEndpoint(AiProviderKind.Openai, null, "sk-openai-test"), CancellationToken.None));
        Assert.That(failure.Message, Does.StartWith("找不到"));
    }

    [TestCase(AiProviderKind.Anthropic, 401, "金鑰不對")]
    [TestCase(AiProviderKind.Openai, 401, "金鑰不對")]
    [TestCase(AiProviderKind.Google, 403, "金鑰不對")]
    [TestCase(AiProviderKind.OpenaiCompatible, 401, "金鑰不對")]
    [TestCase(AiProviderKind.Anthropic, 429, "太頻繁")]
    [TestCase(AiProviderKind.Openai, 500, "供應商那邊出錯")]
    [TestCase(AiProviderKind.Google, 429, "太頻繁")]
    public async Task Failures_AreDescribedInOurOwnWordsPerProvider(AiProviderKind kind, int status, string expectedStart)
    {
        // Arrange
        var body = status == 401 ? """{"error":{"message":"Incorrect API key provided: sk-secret-0000-1111"}}""" : """{"error":{"message":"slow down","status":"RESOURCE_EXHAUSTED"}}""";
        var handler = new RecordingHandler(_ => RecordingHandler.Json(body, (HttpStatusCode)status));
        using var gateway = GatewayFor(handler);
        var endpoint = new AiEndpoint(kind, kind == AiProviderKind.OpenaiCompatible ? "http://ollama.example.com/v1" : null, "sk-secret-0000-1111");

        // Act
        var failure = Assert.ThrowsAsync<AiProviderException>(() => gateway.DiscoverAsync(endpoint, CancellationToken.None));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure.Message, Does.Contain(expectedStart));
            Assert.That(failure.Message, Does.Not.Contain("sk-secret-0000-1111"));
            Assert.That(failure.Message.Length, Is.LessThanOrEqualTo(AiRules.MaxErrorLength));
        }
    }

    [Test]
    public async Task Discover_NetworkFailure_NamesTheHost()
    {
        // Arrange
        var handler = new RecordingHandler(_ => throw new HttpRequestException("Name or service not known"));
        using var gateway = GatewayFor(handler);

        // Act
        var failure = Assert.ThrowsAsync<AiProviderException>(() => gateway.DiscoverAsync(
            new AiEndpoint(AiProviderKind.OpenaiCompatible, "http://ollama.example.com:11434/v1", null), CancellationToken.None));

        // Assert
        Assert.That(failure.Message, Is.EqualTo("連不上 ollama.example.com。"));
    }

    [Test]
    public async Task Discover_CallerCancels_PropagatesTheCancellationInsteadOfAFailure()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var handler = new RecordingHandler(_ => throw new OperationCanceledException());
        using var gateway = GatewayFor(handler);
        await cts.CancelAsync();

        // Act & Assert
        Assert.CatchAsync<OperationCanceledException>(() => gateway.DiscoverAsync(
            new AiEndpoint(AiProviderKind.Openai, null, "sk-openai-test"), cts.Token));
    }

    [Test]
    public async Task Discover_RejectsOverlongOrControlCharacterIdsAndCapsTheCatalog()
    {
        // Arrange
        var many = Enumerable.Range(0, AiRules.MaxCatalogModels + 50).Select(i => $"gpt-5-m{i:D4}");
        var bad = new[] { "gpt-" + new string('x', AiRules.MaxModelIdLength), "gpt-bad\\u0007id" };
        var data = string.Join(",", many.Concat(bad).Select(id => $$"""{"id":"{{id}}","object":"model","created":1,"owned_by":"o"}"""));
        var handler = new RecordingHandler(_ => RecordingHandler.Json($$"""{"object":"list","data":[{{data}}]}"""));
        using var gateway = GatewayFor(handler);

        // Act
        var models = await gateway.DiscoverAsync(new AiEndpoint(AiProviderKind.Openai, null, "sk-openai-test"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(models, Has.Count.EqualTo(AiRules.MaxCatalogModels));
            Assert.That(models.Any(m => m.Id.Any(char.IsControl)), Is.False);
            Assert.That(models.All(m => m.Id.Length <= AiRules.MaxModelIdLength), Is.True);
        }
    }
}
