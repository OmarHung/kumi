using Microsoft.Extensions.AI;
using PmTool.Ai.Assistant;
using PmTool.Ai.Tests.Assistant.Fakes;

namespace PmTool.Ai.Tests.Assistant;

/// <summary>回覆的格式限定（API 的連線）：只顯示、只存單獨一行 <c>[REPLY]</c> 之後的文字；重送歷史時標記補回去。</summary>
[TestFixture]
public sealed class AssistantReplyMarkerTests
{
    private AssistantHarness _h = null!;

    [SetUp]
    public async Task StartAsync() => _h = await AssistantHarness.StartAsync();

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    [Test]
    public async Task Reply_JunkBeforeATool_IsNotShownStoredOrSentBackToTheModel()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script
            .Enqueue(Turn.Create().Text("Thinking...</thinking_mode>").Call("toolu_01", "get_blueprint"))
            .Reply("Let me answer.\n\n[REPLY]\n讀好了。")
            .Reply("第二則。");

        // Act
        var reply = await _h.SendAsync(conversation.Id, "幫我看看");
        var stored = (await _h.RowAsync(reply.Final.Id)).Transcript;
        await _h.SendAsync(conversation.Id, "再看一次");
        var history = _h.Script.Calls.Last().Messages
            .Where(m => m.Role == ChatRole.Assistant)
            .SelectMany(m => m.Contents).OfType<TextContent>().Select(t => t.Text).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reply.Events.OfType<AssistantTextDelta>().Select(e => e.Delta), Is.EqualTo(new[] { "讀好了。" }));
            Assert.That(reply.Final.Parts, Is.EqualTo(new AssistantPart[]
            {
                new AssistantToolPart("c1", "get_blueprint", AssistantToolStatus.Done, null, null),
                new AssistantTextPart("讀好了。"),
            }));
            Assert.That(stored, Does.Not.Contain("[REPLY]").And.Not.Contain("Thinking").And.Contain("讀好了。"), "存的是過濾後的文字，不帶標記。");
            Assert.That(history, Is.EqualTo(new[] { "[REPLY]\n讀好了。" }), "歷史裡助理的文字前面補回標記，垃圾不在。");
        }
    }

    [Test]
    public async Task Reply_ReasoningBeforeTheMarkerInTheLastSegment_OnlyTheAnswerRemains()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Reply("Agile concept question — in scope. Answer briefly.\n\n[REPLY]\n故事點數估的是相對大小。");

        // Act
        var reply = await _h.SendAsync(conversation.Id, "怎麼估故事點數");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reply.Events.OfType<AssistantTextDelta>().Select(e => e.Delta), Is.EqualTo(new[] { "故事點數估的是相對大小。" }));
            Assert.That(reply.Final.Parts, Is.EqualTo(new AssistantPart[] { new AssistantTextPart("故事點數估的是相對大小。") }));
        }
    }

    [Test]
    public async Task Reply_ModelNeverWritesTheMarker_FinalPartsKeepEverythingInPlaceButTheEarlierTextIsNotStreamed()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script
            .Enqueue(Turn.Create().Text("我先讀設定。").Call("toolu_01", "get_blueprint"))
            .Reply("讀好了。");

        // Act
        var reply = await _h.SendAsync(conversation.Id, "幫我看看");

        // Assert：模型沒照格式寫時退路是照原樣；工具前的那段不即時發事件（補發會跑到畫面最後面）。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reply.Events.OfType<AssistantTextDelta>().Select(e => e.Delta), Is.EqualTo(new[] { "讀好了。" }));
            Assert.That(reply.Final.Parts, Is.EqualTo(new AssistantPart[]
            {
                new AssistantTextPart("我先讀設定。"),
                new AssistantToolPart("c1", "get_blueprint", AssistantToolStatus.Done, null, null),
                new AssistantTextPart("讀好了。"),
            }));
        }
    }

    [Test]
    public async Task Reply_ModelNeverWritesTheMarker_TheEarlierTextStillGoesBackToTheModelAsHistory()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script
            .Enqueue(Turn.Create().Text("我先讀設定。").Call("toolu_01", "get_blueprint"))
            .Reply("讀好了。")
            .Reply("第二則。");

        // Act
        await _h.SendAsync(conversation.Id, "幫我看看");
        await _h.SendAsync(conversation.Id, "再看一次");
        var history = _h.Script.Calls.Last().Messages
            .Where(m => m.Role == ChatRole.Assistant)
            .SelectMany(m => m.Contents).OfType<TextContent>().Select(t => t.Text).ToList();

        // Assert：畫面上有的文字，下一則送給模型的歷史也要有（沒照格式寫的模型不能因為這個過濾器少了自己說過的話）。
        Assert.That(history, Is.EqualTo(new[] { "[REPLY]\n我先讀設定。", "[REPLY]\n讀好了。" }));
    }

    [Test]
    public async Task Reply_MarkerBeforeATool_TextIsStreamedAtOnceAndKept()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script
            .Enqueue(Turn.Create().Text("[REPLY]\n我先讀設定。").Call("toolu_01", "get_blueprint"))
            .Reply("[REPLY]\n讀好了。");

        // Act
        var reply = await _h.SendAsync(conversation.Id, "幫我看看");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reply.Events.OfType<AssistantTextDelta>().Select(e => e.Delta), Is.EqualTo(new[] { "我先讀設定。", "讀好了。" }));
            Assert.That(reply.Final.Parts, Is.EqualTo(new AssistantPart[]
            {
                new AssistantTextPart("我先讀設定。"),
                new AssistantToolPart("c1", "get_blueprint", AssistantToolStatus.Done, null, null),
                new AssistantTextPart("讀好了。"),
            }));
        }
    }
}
