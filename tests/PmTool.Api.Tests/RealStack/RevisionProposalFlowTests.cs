using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Npgsql;
using PmTool.Ai;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 刪除與批次修改的建議從 MCP 一路到資料庫：本機代理（測試扮演，不跑真的引擎）經 MCP 提出刪除建議、批次修改建議與撤回，
/// 使用者在網頁套用後，項目真的刪除、每個項目的到期日真的改了，撤回後建議是已略過。用真的 AI 模組、工作引擎與身分模組。
/// </summary>
[TestFixture]
public sealed class RevisionProposalFlowTests : RealStackTestBase
{
    private const string Project = "REVP";

    private SignedInClient _owner = null!;
    private string _agentSecret = string.Empty;
    private Guid _engineId;
    private HttpClient _agent = null!;

    [OneTimeSetUp]
    public async Task SetUpAsync()
    {
        _owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(_owner.Client, Project, "simple_tasks");
        await AllowPersonalConnectionsAsync();

        using var created = await _owner.Client.PostAsync(
            "/v1/account/tokens",
            ApiRequests.Json(new { name = "agent", scopes = new[] { "agent:run", "items:read", "blueprint:read", "blueprint:write" }, expiresInDays = 30 }));
        _agentSecret = (await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created))!["secret"]!.GetValue<string>();
        _agent = Factory.CreateClient();
        _agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _agentSecret);

        await CheckInAsync(capacity: 0);
        var options = await RealStackSteps.GetAsync(_owner.Client, "/v1/ai/options");
        _engineId = options["connections"]!.AsArray().Single(c => c!["kind"]!.GetValue<string>() == "claude_code")!["id"]!.GetValue<Guid>();
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        _agent?.Dispose();
        _owner?.Dispose();
    }

    [Test]
    public async Task DeletionProposal_ProposedOverMcpAndApplied_SoftDeletesTheItems()
    {
        // Arrange
        var keep = (await RealStackSteps.CreateItemAsync(_owner.Client, Project, new { type = "task", title = "留著" }))["key"]!.GetValue<string>();
        var dup1 = (await RealStackSteps.CreateItemAsync(_owner.Client, Project, new { type = "task", title = "重複一" }))["key"]!.GetValue<string>();
        var dup2 = (await RealStackSteps.CreateItemAsync(_owner.Client, Project, new { type = "task", title = "重複二" }))["key"]!.GetValue<string>();
        await using var job = await StartJobAsync();
        await using var mcp = await ConnectMcpAsync(job.JobId);

        // Act
        var proposed = await CallAsync(mcp, "propose_item_deletions", new { projectKey = Project, itemKeys = new[] { dup1, dup2 }, reason = "重複建立" });
        var proposalId = JsonNode.Parse(proposed.Text)!["proposalId"]!.GetValue<Guid>();
        var beforeStatus = await RealStackSteps.StatusOfAsync(_owner.Client, $"/v1/items/{dup1}");
        using var apply = await _owner.Client.PostAsync($"/v1/ai/item-deletion-proposals/{proposalId}/apply", null);
        var applied = await ApiRequests.AssertSuccessEnvelopeAsync(apply, HttpStatusCode.OK);
        var afterStatus = await RealStackSteps.StatusOfAsync(_owner.Client, $"/v1/items/{dup1}");
        var keptStatus = await RealStackSteps.StatusOfAsync(_owner.Client, $"/v1/items/{keep}");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(proposed.IsError, Is.False, proposed.Text);
            Assert.That(beforeStatus, Is.EqualTo(HttpStatusCode.OK), "提出建議時不會真的刪");
            Assert.That(applied!["status"]!.GetValue<string>(), Is.EqualTo("applied"));
            Assert.That(applied["deletedKeys"]!.AsArray().Select(k => k!.GetValue<string>()), Is.EquivalentTo(new[] { dup1, dup2 }));
            Assert.That(afterStatus, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(keptStatus, Is.EqualTo(HttpStatusCode.OK));
        }
    }

    [Test]
    public async Task BatchProposal_ProposedOverMcpAndApplied_ChangesEachItemsDueDate()
    {
        // Arrange
        var keys = new List<string>();
        foreach (var title in new[] { "甲", "乙", "丙" })
        {
            keys.Add((await RealStackSteps.CreateItemAsync(_owner.Client, Project, new { type = "task", title, dueDate = "2026-11-20" }))["key"]!.GetValue<string>());
        }

        await using var job = await StartJobAsync();
        await using var mcp = await ConnectMcpAsync(job.JobId);

        // Act
        var proposed = await CallAsync(mcp, "propose_bulk_item_changes", new
        {
            projectKey = Project,
            items = keys.Select((key, index) => new { itemKey = key, changes = new[] { new { field = "dueDate", value = $"2026-12-0{index + 1}" } } }).ToArray(),
        });
        var proposalId = JsonNode.Parse(proposed.Text)!["proposalId"]!.GetValue<Guid>();
        var before = await RealStackSteps.GetAsync(_owner.Client, $"/v1/items/{keys[0]}");
        using var apply = await _owner.Client.PostAsync($"/v1/ai/item-batch-proposals/{proposalId}/apply", null);
        var applied = await ApiRequests.AssertSuccessEnvelopeAsync(apply, HttpStatusCode.OK);
        var after = new List<string?>();
        foreach (var key in keys)
        {
            after.Add((await RealStackSteps.GetAsync(_owner.Client, $"/v1/items/{key}"))["dueDate"]?.GetValue<string>());
        }

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(proposed.IsError, Is.False, proposed.Text);
            Assert.That(before["dueDate"]!.GetValue<string>(), Is.EqualTo("2026-11-20"), "提出建議時不會真的改");
            Assert.That(applied!["status"]!.GetValue<string>(), Is.EqualTo("applied"));
            Assert.That(after, Is.EqualTo(new[] { "2026-12-01", "2026-12-02", "2026-12-03" }));
        }
    }

    [Test]
    public async Task WithdrawProposal_InTheNextReplyOfTheSameConversation_LeavesTheProposalDismissedAndTheItemUntouched()
    {
        // Arrange：第一件工作提出刪除建議，kumi 回報工具的結果、工作結束，對話才記得這份建議。
        var key = (await RealStackSteps.CreateItemAsync(_owner.Client, Project, new { type = "task", title = "不要刪" }))["key"]!.GetValue<string>();
        Guid conversationId;
        Guid proposalId;
        await using (var first = await StartJobAsync())
        {
            conversationId = first.ConversationId;
            await using var mcp = await ConnectMcpAsync(first.JobId);
            var proposed = await CallAsync(mcp, "propose_item_deletions", new { projectKey = Project, itemKeys = new[] { key } });
            proposalId = JsonNode.Parse(proposed.Text)!["proposalId"]!.GetValue<Guid>();
            using var reported = await _agent.PostAsync(
                $"/v1/agent/jobs/{first.JobId}/events",
                ApiRequests.Json(new { events = new object[] { new { type = "tool", callId = "toolu_1", name = "propose_item_deletions", status = "done", itemDeletionProposalId = proposalId } } }));
            Assert.That(reported.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        // Act：使用者說「不要了」，下一件工作撤回它。
        await using var second = await StartJobAsync(conversationId);
        await using var mcpSecond = await ConnectMcpAsync(second.JobId);
        var withdrawn = await CallAsync(mcpSecond, "withdraw_proposal", new { proposalId = proposalId.ToString() });
        var read = await RealStackSteps.GetAsync(_owner.Client, $"/v1/ai/item-deletion-proposals/{proposalId}");
        using var apply = await _owner.Client.PostAsync($"/v1/ai/item-deletion-proposals/{proposalId}/apply", null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(withdrawn.IsError, Is.False, withdrawn.Text);
            Assert.That(JsonNode.Parse(withdrawn.Text)!["state"]!.GetValue<string>(), Is.EqualTo("dismissed"));
            Assert.That(read["status"]!.GetValue<string>(), Is.EqualTo("dismissed"));
            await ApiRequests.AssertErrorEnvelopeAsync(apply, HttpStatusCode.Conflict, AiErrorCodes.ItemDeletionProposalClosed);
            Assert.That(await RealStackSteps.StatusOfAsync(_owner.Client, $"/v1/items/{key}"), Is.EqualTo(HttpStatusCode.OK));
        }
    }

    // ---- 扮演本機代理（和 LocalAgentToolBudgetFlowTests 同一套做法）----

    private async Task CheckInAsync(int capacity) => Assert.That(await TryCheckInAsync(capacity), Is.Not.Null);

    private async Task<JsonNode?> TryCheckInAsync(int capacity)
    {
        using var response = await _agent.PostAsync(
            "/v1/agent/check-in",
            ApiRequests.Json(new
            {
                machineKey = "machine-0001",
                machineName = "測試電腦",
                agentVersion = "0.17.0",
                engines = new[] { new { kind = "claude_code", version = "2.1.330", models = new[] { new { id = "default", displayName = "預設", efforts = Array.Empty<string>() } } } },
                capacity,
            }));
        return await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
    }

    private async Task<RunningJob> StartJobAsync(Guid? conversationId = null)
    {
        if (conversationId is null)
        {
            using var created = await _owner.Client.PostAsync($"/v1/projects/{Project}/assistant/conversations", null);
            conversationId = (await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created))!["id"]!.GetValue<Guid>();
        }

        var route = $"/v1/projects/{Project}/assistant/conversations/{conversationId}/messages";

        // 回覆是 SSE，要等這件工作結束才讀得完：先放著，領到工作、測完、結束它之後才收。
        var send = _owner.Client.PostAsync(route, ApiRequests.Json(new { text = "請把他加進專案", selection = new { connectionId = _engineId, model = "default" } }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!timeout.IsCancellationRequested)
        {
            var data = await TryCheckInAsync(capacity: 1);
            if (data?["job"] is JsonObject claimed)
            {
                return new RunningJob(this, send, claimed["jobId"]!.GetValue<Guid>(), conversationId.Value);
            }
        }

        throw new TimeoutException("等不到本機代理的工作。");
    }

    private async Task<McpClient> ConnectMcpAsync(Guid jobId)
    {
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {_agentSecret}",
            ["X-Kumi-Project"] = Project,
            ["X-Kumi-Job"] = jobId.ToString(),
        };
        var http = Factory.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, McpTesting.Route), AdditionalHeaders = headers },
            http,
            loggerFactory: null,
            ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    private static async Task<(bool IsError, string Text)> CallAsync(McpClient client, string tool, object arguments)
    {
        var dictionary = JsonSerializer.SerializeToElement(arguments).EnumerateObject()
            .ToDictionary(property => property.Name, property => (object?)property.Value.Clone());
        var result = await client.CallToolAsync(tool, dictionary);
        return (result.IsError ?? false, result.Content.OfType<TextContentBlock>().Single().Text);
    }

    private async Task AllowPersonalConnectionsAsync()
    {
        await using var connection = new NpgsqlConnection(Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO ai.settings (organization_id, enabled, allow_personal_connections, updated_at, updated_by)
            SELECT id, false, true, now(), @person FROM identity.organizations
            ON CONFLICT (organization_id) DO UPDATE SET allow_personal_connections = true
            """,
            connection);
        command.Parameters.AddWithValue("person", _owner.PersonId);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class RunningJob(RevisionProposalFlowTests owner, Task<HttpResponseMessage> send, Guid jobId, Guid conversationId) : IAsyncDisposable
    {
        private bool _finished;

        public Guid JobId { get; } = jobId;

        public Guid ConversationId { get; } = conversationId;

        public async ValueTask DisposeAsync()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            using var finished = await owner._agent.PostAsync($"/v1/agent/jobs/{JobId}/finish", ApiRequests.Json(new { status = "complete" }));
            Assert.That(finished.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            using var response = await send;
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            await response.Content.ReadAsStringAsync();
        }
    }
}
