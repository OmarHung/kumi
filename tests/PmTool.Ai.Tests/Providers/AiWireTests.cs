using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using PmTool.Ai.Providers;

namespace PmTool.Ai.Tests.Providers;

/// <summary>
/// 「線上的」測試：用假的 HttpMessageHandler 接住三家 SDK 真的送出去的請求，確認 JSON 的形狀，
/// 再餵各家格式的串流回應（含一次工具呼叫），確認轉接層讀得出文字、工具呼叫與用量。沒有真的金鑰也驗得到。
/// </summary>
[TestFixture]
public sealed class AiWireTests
{
    private const string AnthropicStream = """
        event: message_start
        data: {"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5-5","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":25,"output_tokens":1}}}

        event: content_block_start
        data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"先看藍圖。"}}

        event: content_block_stop
        data: {"type":"content_block_stop","index":0}

        event: content_block_start
        data: {"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu_1","name":"get_blueprint","input":{}}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{\"depth\":2}"}}

        event: content_block_stop
        data: {"type":"content_block_stop","index":1}

        event: message_delta
        data: {"type":"message_delta","delta":{"stop_reason":"tool_use","stop_sequence":null},"usage":{"output_tokens":42}}

        event: message_stop
        data: {"type":"message_stop"}


        """;

    private const string OpenAiStream = """
        data: {"id":"c1","object":"chat.completion.chunk","created":1,"model":"gpt-5.2","choices":[{"index":0,"delta":{"role":"assistant","content":"先看藍圖。"},"finish_reason":null}]}

        data: {"id":"c1","object":"chat.completion.chunk","created":1,"model":"gpt-5.2","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"get_blueprint","arguments":""}}]},"finish_reason":null}]}

        data: {"id":"c1","object":"chat.completion.chunk","created":1,"model":"gpt-5.2","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"depth\":2}"}}]},"finish_reason":null}]}

        data: {"id":"c1","object":"chat.completion.chunk","created":1,"model":"gpt-5.2","choices":[{"index":0,"delta":{},"finish_reason":"tool_calls"}]}

        data: {"id":"c1","object":"chat.completion.chunk","created":1,"model":"gpt-5.2","choices":[],"usage":{"prompt_tokens":25,"completion_tokens":42,"total_tokens":67}}

        data: [DONE]


        """;

    private const string GoogleStream = """
        data: {"candidates":[{"content":{"role":"model","parts":[{"text":"先看藍圖。"}]}}],"modelVersion":"gemini-3-pro"}

        data: {"candidates":[{"content":{"role":"model","parts":[{"functionCall":{"name":"get_blueprint","args":{"depth":2}}}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":25,"candidatesTokenCount":42,"totalTokenCount":67},"modelVersion":"gemini-3-pro"}


        """;

    private static ChatOptions WithTool(ChatOptions options)
    {
        options.Tools = [AIFunctionFactory.Create((int depth) => depth, "get_blueprint", "讀藍圖")];
        return options;
    }

    private static async Task<(string Text, FunctionCallContent Call, UsageDetails? Usage)> StreamAsync(IChatClient client, ChatOptions options)
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "幫我看一下")], options))
        {
            updates.Add(update);
        }

        var text = string.Concat(updates.Select(u => u.Text));
        var call = updates.SelectMany(u => u.Contents).OfType<FunctionCallContent>().Single();
        var usage = updates.SelectMany(u => u.Contents).OfType<UsageContent>().Select(u => u.Details).LastOrDefault();
        return (text, call, usage);
    }

    [Test]
    public async Task Anthropic_Max_SendsOutputConfigEffortTopLevelCacheControlAndMaxTokensWithoutThinking()
    {
        // Arrange
        var handler = new RecordingHandler(_ => RecordingHandler.EventStream(AnthropicStream));
        using var gateway = new AiProviderGateway(new AiModuleOptions(), handler);
        using var client = gateway.CreateChatClient(new AiEndpoint(AiProviderKind.Anthropic, null, "sk-ant-test"), "claude-opus-5-5");
        var options = WithTool(AiRequestOptions.Create(AiProviderKind.Anthropic, "claude-opus-5-5", AiEffort.Max));

        // Act
        var (text, call, usage) = await StreamAsync(client, options);

        // Assert
        var sent = handler.Single;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent.Method, Is.EqualTo("POST"));
            Assert.That(sent.Uri.AbsolutePath, Is.EqualTo("/v1/messages"));
            Assert.That(sent.Json["model"]!.GetValue<string>(), Is.EqualTo("claude-opus-5-5"));
            Assert.That(sent.Json["max_tokens"]!.GetValue<int>(), Is.EqualTo(32000));
            Assert.That(sent.Json["output_config"]!["effort"]!.GetValue<string>(), Is.EqualTo("max"));
            Assert.That(sent.Json["cache_control"]!["type"]!.GetValue<string>(), Is.EqualTo("ephemeral"));
            Assert.That(sent.Json["thinking"], Is.Null);
            Assert.That(sent.Json["stream"]!.GetValue<bool>(), Is.True);
            Assert.That(sent.Json["messages"]!.AsArray(), Has.Count.EqualTo(1));
            Assert.That(sent.Json["tools"]!.AsArray().Single()!["name"]!.GetValue<string>(), Is.EqualTo("get_blueprint"));
            Assert.That(sent.Headers["Authorization"], Is.EqualTo("Bearer sk-ant-test"));
            Assert.That(text, Is.EqualTo("先看藍圖。"));
            Assert.That(call.Name, Is.EqualTo("get_blueprint"));
            Assert.That(call.CallId, Is.EqualTo("toolu_1"));
            Assert.That(call.Arguments!["depth"]?.ToString(), Is.EqualTo("2"));
            Assert.That(usage?.OutputTokenCount, Is.EqualTo(42));
        }
    }

    [Test]
    public async Task Anthropic_NoEffort_OmitsOutputConfigButStillSendsCacheControl()
    {
        // Arrange
        var handler = new RecordingHandler(_ => RecordingHandler.EventStream(AnthropicStream));
        using var gateway = new AiProviderGateway(new AiModuleOptions(), handler);
        using var client = gateway.CreateChatClient(new AiEndpoint(AiProviderKind.Anthropic, null, "sk-ant-test"), "claude-sonnet-5-5");

        // Act
        await StreamAsync(client, AiRequestOptions.Create(AiProviderKind.Anthropic, "claude-sonnet-5-5", null));

        // Assert
        var sent = handler.Single.Json;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent["output_config"], Is.Null);
            Assert.That(sent["thinking"], Is.Null);
            Assert.That(sent["cache_control"], Is.Not.Null);
            Assert.That(sent["max_tokens"]!.GetValue<int>(), Is.EqualTo(32000));
        }
    }

    [TestCase(AiEffort.Low, "low")]
    [TestCase(AiEffort.Medium, "medium")]
    [TestCase(AiEffort.High, "high")]
    [TestCase(AiEffort.Xhigh, "xhigh")]
    public async Task Anthropic_EachEffort_IsSentUnderItsOwnName(AiEffort effort, string expected)
    {
        // Arrange
        var handler = new RecordingHandler(_ => RecordingHandler.EventStream(AnthropicStream));
        using var gateway = new AiProviderGateway(new AiModuleOptions(), handler);
        using var client = gateway.CreateChatClient(new AiEndpoint(AiProviderKind.Anthropic, null, "sk-ant-test"), "claude-opus-5-5");

        // Act
        await StreamAsync(client, AiRequestOptions.Create(AiProviderKind.Anthropic, "claude-opus-5-5", effort));

        // Assert
        Assert.That(handler.Single.Json["output_config"]!["effort"]!.GetValue<string>(), Is.EqualTo(expected));
    }

    [TestCase(AiEffort.None, "none")]
    [TestCase(AiEffort.Low, "low")]
    [TestCase(AiEffort.Medium, "medium")]
    [TestCase(AiEffort.High, "high")]
    [TestCase(AiEffort.Xhigh, "xhigh")]
    public async Task OpenAi_Effort_IsSentAsReasoningEffortOnChatCompletions(AiEffort effort, string expected)
    {
        // Arrange
        var handler = new RecordingHandler(_ => RecordingHandler.EventStream(OpenAiStream));
        using var gateway = new AiProviderGateway(new AiModuleOptions(), handler);
        using var client = gateway.CreateChatClient(new AiEndpoint(AiProviderKind.Openai, null, "sk-openai-test"), "gpt-5.2");

        // Act
        var (text, call, usage) = await StreamAsync(client, WithTool(AiRequestOptions.Create(AiProviderKind.Openai, "gpt-5.2", effort)));

        // Assert
        var sent = handler.Single;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent.Uri.Host, Is.EqualTo("api.openai.com"));
            Assert.That(sent.Uri.AbsolutePath, Is.EqualTo("/v1/chat/completions"));
            Assert.That(sent.Json["reasoning_effort"]!.GetValue<string>(), Is.EqualTo(expected));
            Assert.That(sent.Json["model"]!.GetValue<string>(), Is.EqualTo("gpt-5.2"));
            Assert.That((sent.Json["max_completion_tokens"] ?? sent.Json["max_tokens"])!.GetValue<int>(), Is.EqualTo(32000));
            Assert.That(sent.Headers["Authorization"], Is.EqualTo("Bearer sk-openai-test"));
            Assert.That(text, Is.EqualTo("先看藍圖。"));
            Assert.That(call.Name, Is.EqualTo("get_blueprint"));
            Assert.That(call.CallId, Is.EqualTo("call_1"));
            Assert.That(call.Arguments!["depth"]?.ToString(), Is.EqualTo("2"));
            Assert.That(usage?.InputTokenCount, Is.EqualTo(25));
        }
    }

    [Test]
    public async Task OpenAi_NoEffort_OmitsReasoningEffort()
    {
        // Arrange
        var handler = new RecordingHandler(_ => RecordingHandler.EventStream(OpenAiStream));
        using var gateway = new AiProviderGateway(new AiModuleOptions(), handler);
        using var client = gateway.CreateChatClient(new AiEndpoint(AiProviderKind.Openai, null, "sk-openai-test"), "gpt-4o");

        // Act
        await StreamAsync(client, AiRequestOptions.Create(AiProviderKind.Openai, "gpt-4o", null));

        // Assert
        Assert.That(handler.Single.Json["reasoning_effort"], Is.Null);
    }

    [Test]
    public async Task Compatible_UsesTheBaseUrlAndAPlaceholderKeyWhenThereIsNone()
    {
        // Arrange
        var handler = new RecordingHandler(_ => RecordingHandler.EventStream(OpenAiStream));
        using var gateway = new AiProviderGateway(new AiModuleOptions(), handler);
        using var client = gateway.CreateChatClient(
            new AiEndpoint(AiProviderKind.OpenaiCompatible, "http://ollama.example.com:11434/v1", null), "llama3");

        // Act
        await StreamAsync(client, AiRequestOptions.Create(AiProviderKind.OpenaiCompatible, "llama3", AiEffort.High));

        // Assert
        var sent = handler.Single;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent.Uri.ToString(), Does.StartWith("http://ollama.example.com:11434/v1/chat/completions"));
            Assert.That(sent.Headers["Authorization"], Is.EqualTo($"Bearer {AiProviderGateway.NoKeyPlaceholder}"));
            Assert.That(sent.Json["reasoning_effort"]!.GetValue<string>(), Is.EqualTo("high"));
        }
    }

    [TestCase(AiEffort.Low, "LOW")]
    [TestCase(AiEffort.Medium, "MEDIUM")]
    [TestCase(AiEffort.High, "HIGH")]
    [TestCase(AiEffort.Xhigh, "HIGH")]
    public async Task Google_Effort_IsSentAsThinkingLevelInTheGenerationConfig(AiEffort effort, string expected)
    {
        // Arrange
        var handler = new RecordingHandler(_ => RecordingHandler.EventStream(GoogleStream));
        using var gateway = new AiProviderGateway(new AiModuleOptions(), handler);
        using var client = gateway.CreateChatClient(new AiEndpoint(AiProviderKind.Google, null, "AIza-test"), "gemini-3-pro");

        // Act
        var (text, call, usage) = await StreamAsync(client, WithTool(AiRequestOptions.Create(AiProviderKind.Google, "gemini-3-pro", effort)));

        // Assert
        var sent = handler.Single;
        var level = sent.Json["generationConfig"]!["thinkingConfig"]!["thinkingLevel"]!.GetValue<string>();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent.Uri.AbsolutePath, Does.Contain("gemini-3-pro").And.Contain("streamGenerateContent"));
            Assert.That(level, Is.EqualTo(expected).IgnoreCase);
            Assert.That(sent.Json["generationConfig"]!["maxOutputTokens"]!.GetValue<int>(), Is.EqualTo(32000));
            Assert.That(sent.Headers.GetValueOrDefault("x-goog-api-key"), Is.EqualTo("AIza-test"));
            Assert.That(text, Is.EqualTo("先看藍圖。"));
            Assert.That(call.Name, Is.EqualTo("get_blueprint"));
            Assert.That(call.Arguments!["depth"]?.ToString(), Is.EqualTo("2"));
            Assert.That(usage?.TotalTokenCount, Is.EqualTo(67));
        }
    }

    [Test]
    public void RequestOptions_AreFreshObjectsEachTime()
    {
        // Arrange & Act
        var first = AiRequestOptions.Create(AiProviderKind.Openai, "gpt-5.2", AiEffort.High);
        var second = AiRequestOptions.Create(AiProviderKind.Openai, "gpt-5.2", AiEffort.High);
        first.Tools = [];

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(second.Tools, Is.Null);
            Assert.That(first.MaxOutputTokens, Is.EqualTo(AiRules.MaxOutputTokens));
        }
    }
}
