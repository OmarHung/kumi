using System.Text.Json;
using Microsoft.Extensions.AI;
using PmTool.Ai.Assistant;
using PmTool.Ai.Tests.Assistant.Fakes;

namespace PmTool.Ai.Tests.Assistant;

/// <summary>存下來的 transcript 與它再送給模型的樣子：呼叫編號重編、中斷時補結果、思考不存，以及系統提示。</summary>
[TestFixture]
public sealed class AssistantTranscriptTests
{
    private AssistantHarness _h = null!;

    [SetUp]
    public async Task StartAsync() => _h = await AssistantHarness.StartAsync();

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    [Test]
    public async Task Transcript_StoredShape_IsUserCallResultAndAnswerMessages()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script
            .Enqueue(Turn.Create().Text("先讀。").Call("toolu_01AAA", "get_blueprint"))
            .Reply("好了。");

        // Act
        var reply = await _h.SendAsync(conversation.Id, "改一下");
        var assistant = await _h.RowAsync(reply.Final.Id);
        var user = await _h.RowAsync(reply.Run.UserMessage.Id);

        // Assert
        using var transcript = JsonDocument.Parse(assistant.Transcript);
        var messages = transcript.RootElement.EnumerateArray().ToList();
        using var userTranscript = JsonDocument.Parse(user.Transcript);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(userTranscript.RootElement[0].GetProperty("role").GetString(), Is.EqualTo("user"));
            Assert.That(userTranscript.RootElement[0].GetProperty("contents")[0].GetProperty("text").GetString(), Is.EqualTo("改一下"));
            Assert.That(messages.Select(m => m.GetProperty("role").GetString()), Is.EqualTo(new[] { "assistant", "tool", "assistant" }));
            var call = messages[0].GetProperty("contents")[1];
            Assert.That(call.GetProperty("type").GetString(), Is.EqualTo("call"));
            Assert.That(call.GetProperty("callId").GetString(), Is.EqualTo("c1"));
            Assert.That(call.GetProperty("name").GetString(), Is.EqualTo("get_blueprint"));
            var result = messages[1].GetProperty("contents")[0];
            Assert.That(result.GetProperty("type").GetString(), Is.EqualTo("result"));
            Assert.That(result.GetProperty("callId").GetString(), Is.EqualTo("c1"));
            Assert.That(result.GetProperty("result").GetString(), Does.StartWith("{\"ok\":true"));
            using var parts = JsonDocument.Parse(assistant.Parts);
            Assert.That(parts.RootElement[1].GetProperty("callId").GetString(), Is.EqualTo("c1"));
        }
    }

    [Test]
    public async Task NextMessage_SendsSystemPromptProjectNoteHistoryThenTheNewText()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Enqueue(Turn.Create().Text("先讀。").Call("toolu_01AAA", "get_blueprint")).Reply("好了。");
        await _h.SendAsync(conversation.Id, "第一個問題");
        _h.Script.Reply("第二個答案");

        // Act
        await _h.SendAsync(conversation.Id, "第二個問題");

        // Assert
        var messages = _h.Script.Calls.Last().Messages;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(messages.Select(m => m.Role.Value), Is.EqualTo(new[]
            {
                "system", "system", "user", "assistant", "tool", "assistant", "user",
            }));
            Assert.That(messages[0].Text, Is.EqualTo(AssistantPrompt.SystemPrompt));
            Assert.That(messages[1].Text, Does.Contain("WEB").And.Not.Contain("網站改版"));
            Assert.That(messages[2].Text, Is.EqualTo("第一個問題"));
            Assert.That(messages[3].Text, Is.EqualTo("先讀。"));
            Assert.That(messages[5].Text, Is.EqualTo("好了。"));
            Assert.That(messages[6].Text, Is.EqualTo("第二個問題"));
        }
    }

    [Test]
    public async Task NextMessage_CallIdsAreRenumberedPerMessage_ProviderIdsNeverComeBack()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Enqueue(Turn.Create().Call("toolu_01AAA", "get_blueprint")).Reply("一");
        await _h.SendAsync(conversation.Id, "問一");
        _h.Script.Enqueue(Turn.Create().Call("call_ZZZ", "describe_usage")).Reply("二");
        await _h.SendAsync(conversation.Id, "問二");
        _h.Script.Reply("三");

        // Act
        await _h.SendAsync(conversation.Id, "問三");

        // Assert
        var contents = _h.Script.Calls.Last().Messages.SelectMany(m => m.Contents).ToList();
        var callIds = contents.OfType<FunctionCallContent>().Select(c => c.CallId).ToList();
        var resultIds = contents.OfType<FunctionResultContent>().Select(c => c.CallId).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(callIds, Is.EqualTo(new[] { "m2c1", "m4c1" }));
            Assert.That(resultIds, Is.EqualTo(new[] { "m2c1", "m4c1" }));
            Assert.That(callIds.Concat(resultIds), Has.None.Contain("toolu").And.None.Contain("call_ZZZ"));
        }
    }

    [Test]
    public async Task NextMessage_StoredToolArguments_AreSentBackAsTheyWere()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script
            .Enqueue(Turn.Create().Call("a", "get_template", new { templateKey = "simple_tasks" }))
            .Reply("好");
        await _h.SendAsync(conversation.Id, "問一");
        _h.Script.Reply("再");

        // Act
        await _h.SendAsync(conversation.Id, "問二");

        // Assert
        var call = _h.Script.Calls.Last().Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Single();
        Assert.That(((JsonElement)call.Arguments!["templateKey"]!).GetString(), Is.EqualTo("simple_tasks"));
    }

    [Test]
    public async Task Interrupted_UnfinishedToolCall_GetsAFailedResultSoTheNextMessageIsAccepted()
    {
        // Arrange：模型要呼叫工具、還沒執行就被中斷。
        var conversation = await _h.NewConversationAsync();
        _h.Usage.OnDescribeAsync = async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return default;
        };
        _h.Script.Enqueue(Turn.Create().Text("我來讀").Call("toolu_X", "describe_usage"));
        var run = await _h.StartAsync(conversation.Id, "問一");
        using var stop = new CancellationTokenSource();
        var events = new List<AssistantRunEvent>();
        await foreach (var runEvent in run.Events.WithCancellation(stop.Token))
        {
            events.Add(runEvent);
            if (runEvent is AssistantToolUpdate)
            {
                await stop.CancelAsync();
            }
        }

        _h.Script.Reply("接著說");

        // Act
        var stopped = events.OfType<AssistantRunCompleted>().Single().Message;
        await _h.SendAsync(conversation.Id, "問二");

        // Assert
        var sent = _h.Script.Calls.Last().Messages.SelectMany(m => m.Contents).ToList();
        var result = sent.OfType<FunctionResultContent>().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stopped.Status, Is.EqualTo(AssistantMessageStatus.Stopped));
            Assert.That(stopped.Parts.OfType<AssistantToolPart>().Single().Status, Is.EqualTo(AssistantToolStatus.Failed));
            Assert.That(stopped.Parts.OfType<AssistantToolPart>().Single().Error, Is.EqualTo("回覆中斷了。"));
            Assert.That(sent.OfType<FunctionCallContent>().Single().CallId, Is.EqualTo(result.CallId));
            Assert.That((string)result.Result!, Does.Contain("\"ok\":false").And.Contain("回覆中斷了"));
        }
    }

    [Test]
    public async Task NextMessage_FailedReplyBefore_StillSendsWhatWasSaid()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Enqueue(Turn.Create().Text("說到一半").Throw(new InvalidOperationException("壞了")));
        await _h.SendAsync(conversation.Id, "問一");
        _h.Script.Reply("好");

        // Act
        await _h.SendAsync(conversation.Id, "問二");

        // Assert
        Assert.That(_h.Script.Calls.Last().Messages.Select(m => m.Text), Is.EqualTo(new[]
        {
            AssistantPrompt.SystemPrompt, _h.Script.Calls.Last().Messages[1].Text, "問一", "說到一半", "問二",
        }));
    }

    [Test]
    public async Task Transcript_NulInToolArguments_IsDroppedSoTheMessageStillSaves()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script
            .Enqueue(Turn.Create().Call("a", "get_template", new { templateKey = "a\0b" }))
            .Reply("好");

        // Act
        var reply = await _h.SendAsync(conversation.Id);

        // Assert
        Assert.That(reply.Final.Status, Is.EqualTo(AssistantMessageStatus.Complete));
    }

    [Test]
    public void SystemPrompt_IsTheRulesFollowedByTheThreeReferencesInOrder_AndNeverChanges()
    {
        // Act
        var prompt = AssistantPrompt.SystemPrompt;

        // Assert
        var order = new[] { "project-blueprint.schema.json", "changeset.schema.json", "planning-structure.md" }
            .Select(name => prompt.IndexOf($"<reference name=\"{name}\">", StringComparison.Ordinal))
            .ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prompt, Does.StartWith("你是 Kumi 的「AI 助理」"));
            Assert.That(order, Has.All.GreaterThan(0));
            Assert.That(order, Is.Ordered);
            Assert.That(prompt, Does.Contain("</reference>"));
            Assert.That(AssistantPrompt.SystemPrompt, Is.SameAs(prompt));
        }
    }

    [TestCase(true, true, "能提出變更集；能核准")]
    [TestCase(true, false, "能提出變更集；不能核准")]
    [TestCase(false, false, "不能提出變更集；不能核准")]
    public void ProjectNote_SaysWhatTheUserMayDo(bool propose, bool apply, string expected)
    {
        // Arrange
        var permissions = new List<string> { PmTool.Work.WorkPermissions.BlueprintRead };
        if (propose)
        {
            permissions.Add(PmTool.Work.WorkPermissions.BlueprintPropose);
        }

        if (apply)
        {
            permissions.Add(PmTool.Work.WorkPermissions.BlueprintApply);
        }

        // Act
        var note = AssistantPrompt.ProjectNote("WEB", permissions);

        // Assert：專案名稱是專案管理者寫的內容，不放進 system 訊息，只放代號。
        Assert.That(note, Is.EqualTo($"這則對話在專案代號 WEB 的專案。使用者在這個專案：能讀設定；{expected}。"));
    }
}
