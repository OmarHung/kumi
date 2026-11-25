using PmTool.Ai.Assistant;

namespace PmTool.Ai.Tests.LocalAgents;

/// <summary>本機的引擎把工具呼叫的標記吐成文字時：網頁看不到、存下來的與下一則送給引擎的也沒有。</summary>
[TestFixture]
public sealed class LocalAgentToolMarkupTests
{
    private LocalAgentHarness _h = null!;

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    [Test]
    public async Task Reply_EngineWritesToolCallMarkupAsText_ItIsLeftOutOfEventsPartsAndTheNextPrompt()
    {
        // Arrange
        _h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        var engine = await _h.RegisterAsync(name: "辦公室的 Mac");
        var first = await _h.SendAsync(engine, "幫我看看");
        var job = await _h.ClaimAsync();

        // Act：標記被切在兩次回報裡，後面接著真的工具呼叫。
        await _h.ReportAsync(job.JobId, LocalAgentHarness.Text("</invoke> <invoke name=\"mcp__kumi__get_item\"> <param"));
        await _h.ReportAsync(
            job.JobId,
            LocalAgentHarness.Text("eter name=\"itemKey\">APP-5</parameter> </invoke>"),
            LocalAgentHarness.Tool("toolu_1", "get_item", AssistantToolStatus.Running));
        await _h.ReportAsync(job.JobId, LocalAgentHarness.Tool("toolu_1", "get_item", AssistantToolStatus.Done));
        await _h.ReportAsync(job.JobId, LocalAgentHarness.Text("讀好了。"));
        await _h.FinishAsync(job.JobId);
        var events = await first.ReadAllAsync();
        var final = events.OfType<AssistantRunCompleted>().Single().Message;
        await _h.SendAsync(engine, "再看一次", conversationId: first.ConversationId);
        var second = await _h.ClaimAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(events.OfType<AssistantTextDelta>().Select(e => e.Delta), Is.EqualTo(new[] { "讀好了。" }));
            Assert.That(final.Parts, Is.EqualTo(new AssistantPart[]
            {
                new AssistantToolPart("c1", "get_item", AssistantToolStatus.Done, null, null),
                new AssistantTextPart("讀好了。"),
            }));
            Assert.That(second.Prompt, Does.Not.Contain("invoke"), "重送給引擎的對話不留標記。");
        }
    }

    [Test]
    public async Task Reply_StatusUpdateOfAToolAlreadyRunning_DoesNotCutAMarkupInHalf()
    {
        // Arrange
        _h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        var engine = await _h.RegisterAsync(name: "辦公室的 Mac");
        var run = await _h.SendAsync(engine, "幫我看看");
        var job = await _h.ClaimAsync();
        await _h.ReportAsync(job.JobId, LocalAgentHarness.Tool("toolu_1", "get_item", AssistantToolStatus.Running));

        // Act：標記切在一半時，前一個工具剛好回報完成；它不是新的一段，標記要接得起來。
        await _h.ReportAsync(job.JobId, LocalAgentHarness.Text("先看 <inv"));
        await _h.ReportAsync(job.JobId, LocalAgentHarness.Tool("toolu_1", "get_item", AssistantToolStatus.Done));
        await _h.ReportAsync(job.JobId, LocalAgentHarness.Text("oke name=\"mcp__kumi__get_item\"></invoke>好了"));
        await _h.FinishAsync(job.JobId);
        var events = await run.ReadAllAsync();

        // Assert
        Assert.That(string.Concat(events.OfType<AssistantTextDelta>().Select(e => e.Delta)), Is.EqualTo("先看 好了"));
    }
}
