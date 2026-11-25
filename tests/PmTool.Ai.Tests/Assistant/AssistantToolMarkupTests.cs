using Microsoft.Extensions.AI;
using PmTool.Ai.Assistant;
using PmTool.Ai.Tests.Assistant.Fakes;

namespace PmTool.Ai.Tests.Assistant;

/// <summary>模型把工具呼叫的標記吐成文字時（API 的連線）：網頁看不到、存下來的與下一則送給模型的也沒有。</summary>
[TestFixture]
public sealed class AssistantToolMarkupTests
{
    private AssistantHarness _h = null!;

    [SetUp]
    public async Task StartAsync() => _h = await AssistantHarness.StartAsync();

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    [Test]
    public async Task Reply_ModelWritesToolCallMarkupAsText_ItIsLeftOutOfEventsPartsAndTheNextTranscript()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script
            .Enqueue(Turn.Create()
                .Text("</invoke> <invoke name=\"get_item\"> <parameter name=\"item")
                .Text("Key\">APP-5</parameter> </invoke>")
                .Call("toolu_01", "get_blueprint"))
            .Reply("讀好了。")
            .Reply("第二則。");

        // Act
        var reply = await _h.SendAsync(conversation.Id, "幫我看看");
        await _h.SendAsync(conversation.Id, "再看一次");
        var history = _h.Script.Calls.Last().Messages.SelectMany(m => m.Contents).OfType<TextContent>().Select(t => t.Text);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reply.Events.OfType<AssistantTextDelta>().Select(e => e.Delta), Is.EqualTo(new[] { "讀好了。" }));
            Assert.That(reply.Final.Parts, Is.EqualTo(new AssistantPart[]
            {
                new AssistantToolPart("c1", "get_blueprint", AssistantToolStatus.Done, null, null),
                new AssistantTextPart("讀好了。"),
            }));
            Assert.That(history, Has.None.Contains("invoke"), "存下來的 transcript 不留標記，不然模型下一次看到自己吐過、更容易再犯。");
        }
    }
}
