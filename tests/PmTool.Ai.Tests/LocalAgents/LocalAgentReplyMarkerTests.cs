using PmTool.Ai.Assistant;

namespace PmTool.Ai.Tests.LocalAgents;

/// <summary>回覆的格式限定（本機代理）：只顯示、只存單獨一行 <c>[REPLY]</c> 之後的文字；重送給引擎的對話把標記補回去。</summary>
[TestFixture]
public sealed class LocalAgentReplyMarkerTests
{
    private LocalAgentHarness _h = null!;

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    private async Task<(IReadOnlyList<AssistantRunEvent> Events, AssistantMessage Final, string NextPrompt)> RunAsync(
        params LocalAgentEvent[][] reports)
    {
        _h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        var engine = await _h.RegisterAsync(name: "辦公室的 Mac");
        var first = await _h.SendAsync(engine, "幫我看看");
        var job = await _h.ClaimAsync();
        foreach (var report in reports)
        {
            await _h.ReportAsync(job.JobId, report);
        }

        await _h.FinishAsync(job.JobId);
        var events = await first.ReadAllAsync();
        var final = events.OfType<AssistantRunCompleted>().Single().Message;
        await _h.SendAsync(engine, "再看一次", conversationId: first.ConversationId);
        var second = await _h.ClaimAsync();
        return (events, final, second.Prompt);
    }

    [Test]
    public async Task Reply_JunkBeforeATool_IsNotShownStoredOrSentBackToTheEngine()
    {
        // Act
        var (events, final, prompt) = await RunAsync(
            [LocalAgentHarness.Text("Thinking...</thinking_mode>"), LocalAgentHarness.Tool("toolu_1", "get_item", AssistantToolStatus.Running)],
            [LocalAgentHarness.Tool("toolu_1", "get_item", AssistantToolStatus.Done)],
            [LocalAgentHarness.Text("Let me answer.\n\n[REPLY]\n讀好了。")]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(events.OfType<AssistantTextDelta>().Select(e => e.Delta), Is.EqualTo(new[] { "讀好了。" }));
            Assert.That(final.Parts, Is.EqualTo(new AssistantPart[]
            {
                new AssistantToolPart("c1", "get_item", AssistantToolStatus.Done, null, null),
                new AssistantTextPart("讀好了。"),
            }));
            Assert.That(prompt, Does.Not.Contain("Thinking").And.Contain("<<<助理>>>\n[REPLY]\n讀好了。\n<<<結束>>>"));
        }
    }

    [Test]
    public async Task Reply_ReasoningBeforeTheMarkerInTheLastSegment_OnlyTheAnswerRemains()
    {
        // Act
        var (events, final, prompt) = await RunAsync(
            [LocalAgentHarness.Text("Agile concept question — in scope.\n\n[REP")],
            [LocalAgentHarness.Text("LY]\n故事點數估的是相對大小。")]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(events.OfType<AssistantTextDelta>().Select(e => e.Delta), Is.EqualTo(new[] { "故事點數估的是相對大小。" }));
            Assert.That(final.Parts, Is.EqualTo(new AssistantPart[] { new AssistantTextPart("故事點數估的是相對大小。") }));
            Assert.That(prompt, Does.Not.Contain("Agile").And.Contain("[REPLY]\n故事點數估的是相對大小。"));
        }
    }

    [Test]
    public async Task Reply_EngineNeverWritesTheMarker_FinalPartsKeepEverythingInPlaceButTheEarlierTextIsNotStreamed()
    {
        // Act
        var (events, final, _) = await RunAsync(
            [LocalAgentHarness.Text("我先讀設定。"), LocalAgentHarness.Tool("toolu_1", "get_item", AssistantToolStatus.Running)],
            [LocalAgentHarness.Tool("toolu_1", "get_item", AssistantToolStatus.Done)],
            [LocalAgentHarness.Text("讀好了。")]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(events.OfType<AssistantTextDelta>().Select(e => e.Delta), Is.EqualTo(new[] { "讀好了。" }));
            Assert.That(final.Parts, Is.EqualTo(new AssistantPart[]
            {
                new AssistantTextPart("我先讀設定。"),
                new AssistantToolPart("c1", "get_item", AssistantToolStatus.Done, null, null),
                new AssistantTextPart("讀好了。"),
            }));
        }
    }

    [Test]
    public async Task Reply_EngineNeverWritesTheMarker_TheEarlierTextStillGoesBackToTheEngineAsHistory()
    {
        // Act
        var (_, _, prompt) = await RunAsync(
            [LocalAgentHarness.Text("我先讀設定。"), LocalAgentHarness.Tool("toolu_1", "get_item", AssistantToolStatus.Running)],
            [LocalAgentHarness.Tool("toolu_1", "get_item", AssistantToolStatus.Done)],
            [LocalAgentHarness.Text("讀好了。")]);

        // Assert：畫面上有的文字，重送給引擎的對話也要有。
        Assert.That(prompt, Does.Contain("我先讀設定。").And.Contain("讀好了。"));
    }

    [Test]
    public async Task Reply_EngineNeverWritesTheMarkerAndOnlySpeaksBeforeATool_TheTranscriptKeepsThoseWords()
    {
        // Act：整則只有工具前的一句話，沒有最後的回答。
        var (_, final, prompt) = await RunAsync(
            [LocalAgentHarness.Text("我先讀設定。"), LocalAgentHarness.Tool("toolu_1", "get_item", AssistantToolStatus.Running)],
            [LocalAgentHarness.Tool("toolu_1", "get_item", AssistantToolStatus.Done)]);

        // Assert：不能存成「沒有文字回覆」，畫面上明明有那句話。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(final.Parts.OfType<AssistantTextPart>().Select(p => p.Text), Is.EqualTo(new[] { "我先讀設定。" }));
            Assert.That(prompt, Does.Contain("我先讀設定。").And.Not.Contain("沒有文字回覆"));
        }
    }
}
