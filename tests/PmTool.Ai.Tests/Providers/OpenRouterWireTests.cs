using System.Net;
using Microsoft.Extensions.AI;
using PmTool.Ai.Providers;

namespace PmTool.Ai.Tests.Providers;

/// <summary>
/// OpenRouter 的「線上」測試（internal/HANDOFF-ai-floating.md 第 4.2 節）：假的 handler 接住請求，確認 JSON、標頭、模型清單的過濾與思考深度，
/// 再餵 OpenRouter 格式的串流（心跳行、工具呼叫、串流中途的 error）。沒有真的帳號，格式是照官方文件寫的。
/// </summary>
[TestFixture]
public sealed class OpenRouterWireTests
{
    private const string Key = "sk-or-v1-abcdef0123456789";

    private const string Stream = """
        : OPENROUTER PROCESSING

        data: {"id":"c1","object":"chat.completion.chunk","created":1,"model":"anthropic/claude-opus-5-5","choices":[{"index":0,"delta":{"role":"assistant","content":"先看藍圖。"},"finish_reason":null}]}

        : OPENROUTER PROCESSING

        data: {"id":"c1","object":"chat.completion.chunk","created":1,"model":"anthropic/claude-opus-5-5","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"get_blueprint","arguments":""}}]},"finish_reason":null}]}

        data: {"id":"c1","object":"chat.completion.chunk","created":1,"model":"anthropic/claude-opus-5-5","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"depth\":2}"}}]},"finish_reason":null}]}

        data: {"id":"c1","object":"chat.completion.chunk","created":1,"model":"anthropic/claude-opus-5-5","choices":[{"index":0,"delta":{},"finish_reason":"tool_calls"}]}

        data: {"id":"c1","object":"chat.completion.chunk","created":1,"model":"anthropic/claude-opus-5-5","choices":[],"usage":{"prompt_tokens":25,"completion_tokens":42,"total_tokens":67}}

        data: [DONE]


        """;

    private const string StreamWithMidError = """
        data: {"id":"c1","object":"chat.completion.chunk","created":1,"model":"x/y","choices":[{"index":0,"delta":{"role":"assistant","content":"寫到一半"},"finish_reason":null}]}

        data: {"id":"c1","object":"chat.completion.chunk","created":1,"model":"x/y","error":{"code":"server_error","message":"Provider disconnected"},"choices":[{"index":0,"delta":{"content":""},"finish_reason":"error"}]}

        data: [DONE]


        """;

    private static AiEndpoint Endpoint => new(AiProviderKind.Openrouter, null, Key);

    private static ChatOptions WithTool(ChatOptions options)
    {
        options.Tools = [AIFunctionFactory.Create((int depth) => depth, "get_blueprint", "讀藍圖")];
        return options;
    }

    private static async Task<List<ChatResponseUpdate>> ReadAllAsync(IChatClient client, ChatOptions options)
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "幫我看一下")], options))
        {
            updates.Add(update);
        }

        return updates;
    }

    [TestCase(AiEffort.None, "none")]
    [TestCase(AiEffort.Low, "low")]
    [TestCase(AiEffort.High, "high")]
    [TestCase(AiEffort.Xhigh, "xhigh")]
    [TestCase(AiEffort.Max, "max")]
    public async Task Chat_Effort_IsSentAsReasoningObjectNotReasoningEffort(AiEffort effort, string expected)
    {
        // Arrange
        var handler = new RecordingHandler(_ => RecordingHandler.EventStream(Stream));
        using var gateway = new AiProviderGateway(new AiModuleOptions(PublicUrl: "https://kumi.example.com"), handler);
        using var client = gateway.CreateChatClient(Endpoint, "anthropic/claude-opus-5-5");

        // Act
        var updates = await ReadAllAsync(client, WithTool(AiRequestOptions.Create(AiProviderKind.Openrouter, "anthropic/claude-opus-5-5", effort)));

        // Assert
        var sent = handler.Single;
        var call = updates.SelectMany(u => u.Contents).OfType<FunctionCallContent>().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent.Method, Is.EqualTo("POST"));
            Assert.That(sent.Uri.Host, Is.EqualTo("openrouter.ai"));
            Assert.That(sent.Uri.AbsolutePath, Is.EqualTo("/api/v1/chat/completions"));
            Assert.That(sent.Json["reasoning"]!["effort"]!.GetValue<string>(), Is.EqualTo(expected));
            Assert.That(sent.Json["reasoning_effort"], Is.Null);
            Assert.That(sent.Json["max_tokens"]!.GetValue<int>(), Is.EqualTo(AiRules.MaxOutputTokens));
            Assert.That(sent.Json["max_completion_tokens"], Is.Null);
            Assert.That(sent.Json["model"]!.GetValue<string>(), Is.EqualTo("anthropic/claude-opus-5-5"));
            Assert.That(sent.Headers["Authorization"], Is.EqualTo($"Bearer {Key}"));
            Assert.That(sent.Headers["HTTP-Referer"], Is.EqualTo("https://kumi.example.com"));
            Assert.That(sent.Headers["X-OpenRouter-Title"], Is.EqualTo("Kumi"));
            Assert.That(string.Concat(updates.Select(u => u.Text)), Is.EqualTo("先看藍圖。"));
            Assert.That(call.Name, Is.EqualTo("get_blueprint"));
            Assert.That(call.CallId, Is.EqualTo("call_1"));
            Assert.That(call.Arguments!["depth"]?.ToString(), Is.EqualTo("2"));
        }
    }

    [Test]
    public async Task Chat_NoEffort_OmitsReasoningAndSkipsRefererWithoutPublicUrl()
    {
        // Arrange
        var handler = new RecordingHandler(_ => RecordingHandler.EventStream(Stream));
        using var gateway = new AiProviderGateway(new AiModuleOptions(), handler);
        using var client = gateway.CreateChatClient(Endpoint, "openai/gpt-5.2");

        // Act
        await ReadAllAsync(client, AiRequestOptions.Create(AiProviderKind.Openrouter, "openai/gpt-5.2", null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(handler.Single.Json["reasoning"], Is.Null);
            Assert.That(handler.Single.Json["reasoning_effort"], Is.Null);
            Assert.That(handler.Single.Headers.ContainsKey("HTTP-Referer"), Is.False);
            Assert.That(handler.Single.Headers["X-OpenRouter-Title"], Is.EqualTo("Kumi"));
        }
    }

    [Test]
    public async Task Chat_ErrorInTheMiddleOfTheStream_FailsWithTheProvidersMessageAfterTheEarlierText()
    {
        // Arrange
        var handler = new RecordingHandler(_ => RecordingHandler.EventStream(StreamWithMidError));
        using var gateway = new AiProviderGateway(new AiModuleOptions(), handler);
        using var client = gateway.CreateChatClient(Endpoint, "x/y");
        var received = new List<string>();

        // Act
        var thrown = Assert.ThrowsAsync<AiProviderException>(async () =>
        {
            await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")], AiRequestOptions.Create(AiProviderKind.Openrouter, "x/y", null)))
            {
                received.Add(update.Text);
            }
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(string.Concat(received), Is.EqualTo("寫到一半"));
            Assert.That(thrown!.Message, Does.Contain("Provider disconnected"));
            Assert.That(AiProviderFailures.Describe(thrown), Does.Contain("Provider disconnected"));
        }
    }

    [Test]
    public async Task Chat_PaymentRequired_IsDescribedAsNotEnoughCredit()
    {
        // Arrange
        var handler = new RecordingHandler(_ => RecordingHandler.Json("""{"error":{"code":402,"message":"Insufficient credits. Add more using https://openrouter.ai/credits"}}""", HttpStatusCode.PaymentRequired));
        using var gateway = new AiProviderGateway(new AiModuleOptions(), handler);
        using var client = gateway.CreateChatClient(Endpoint, "x/y");

        // Act
        var thrown = Assert.CatchAsync(async () => await ReadAllAsync(client, AiRequestOptions.Create(AiProviderKind.Openrouter, "x/y", null)));
        var text = AiProviderFailures.Describe(thrown!, "openrouter.ai", Key);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(text, Does.Contain("額度不夠"));
            Assert.That(text, Does.Contain("Insufficient credits"));
            Assert.That(text, Does.Not.Contain(Key));
        }
    }

    [Test]
    public async Task Discover_ChecksTheKeyFirstThenReadsOnlyToolModelsWithTheirEfforts()
    {
        // Arrange
        const string models = """
            {"data":[
              {"id":"anthropic/claude-opus-5-5","name":"Anthropic: Claude Opus 5.5","supported_parameters":["tools","reasoning"],
               "reasoning":{"mandatory":false,"supported_efforts":["max","xhigh","high","medium","low","minimal","none","bogus"]}},
              {"id":"openai/gpt-5.2","name":"OpenAI: GPT-5.2","supported_parameters":["tools"],
               "reasoning":{"mandatory":true,"supported_efforts":["high","medium","none"]}},
              {"id":"vendor/open-reasoner","name":"Open Reasoner","supported_parameters":["tools"],"reasoning":{"supported_efforts":null}},
              {"id":"meta/llama-plain","name":"Llama","supported_parameters":["tools","temperature"]},
              {"id":"meta/no-tools","name":"No tools","supported_parameters":["temperature"]}
            ]}
            """;
        var handler = new RecordingHandler(request => request.Uri.AbsolutePath.EndsWith("/key", StringComparison.Ordinal)
            ? RecordingHandler.Json("""{"data":{"label":"Kumi"}}""")
            : RecordingHandler.Json(models));
        using var gateway = new AiProviderGateway(new AiModuleOptions(), handler);

        // Act
        var found = await gateway.DiscoverAsync(Endpoint, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(handler.Requests, Has.Count.EqualTo(2));
            Assert.That(handler.Requests[0].Uri.AbsolutePath, Is.EqualTo("/api/v1/key"));
            Assert.That(handler.Requests[1].Uri.PathAndQuery, Is.EqualTo("/api/v1/models?supported_parameters=tools"));
            Assert.That(handler.Requests.All(r => r.Headers["Authorization"] == $"Bearer {Key}"), Is.True);
            Assert.That(found.Select(m => m.Id), Is.EqualTo(new[] { "anthropic/claude-opus-5-5", "meta/llama-plain", "openai/gpt-5.2", "vendor/open-reasoner" }));
            Assert.That(found.Single(m => m.Id == "anthropic/claude-opus-5-5").DisplayName, Is.EqualTo("Anthropic: Claude Opus 5.5"));
            Assert.That(
                found.Single(m => m.Id == "anthropic/claude-opus-5-5").Efforts,
                Is.EqualTo(new[] { AiEffort.None, AiEffort.Low, AiEffort.Medium, AiEffort.High, AiEffort.Xhigh, AiEffort.Max }));
            Assert.That(found.Single(m => m.Id == "openai/gpt-5.2").Efforts, Is.EqualTo(new[] { AiEffort.Medium, AiEffort.High }));
            Assert.That(found.Single(m => m.Id == "vendor/open-reasoner").Efforts, Is.EqualTo(new[] { AiEffort.Low, AiEffort.Medium, AiEffort.High }));
            Assert.That(found.Single(m => m.Id == "meta/llama-plain").Efforts, Is.Empty);
        }
    }

    [Test]
    public async Task Discover_ModelIdContainingAHash_IsSkipped()
    {
        // Arrange
        var handler = new RecordingHandler(request => request.Uri.AbsolutePath.EndsWith("/key", StringComparison.Ordinal)
            ? RecordingHandler.Json("{}")
            : RecordingHandler.Json("""{"data":[{"id":"vendor/ok","name":"Ok","supported_parameters":["tools"]},{"id":"vendor/bad#effort=high","name":"Bad","supported_parameters":["tools"]}]}"""));
        using var gateway = new AiProviderGateway(new AiModuleOptions(), handler);

        // Act
        var found = await gateway.DiscoverAsync(Endpoint, CancellationToken.None);

        // Assert
        Assert.That(found.Select(m => m.Id), Is.EqualTo(new[] { "vendor/ok" }));
    }

    [Test]
    public void Rewrite_ModelWithAHashInItsId_UsesTheLastMarker()
    {
        // Act
        var rewritten = OpenRouterHandler.TryRewrite("""{"model":"a/b#effort=low#effort=max","max_completion_tokens":5}""", out var json);

        // Assert
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rewritten, Is.True);
            Assert.That(node["model"]!.GetValue<string>(), Is.EqualTo("a/b#effort=low"));
            Assert.That(node["reasoning"]!["effort"]!.GetValue<string>(), Is.EqualTo("max"));
        }
    }

    [Test]
    public async Task Discover_InvalidKey_FailsOnTheKeyCheckWithoutReadingTheList()
    {
        // Arrange
        var handler = new RecordingHandler(_ => RecordingHandler.Json("""{"error":{"code":401,"message":"No auth credentials found"}}""", HttpStatusCode.Unauthorized));
        using var gateway = new AiProviderGateway(new AiModuleOptions(), handler);

        // Act
        var thrown = Assert.ThrowsAsync<AiProviderException>(() => gateway.DiscoverAsync(Endpoint, CancellationToken.None));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(thrown!.Message, Does.Contain("金鑰不對"));
            Assert.That(thrown.Message, Does.Not.Contain(Key));
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void Discover_UnexpectedShape_Fails()
    {
        // Arrange
        var handler = new RecordingHandler(request => RecordingHandler.Json(request.Uri.AbsolutePath.EndsWith("/key", StringComparison.Ordinal) ? "{}" : """{"models":[]}"""));
        using var gateway = new AiProviderGateway(new AiModuleOptions(), handler);

        // Act
        var thrown = Assert.ThrowsAsync<AiProviderException>(() => gateway.DiscoverAsync(Endpoint, CancellationToken.None));

        // Assert
        Assert.That(thrown!.Message, Does.Contain("格式"));
    }

    [Test]
    public async Task Exchange_PostsCodeAndVerifierAndReturnsTheKey()
    {
        // Arrange
        var handler = new RecordingHandler(_ => RecordingHandler.Json($$"""{"key":"{{Key}}","user_id":"user_1"}"""));
        using var client = new OpenRouterAuthClient(new AiModuleOptions(), handler);

        // Act
        var key = await client.ExchangeCodeAsync("code-1", "verifier-1", CancellationToken.None);

        // Assert
        var sent = handler.Single;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(key, Is.EqualTo(Key));
            Assert.That(sent.Method, Is.EqualTo("POST"));
            Assert.That(sent.Uri.ToString(), Is.EqualTo("https://openrouter.ai/api/v1/auth/keys"));
            Assert.That(sent.Json["code"]!.GetValue<string>(), Is.EqualTo("code-1"));
            Assert.That(sent.Json["code_verifier"]!.GetValue<string>(), Is.EqualTo("verifier-1"));
            Assert.That(sent.Json["code_challenge_method"]!.GetValue<string>(), Is.EqualTo("S256"));
        }
    }

    [TestCase(HttpStatusCode.Forbidden, "Authorization code expired", "過期")]
    [TestCase(HttpStatusCode.Forbidden, "Invalid code or code_verifier", "授權碼或驗證碼不對")]
    [TestCase(HttpStatusCode.BadRequest, "Invalid code_challenge_method", "不接受")]
    [TestCase(HttpStatusCode.InternalServerError, "boom", "出錯")]
    public void Exchange_Failure_IsExplainedWithoutEchoingTheBody(HttpStatusCode status, string message, string expected)
    {
        // Arrange
        var handler = new RecordingHandler(_ => RecordingHandler.Json("{\"error\":{\"message\":\"" + message + "\"}}", status));
        using var client = new OpenRouterAuthClient(new AiModuleOptions(), handler);

        // Act
        var thrown = Assert.ThrowsAsync<AiProviderException>(() => client.ExchangeCodeAsync("code-1", "verifier-1", CancellationToken.None));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(thrown!.Message, Does.Contain(expected));
            Assert.That(thrown.Message, Does.Not.Contain("verifier-1"));
        }
    }

    [Test]
    public void Exchange_ResponseWithoutKey_Fails()
    {
        // Arrange
        var handler = new RecordingHandler(_ => RecordingHandler.Json("""{"user_id":"u"}"""));
        using var client = new OpenRouterAuthClient(new AiModuleOptions(), handler);

        // Act
        var thrown = Assert.ThrowsAsync<AiProviderException>(() => client.ExchangeCodeAsync("c", "v", CancellationToken.None));

        // Assert
        Assert.That(thrown!.Message, Does.Contain("沒有拿到金鑰"));
    }

    [Test]
    public void EffortCatalog_ForOpenRouter_MandatoryReasoningHasNoNone()
    {
        // Act
        var efforts = AiEffortCatalog.ForOpenRouter(hasReasoning: true, ["none", "low", "minimal"], mandatory: true);

        // Assert
        Assert.That(efforts, Is.EqualTo(new[] { AiEffort.Low }));
    }
}
