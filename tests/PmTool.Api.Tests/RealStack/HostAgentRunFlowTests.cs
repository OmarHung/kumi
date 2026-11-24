using System.Net;
using System.Text.Json.Nodes;
using PmTool.Ai;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 主機代理跑一件工作的整條路：兩個不同角色的成員送出 → 排隊（位置）→ worker 領工作、回報、結束 →
/// 兩邊的事件串流都收到 queued／started／文字／done；工作的工具授權（kjt_）打 /mcp：只列允許的工具、身分是發問的人、
/// 看不到「只看自己」的人看不到的項目、提出修改建議重送不重複、工作結束後失效。
/// </summary>
[TestFixture]
public sealed class HostAgentRunFlowTests : HostAgentFlowBase
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [OneTimeSetUp]
    public Task SetUpAsync() => SetUpHostAsync();

    [Test]
    public async Task TwoRolesSend_TheyQueueInOrder_AndBothStreamsGetQueuedStartedTextAndDone()
    {
        // Arrange：成員先送、管理員後送；同時只有一件在跑，管理員排第二。
        using var memberResponse = await SendAsync(Member, "成員的問題");
        var memberStream = new AssistantStreamTests.SseReader(memberResponse);
        var memberStart = await memberStream.ReadFrameAsync(Patience);
        var memberQueued = await memberStream.ReadFrameAsync(Patience);
        using var adminResponse = await SendAsync(Admin, "管理員的問題");
        var adminStream = new AssistantStreamTests.SseReader(adminResponse);
        var adminStart = await adminStream.ReadFrameAsync(Patience);
        var adminQueued = await adminStream.ReadFrameAsync(Patience);

        // Act：worker 領第一件、回報、結束；再領第二件。
        var first = await NextJobAsync();
        var memberStarted = await memberStream.ReadFrameAsync(Patience);
        await ReportTextAsync(first, "給成員的回覆");
        await FinishAsync(first);
        var memberRest = await ReadRestAsync(memberStream);
        var second = await NextJobAsync();
        var adminAfter = await ReadFramesUntilAsync(adminStream, "started");
        await ReportTextAsync(second, "給管理員的回覆");
        await FinishAsync(second);
        var adminRest = await ReadRestAsync(adminStream);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((memberStart?.Event, memberQueued?.Event), Is.EqualTo(("start", "queued")));
            Assert.That(JsonNode.Parse(memberQueued!.Data!)!["position"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That((adminStart?.Event, adminQueued?.Event), Is.EqualTo(("start", "queued")));
            Assert.That(JsonNode.Parse(adminQueued!.Data!)!["position"]!.GetValue<int>(), Is.EqualTo(2), "前面還有一件");
            Assert.That(memberStarted?.Event, Is.EqualTo("started"));
            Assert.That(memberRest.Select(f => f.Event), Is.EqualTo(new[] { "text", "done" }));
            Assert.That(JsonNode.Parse(memberRest[0].Data!)!["delta"]!.GetValue<string>(), Is.EqualTo("給成員的回覆"));
            Assert.That(JsonNode.Parse(memberRest[1].Data!)!["message"]!["status"]!.GetValue<string>(), Is.EqualTo("complete"));
            Assert.That(first["projectKey"]!.GetValue<string>(), Is.EqualTo(Project));
            Assert.That(first["prompt"]!.GetValue<string>(), Does.Contain("成員的問題"));
            Assert.That(first["mcpUrl"]!.GetValue<string>(), Is.EqualTo($"{ServerUrl}/mcp"));
            Assert.That(first["toolToken"]!.GetValue<string>(), Does.StartWith("kjt_"));
            Assert.That(second["prompt"]!.GetValue<string>(), Does.Contain("管理員的問題"));
            Assert.That(adminAfter.Select(f => f.Event), Does.Contain("started"));
            Assert.That(adminRest.Select(f => f.Event), Is.EqualTo(new[] { "text", "done" }));
        }
    }

    [Test]
    public async Task SecondMessageFromTheSamePersonWhileOneIsWaiting_Is409QueueFull_AndNothingIsStored()
    {
        // Arrange
        using var first = await SendAsync(Member, "第一則");
        var firstStream = new AssistantStreamTests.SseReader(first);
        await firstStream.ReadFrameAsync(Patience);

        // Act
        using var second = await SendAsync(Member, "第二則");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(second, HttpStatusCode.Conflict, AiErrorCodes.HostQueueFull);

        // Cleanup：讓第一則跑完，不留排隊的工作給別的測試。
        var job = await NextJobAsync();
        await FinishAsync(job);
        await ReadRestAsync(firstStream);
    }

    [Test]
    public async Task ToolGrant_ListsOnlyAllowedTools_ActsAsTheAsker_AndEndsWithTheJob()
    {
        // Arrange
        var own = await RealStackSteps.CreateItemAsync(Contributor.Client, Project, new { type = "task", title = "執行者自己的項目" });
        var others = await RealStackSteps.CreateItemAsync(Owner.Client, Project, new { type = "task", title = "別人的項目" });
        using var response = await SendAsync(Contributor, "看一下我的項目");
        var stream = new AssistantStreamTests.SseReader(response);
        await stream.ReadFrameAsync(Patience);
        var job = await NextJobAsync();
        var token = job["toolToken"]!.GetValue<string>();
        var allowed = job["allowedTools"]!.AsArray().Select(t => t!.GetValue<string>()).ToArray();
        await using var mcp = await ConnectToolsAsync(token);

        // Act
        var tools = await mcp.ListToolsAsync();
        var (ownError, _) = await McpTesting.CallAsync(mcp, "get_item", new { itemKey = own["key"]!.GetValue<string>() });
        var (othersError, othersBody) = await McpTesting.CallAsync(mcp, "get_item", new { itemKey = others["key"]!.GetValue<string>() });
        var (writeError, writeBody) = await McpTesting.CallAsync(mcp, "update_item", new { itemKey = own["key"]!.GetValue<string>(), title = "改掉" });
        await FinishAsync(job);
        await ReadRestAsync(stream);
        using var http = Factory.CreateClient();
        using var after = await McpTesting.PostRawAsync(http, token);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tools.Select(t => t.Name).Order(), Is.EqualTo(allowed.Order()));
            Assert.That(tools.Select(t => t.Name), Does.Not.Contain("update_item"));
            Assert.That(ownError, Is.False, "看得到自己的項目");
            Assert.That(othersError, Is.True, "只看自己的人看不到別人的項目");
            Assert.That(othersBody["error"]?.GetValue<string>(), Does.Contain("找不到"));
            Assert.That(writeError, Is.True);
            Assert.That(writeBody["error"]?.GetValue<string>(), Does.Contain("不能用"));
            Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "工作結束後授權失效");
        }
    }

    [Test]
    public async Task ToolGrant_ProposeItemChangesTwice_CreatesOneProposal()
    {
        // Arrange
        var item = await RealStackSteps.CreateItemAsync(Member.Client, Project, new { type = "task", title = "要提出修改的項目" });
        var key = item["key"]!.GetValue<string>();
        using var response = await SendAsync(Member, "幫我改標題", itemKey: key);
        var stream = new AssistantStreamTests.SseReader(response);
        await stream.ReadFrameAsync(Patience);
        var job = await NextJobAsync();
        await using var mcp = await ConnectToolsAsync(job["toolToken"]!.GetValue<string>());
        var arguments = new { projectKey = Project, itemKey = key, changes = new[] { new { field = "title", value = "新標題" } }, comment = "改標題" };

        // Act
        var (firstError, first) = await McpTesting.CallAsync(mcp, "propose_item_changes", arguments);
        var (secondError, second) = await McpTesting.CallAsync(mcp, "propose_item_changes", arguments);
        await FinishAsync(job);
        await ReadRestAsync(stream);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((firstError, secondError), Is.EqualTo((false, false)), $"{first.ToJsonString()} / {second.ToJsonString()}");
            Assert.That(second["proposalId"]!.GetValue<Guid>(), Is.EqualTo(first["proposalId"]!.GetValue<Guid>()), "重送拿到同一份");
        }
    }

    private static async Task<IReadOnlyList<AssistantStreamTests.SseFrame>> ReadRestAsync(AssistantStreamTests.SseReader reader)
    {
        var frames = new List<AssistantStreamTests.SseFrame>();
        while (await reader.ReadFrameAsync(Patience) is { } frame)
        {
            frames.Add(frame);
        }

        return frames;
    }

    private static async Task<IReadOnlyList<AssistantStreamTests.SseFrame>> ReadFramesUntilAsync(AssistantStreamTests.SseReader reader, string eventName)
    {
        var frames = new List<AssistantStreamTests.SseFrame>();
        while (await reader.ReadFrameAsync(Patience) is { } frame)
        {
            frames.Add(frame);
            if (frame.Event == eventName)
            {
                break;
            }
        }

        return frames;
    }
}
