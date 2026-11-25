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
/// 本機代理一件工作的工具額度（AG-07）從 HTTP 一路到資料庫：真的 AI 模組、真的 MCP、真的工作引擎。
/// 測試扮演本機代理（登記、領工作、用工作的編號打 /mcp、結束），不跑真的引擎。
/// 第 14 次工具呼叫、第 3 份提出、沒帶 X-Kumi-Job、別人的工作編號都被擋，擋下的不碰工作引擎。
/// </summary>
[TestFixture]
public sealed class LocalAgentToolBudgetFlowTests : RealStackTestBase
{
    private const string Project = "AGBUD";

    private SignedInClient _owner = null!;
    private string _agentSecret = string.Empty;
    private Guid _engineId;
    private HttpClient _agent = null!;

    [OneTimeSetUp]
    public async Task SetUpAsync()
    {
        _owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(_owner.Client, Project, "simple_tasks");
        await RealStackSteps.CreateItemAsync(_owner.Client, Project, new { type = "task", title = "任務" });
        await AllowPersonalConnectionsAsync();

        using var created = await _owner.Client.PostAsync(
            "/v1/account/tokens",
            ApiRequests.Json(new
            {
                name = "agent",
                scopes = new[] { "agent:run", "items:read", "blueprint:read", "blueprint:write" },
                expiresInDays = 30,
            }));
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
    public async Task Job_FourteenthToolCall_IsRefusedAndNeverReachesTheWorkEngine()
    {
        // Arrange
        await using var job = await StartJobAsync();
        await using var mcp = await ConnectMcpAsync(job.JobId);

        // Act
        var results = new List<(bool IsError, string Text)>();
        for (var i = 0; i < AiRules.MaxAgentToolCallsPerJob + 1; i++)
        {
            results.Add(await CallAsync(mcp, "get_blueprint", new { projectKey = Project }));
        }

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Take(AiRules.MaxAgentToolCallsPerJob).Select(r => r.IsError), Has.All.False);
            Assert.That(results[^1].IsError, Is.True);
            Assert.That(results[^1].Text, Does.Contain("用完"));
        }
    }

    [Test]
    public async Task Job_ThirdProposal_IsRefusedWhileReadsStillWork()
    {
        // Arrange
        await using var job = await StartJobAsync();
        await using var mcp = await ConnectMcpAsync(job.JobId);

        // Act
        var first = await CallAsync(mcp, "propose_item_changes", Proposal("第一個標題"));
        var second = await CallAsync(mcp, "propose_item_changes", Proposal("第二個標題"));
        var third = await CallAsync(mcp, "propose_item_changes", Proposal("第三個標題"));
        var read = await CallAsync(mcp, "get_item", new { itemKey = $"{Project}-1" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.IsError, Is.False, first.Text);
            Assert.That(second.IsError, Is.False, second.Text);
            Assert.That(third.IsError, Is.True);
            Assert.That(third.Text, Does.Contain("上限"));
            Assert.That(read.IsError, Is.False, read.Text);
        }
    }

    [Test]
    public async Task Job_ProposalThatFails_GivesTheSlotBackSoTheModelCanRetry()
    {
        // Arrange
        await using var job = await StartJobAsync();
        await using var mcp = await ConnectMcpAsync(job.JobId);

        // Act：欄位不存在的建議會失敗，不能吃掉名額。
        var bad1 = await CallAsync(mcp, "propose_item_changes", new { projectKey = Project, itemKey = $"{Project}-1", changes = new[] { new { field = "no_such_field", value = "x" } } });
        var bad2 = await CallAsync(mcp, "propose_item_changes", new { projectKey = Project, itemKey = $"{Project}-1", changes = new[] { new { field = "no_such_field", value = "y" } } });
        var good1 = await CallAsync(mcp, "propose_item_changes", Proposal("好的一"));
        var good2 = await CallAsync(mcp, "propose_item_changes", Proposal("好的二"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(bad1.IsError, Is.True);
            Assert.That(bad2.IsError, Is.True);
            Assert.That(good1.IsError, Is.False, good1.Text);
            Assert.That(good2.IsError, Is.False, good2.Text);
        }
    }

    [Test]
    public async Task Request_WithoutTheJobHeader_IsAToolErrorTellingToUpdateKumi()
    {
        // Arrange
        await using var job = await StartJobAsync();
        await using var mcp = await ConnectMcpAsync(jobId: null);

        // Act
        var result = await CallAsync(mcp, "get_blueprint", new { projectKey = Project });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsError, Is.True);
            Assert.That(result.Text, Does.Contain("kumi update"));
        }
    }

    [Test]
    public async Task Request_WithAJobIdThatIsNotAClaimedJobOfThisPerson_IsAToolError()
    {
        // Arrange
        await using var job = await StartJobAsync();
        await using var guessed = await ConnectMcpAsync(Guid.NewGuid());

        // Act
        var result = await CallAsync(guessed, "get_blueprint", new { projectKey = Project });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsError, Is.True);
            Assert.That(result.Text, Does.Contain("找不到這件工作"));
        }
    }

    [Test]
    public async Task Request_AfterTheJobFinished_IsAToolError()
    {
        // Arrange
        var job = await StartJobAsync();
        await using var mcp = await ConnectMcpAsync(job.JobId);
        await job.FinishAsync();

        // Act
        var result = await CallAsync(mcp, "get_blueprint", new { projectKey = Project });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsError, Is.True);
            Assert.That(result.Text, Does.Contain("找不到這件工作"));
        }
    }

    [Test]
    public async Task Request_WhoseProjectHeaderIsNotTheJobsProject_IsAToolError()
    {
        // Arrange
        await RealStackSteps.CreateProjectAsync(_owner.Client, "AGOTH", "simple_tasks");
        await using var job = await StartJobAsync();
        await using var mcp = await ConnectMcpAsync(job.JobId, project: "AGOTH");

        // Act
        var result = await CallAsync(mcp, "get_blueprint", new { projectKey = "AGOTH" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsError, Is.True);
            Assert.That(result.Text, Does.Contain("找不到這件工作"));
        }
    }

    [Test]
    public async Task Job_ClaimedPayloadIsClearedButTheClaimResponseCarriesTheConversation()
    {
        // Arrange
        await using var job = await StartJobAsync("請把這句話帶給引擎");

        // Act
        var payload = await PayloadOfAsync(job.JobId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(job.Prompt, Does.Contain("請把這句話帶給引擎"));
            Assert.That(payload, Is.EqualTo("{}"));
        }
    }

    // ---- 扮演本機代理 ----

    private static object Proposal(string title) =>
        new { projectKey = Project, itemKey = $"{Project}-1", changes = new[] { new { field = "title", value = title } } };

    private async Task CheckInAsync(int capacity)
    {
        var data = await TryCheckInAsync(capacity);
        Assert.That(data, Is.Not.Null);
    }

    private async Task<JsonNode?> TryCheckInAsync(int capacity)
    {
        using var response = await _agent.PostAsync(
            "/v1/agent/check-in",
            ApiRequests.Json(new
            {
                machineKey = "machine-0001",
                machineName = "測試電腦",
                agentVersion = "0.9.0",
                engines = new[] { new { kind = "claude_code", version = "2.1.330", models = new[] { new { id = "default", displayName = "預設", efforts = Array.Empty<string>() } } } },
                capacity,
            }));
        return await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
    }

    private async Task<RunningJob> StartJobAsync(string text = "列出目前的狀態")
    {
        using var created = await _owner.Client.PostAsync($"/v1/projects/{Project}/assistant/conversations", null);
        var conversation = await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created);
        var route = $"/v1/projects/{Project}/assistant/conversations/{conversation!["id"]!.GetValue<Guid>()}/messages";

        // 回覆是 SSE，要等這件工作結束才讀得完：先放著，領到工作、測完、結束它之後才收。
        var send = _owner.Client.PostAsync(route, ApiRequests.Json(new { text, selection = new { connectionId = _engineId, model = "default" } }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!timeout.IsCancellationRequested)
        {
            var data = await TryCheckInAsync(capacity: 1);
            if (data?["job"] is JsonObject claimed)
            {
                return new RunningJob(this, send, claimed["jobId"]!.GetValue<Guid>(), claimed["prompt"]!.GetValue<string>());
            }
        }

        throw new TimeoutException("等不到本機代理的工作。");
    }

    private async Task<McpClient> ConnectMcpAsync(Guid? jobId, string project = Project)
    {
        var headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {_agentSecret}", ["X-Kumi-Project"] = project };
        if (jobId is { } id)
        {
            headers["X-Kumi-Job"] = id.ToString();
        }

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

    private async Task<string> PayloadOfAsync(Guid jobId)
    {
        await using var connection = new NpgsqlConnection(Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT payload::text FROM ai.local_agent_jobs WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", jobId);
        return (string)(await command.ExecuteScalarAsync())!;
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

    /// <summary>領到的一件工作加它背後還在等結果的回覆；測完用 <see cref="FinishAsync"/> 結束（沒結束的 DisposeAsync 會替你結束）。</summary>
    private sealed class RunningJob(LocalAgentToolBudgetFlowTests owner, Task<HttpResponseMessage> send, Guid jobId, string prompt) : IAsyncDisposable
    {
        private bool _finished;

        public Guid JobId { get; } = jobId;

        public string Prompt { get; } = prompt;

        public async Task FinishAsync()
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

        public async ValueTask DisposeAsync() => await FinishAsync();
    }
}
