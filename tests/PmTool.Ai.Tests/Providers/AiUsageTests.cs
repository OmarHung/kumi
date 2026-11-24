using Microsoft.Extensions.AI;
using PmTool.Ai.Assistant.Transcript;
using PmTool.Ai.Providers;

namespace PmTool.Ai.Tests.Providers;

/// <summary>
/// 用量要是「每次呼叫模型的和」，不能重複算：Anthropic 的 message_delta 與 Gemini 的每個區塊帶的都是累計值。
/// 這裡走真的轉接層（各家 SDK 的 IChatClient）加上工具呼叫的迴圈，再交給 <see cref="ReplyAccumulator"/> 算。
/// </summary>
[TestFixture]
public sealed class AiUsageTests
{
    private static string AnthropicTurn(string content, string stopReason) => AnthropicTemplate.Replace("@CONTENT@", content).Replace("@STOP@", stopReason);

    private const string AnthropicTemplate = """
        event: message_start
        data: {"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5-5","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":100,"output_tokens":1}}}

        @CONTENT@

        event: message_delta
        data: {"type":"message_delta","delta":{"stop_reason":"@STOP@","stop_sequence":null},"usage":{"output_tokens":50}}

        event: message_stop
        data: {"type":"message_stop"}


        """;

    private const string AnthropicToolBlock = """
        event: content_block_start
        data: {"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"toolu_1","name":"get_blueprint","input":{}}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{\"depth\":2}"}}

        event: content_block_stop
        data: {"type":"content_block_stop","index":0}
        """;

    private const string AnthropicTextBlock = """
        event: content_block_start
        data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"好"}}

        event: content_block_stop
        data: {"type":"content_block_stop","index":0}
        """;

    private static string OpenAiTurn(string delta, string finish) => OpenAiTemplate.Replace("@DELTA@", delta).Replace("@FINISH@", finish);

    private const string OpenAiTemplate = """
        data: {"id":"c1","object":"chat.completion.chunk","created":1,"model":"gpt-5.2","choices":[{"index":0,"delta":@DELTA@,"finish_reason":null}]}

        data: {"id":"c1","object":"chat.completion.chunk","created":1,"model":"gpt-5.2","choices":[{"index":0,"delta":{},"finish_reason":"@FINISH@"}]}

        data: {"id":"c1","object":"chat.completion.chunk","created":1,"model":"gpt-5.2","choices":[],"usage":{"prompt_tokens":100,"completion_tokens":50,"total_tokens":150}}

        data: [DONE]


        """;

    private const string OpenAiToolDelta = """{"role":"assistant","tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"get_blueprint","arguments":"{\"depth\":2}"}}]}""";

    private const string OpenAiTextDelta = """{"role":"assistant","content":"好"}""";

    // Gemini 每個區塊都帶 usageMetadata，數字是到這個區塊為止的累計。
    private const string GoogleToolTurn = """
        data: {"candidates":[{"content":{"role":"model","parts":[{"text":"看"}]}}],"usageMetadata":{"promptTokenCount":100,"candidatesTokenCount":10,"totalTokenCount":110}}

        data: {"candidates":[{"content":{"role":"model","parts":[{"functionCall":{"name":"get_blueprint","args":{"depth":2}}}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":100,"candidatesTokenCount":50,"totalTokenCount":150}}


        """;

    private const string GoogleTextTurn = """
        data: {"candidates":[{"content":{"role":"model","parts":[{"text":"好"}]}}],"usageMetadata":{"promptTokenCount":100,"candidatesTokenCount":10,"totalTokenCount":110}}

        data: {"candidates":[{"content":{"role":"model","parts":[{"text":"的"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":100,"candidatesTokenCount":50,"totalTokenCount":150}}


        """;

    private static async Task<(long? Input, long? Output)> RunAsync(AiProviderKind kind, string model, params string[] responses)
    {
        var next = 0;
        var handler = new RecordingHandler(_ => RecordingHandler.EventStream(responses[Math.Min(next++, responses.Length - 1)]));
        using var gateway = new AiProviderGateway(new AiModuleOptions(), handler);
        using var inner = gateway.CreateChatClient(new AiEndpoint(kind, null, "test-key"), model);
        using var client = new ChatClientBuilder(inner)
            .UseFunctionInvocation(configure: loop => loop.MaximumIterationsPerRequest = AiRules.MaxToolCallsPerReply)
            .Build();
        var options = AiRequestOptions.Create(kind, model, null);
        options.Tools = [AIFunctionFactory.Create((int depth) => depth, "get_blueprint", "讀藍圖")];

        var accumulator = new ReplyAccumulator();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "看一下")], options))
        {
            accumulator.Apply(update);
        }

        return (accumulator.InputTokens, accumulator.OutputTokens);
    }

    [Test]
    public async Task Anthropic_OneCall_OutputIsTheCumulativeValueNotAddedToTheStartValue()
    {
        // Act：message_start 的 output 是 1，message_delta 的 output 是累計的 50。
        var usage = await RunAsync(AiProviderKind.Anthropic, "claude-opus-5-5", AnthropicTurn(AnthropicTextBlock, "end_turn"));

        // Assert
        Assert.That(usage, Is.EqualTo((100L, 50L)));
    }

    [Test]
    public async Task Anthropic_ToolRoundTrip_IsTheSumOfTheTwoCalls()
    {
        // Act
        var usage = await RunAsync(
            AiProviderKind.Anthropic, "claude-opus-5-5", AnthropicTurn(AnthropicToolBlock, "tool_use"), AnthropicTurn(AnthropicTextBlock, "end_turn"));

        // Assert
        Assert.That(usage, Is.EqualTo((200L, 100L)));
    }

    [Test]
    public async Task OpenAi_OneCallAndToolRoundTrip_AreCountedOncePerCall()
    {
        // Act
        var single = await RunAsync(AiProviderKind.Openai, "gpt-5.2", OpenAiTurn(OpenAiTextDelta, "stop"));
        var roundTrip = await RunAsync(
            AiProviderKind.Openai, "gpt-5.2", OpenAiTurn(OpenAiToolDelta, "tool_calls"), OpenAiTurn(OpenAiTextDelta, "stop"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(single, Is.EqualTo((100L, 50L)));
            Assert.That(roundTrip, Is.EqualTo((200L, 100L)));
        }
    }

    [Test]
    public async Task Google_CumulativeUsageOnEveryChunk_IsCountedOncePerCall()
    {
        // Act
        var single = await RunAsync(AiProviderKind.Google, "gemini-3-pro", GoogleTextTurn);
        var roundTrip = await RunAsync(AiProviderKind.Google, "gemini-3-pro", GoogleToolTurn, GoogleTextTurn);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(single, Is.EqualTo((100L, 50L)));
            Assert.That(roundTrip, Is.EqualTo((200L, 100L)));
        }
    }
}
