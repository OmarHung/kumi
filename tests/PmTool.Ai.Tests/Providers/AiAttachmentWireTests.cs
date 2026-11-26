using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using PmTool.Ai.Providers;

namespace PmTool.Ai.Tests.Providers;

/// <summary>
/// 附件的「線上」測試（internal/HANDOFF-ai-attachments.md 第 4 節）：用假的 handler 接住四家 SDK 真的送出去的請求，
/// 確認使用者的訊息裡有圖片與 PDF 的區塊（<see cref="DataContent"/> 經過各家轉接層的結果）。沒有真的金鑰也驗得到；格式照 SDK 實際送出的。
/// </summary>
[TestFixture]
public sealed class AiAttachmentWireTests
{
    private const string AnthropicStream = """
        event: message_start
        data: {"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5-5","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":25,"output_tokens":1}}}

        event: message_stop
        data: {"type":"message_stop"}


        """;

    private const string OpenAiStream = """
        data: {"id":"c1","object":"chat.completion.chunk","created":1,"model":"gpt-5.2","choices":[{"index":0,"delta":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}

        data: [DONE]


        """;

    private const string GoogleStream = """
        data: {"candidates":[{"content":{"role":"model","parts":[{"text":"ok"}]},"finishReason":"STOP"}]}


        """;

    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static readonly byte[] Pdf = "%PDF-1.4\n%%EOF"u8.ToArray();

    private static string PngBase64 => Convert.ToBase64String(Png);

    private static string PdfBase64 => Convert.ToBase64String(Pdf);

    private static async Task<JsonNode> SendAsync(AiProviderKind kind, string model, string? baseUrl = null)
    {
        var stream = kind switch
        {
            AiProviderKind.Anthropic => AnthropicStream,
            AiProviderKind.Google => GoogleStream,
            _ => OpenAiStream,
        };
        var handler = new RecordingHandler(_ => RecordingHandler.EventStream(stream));
        using var gateway = new AiProviderGateway(new AiModuleOptions(), handler);
        using var client = gateway.CreateChatClient(new AiEndpoint(kind, baseUrl, "key-123"), model);
        var message = new ChatMessage(
            ChatRole.User,
            [
                new TextContent("看這些"),
                new DataContent(Png, "image/png") { Name = "a.png" },
                new DataContent(Pdf, "application/pdf") { Name = "spec.pdf" },
            ]);

        await foreach (var _ in client.GetStreamingResponseAsync([message], AiRequestOptions.Create(kind, model, null)))
        {
        }

        return handler.Single.Json;
    }

    [Test]
    public async Task Anthropic_SendsAnImageBlockAndADocumentBlockWithBase64Sources()
    {
        // Act
        var json = await SendAsync(AiProviderKind.Anthropic, "claude-opus-5-5");

        // Assert
        var content = json["messages"]![0]!["content"]!.AsArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(content, Has.Count.EqualTo(3));
            Assert.That(content[0]!["type"]!.GetValue<string>(), Is.EqualTo("text"));
            Assert.That(content[1]!["type"]!.GetValue<string>(), Is.EqualTo("image"));
            Assert.That(content[1]!["source"]!["type"]!.GetValue<string>(), Is.EqualTo("base64"));
            Assert.That(content[1]!["source"]!["media_type"]!.GetValue<string>(), Is.EqualTo("image/png"));
            Assert.That(content[1]!["source"]!["data"]!.GetValue<string>(), Is.EqualTo(PngBase64));
            Assert.That(content[2]!["type"]!.GetValue<string>(), Is.EqualTo("document"));
            Assert.That(content[2]!["source"]!["media_type"]!.GetValue<string>(), Is.EqualTo("application/pdf"));
            Assert.That(content[2]!["source"]!["data"]!.GetValue<string>(), Is.EqualTo(PdfBase64));
        }
    }

    [TestCase(AiProviderKind.Openai, "gpt-5.2", null)]
    [TestCase(AiProviderKind.Openrouter, "anthropic/claude-opus-5-5", null)]
    [TestCase(AiProviderKind.OpenaiCompatible, "llama", "https://llm.example.com/v1")]
    public async Task ChatCompletionsProviders_SendAnImageUrlPartAndAFilePartWithAFileName(AiProviderKind kind, string model, string? baseUrl)
    {
        // Act
        var json = await SendAsync(kind, model, baseUrl);

        // Assert
        var content = json["messages"]![0]!["content"]!.AsArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(content, Has.Count.EqualTo(3));
            Assert.That(content[1]!["type"]!.GetValue<string>(), Is.EqualTo("image_url"));
            Assert.That(content[1]!["image_url"]!["url"]!.GetValue<string>(), Is.EqualTo($"data:image/png;base64,{PngBase64}"));
            Assert.That(content[2]!["type"]!.GetValue<string>(), Is.EqualTo("file"));
            Assert.That(content[2]!["file"]!["filename"]!.GetValue<string>(), Is.EqualTo("spec.pdf"));
            Assert.That(content[2]!["file"]!["file_data"]!.GetValue<string>(), Is.EqualTo($"data:application/pdf;base64,{PdfBase64}"));
        }
    }

    [Test]
    public async Task Google_SendsInlineDataPartsForTheImageAndThePdf()
    {
        // Act
        var json = await SendAsync(AiProviderKind.Google, "gemini-3-pro");

        // Assert
        var parts = json["contents"]![0]!["parts"]!.AsArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parts, Has.Count.EqualTo(3));
            Assert.That(parts[1]!["inlineData"]!["mimeType"]!.GetValue<string>(), Is.EqualTo("image/png"));
            Assert.That(parts[1]!["inlineData"]!["data"]!.GetValue<string>(), Is.EqualTo(PngBase64));
            Assert.That(parts[2]!["inlineData"]!["mimeType"]!.GetValue<string>(), Is.EqualTo("application/pdf"));
            Assert.That(parts[2]!["inlineData"]!["data"]!.GetValue<string>(), Is.EqualTo(PdfBase64));
        }
    }
}
