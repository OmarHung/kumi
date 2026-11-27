using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Npgsql;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 跨專案查項目（find_items）從 /mcp 一路到資料庫：真的 AI 模組、真的工作引擎。
/// 一般權杖只查得到自己看得到的；本機代理的組織對話查到別人的項目之後，對話記了讀過的專案——
/// 之後他在那個專案只剩看自己的，對話就被刪掉（這一段靠「讀過的專案」的模組，沒有它時對話會留著）。
/// </summary>
[TestFixture]
public sealed class FindItemsFlowTests : RealStackTestBase
{
    private const string Project = "FNDIT";
    private const string LeadEmail = "find-lead@example.com";

    private SignedInClient _owner = null!;
    private SignedInClient _lead = null!;
    private SignedInClient _worker = null!;
    private string _ownItemKey = string.Empty;
    private string _othersItemKey = string.Empty;
    private string _agentSecret = string.Empty;
    private HttpClient _agent = null!;
    private Guid _engineId;

    [OneTimeSetUp]
    public async Task SetUpAsync()
    {
        _owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(_owner.Client, Project, "simple_tasks");
        _lead = await AddMemberAsync(_owner, Project, "admin", "Lead Example", LeadEmail);
        _worker = await AddMemberAsync(_owner, Project, "contributor", "Worker Example", "find-worker@example.com");
        var own = await RealStackSteps.CreateItemAsync(_owner.Client, Project, new { type = "task", title = "老闆的任務" });
        var others = await RealStackSteps.CreateItemAsync(_owner.Client, Project, new { type = "task", title = "執行者的任務", assigneeId = _worker.PersonId });
        _ownItemKey = own["key"]!.GetValue<string>();
        _othersItemKey = others["key"]!.GetValue<string>();
        await AllowPersonalConnectionsAsync();

        using var created = await _lead.Client.PostAsync(
            "/v1/account/tokens",
            ApiRequests.Json(new { name = "agent", scopes = new[] { "agent:run", "items:read", "blueprint:read", "blueprint:write" }, expiresInDays = 30 }));
        _agentSecret = (await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created))!["secret"]!.GetValue<string>();
        _agent = Factory.CreateClient();
        _agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _agentSecret);
        await TryCheckInAsync(capacity: 0);
        var options = await RealStackSteps.GetAsync(_lead.Client, "/v1/ai/options");
        _engineId = options["connections"]!.AsArray().Single(c => c!["kind"]!.GetValue<string>() == "claude_code")!["id"]!.GetValue<Guid>();
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        _agent?.Dispose();
        _lead?.Dispose();
        _worker?.Dispose();
        _owner?.Dispose();
    }

    [Test]
    public async Task OrdinaryToken_FindsOnlyWhatItsPersonCanSee()
    {
        // Arrange：執行者只看得到自己負責或建立的；擁有者看得到全部。
        using var cli = Factory.CreateClient();
        var (workerAccess, _, _) = await OAuthFlowTests.ConnectAsync(_worker.Client, cli);
        var (ownerAccess, _, _) = await OAuthFlowTests.ConnectAsync(_owner.Client, cli);
        await using var workerMcp = await ConnectMcpAsync(workerAccess);
        await using var ownerMcp = await ConnectMcpAsync(ownerAccess);

        // Act
        var (workerError, workerBody) = await McpTesting.CallAsync(workerMcp, "find_items", new { projectKey = Project });
        var (ownerError, ownerBody) = await McpTesting.CallAsync(ownerMcp, "find_items", new { projectKey = Project });
        var (assigneeError, assigneeBody) = await McpTesting.CallAsync(ownerMcp, "find_items", new { assignee = _worker.PersonId.ToString() });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(workerError, Is.False, workerBody.ToJsonString());
            Assert.That(KeysOf(workerBody), Is.EquivalentTo(new[] { _othersItemKey }));
            Assert.That(ownerError, Is.False, ownerBody.ToJsonString());
            Assert.That(KeysOf(ownerBody), Is.EquivalentTo(new[] { _ownItemKey, _othersItemKey }));
            Assert.That(assigneeError, Is.False, assigneeBody.ToJsonString());
            Assert.That(KeysOf(assigneeBody), Is.EquivalentTo(new[] { _othersItemKey }));
            Assert.That(ownerBody["items"]![0]!["projectName"]?.GetValue<string>(), Is.Not.Empty);
            Assert.That(ownerBody["hasMore"]?.GetValue<bool>(), Is.False);
        }
    }

    [Test]
    public async Task OrganizationJob_ReadingSomeoneElsesItems_IsRememberedSoDemotingTheReaderDeletesTheConversation()
    {
        // Arrange：組織對話（沒有專案），主管用本機代理查別人的項目。
        var (conversationId, job) = await StartOrganizationJobAsync();
        await using var mcp = await ConnectMcpAsync(_agentSecret, job.JobId);

        // Act
        var (error, body) = await McpTesting.CallAsync(mcp, "find_items", new { assignee = _worker.PersonId.ToString() });
        var (peopleError, _) = await McpTesting.CallAsync(mcp, "list_people", new { text = "Worker" });
        await job.FinishAsync();
        using var before = await _lead.Client.GetAsync($"/v1/assistant/conversations/{conversationId}");
        using var demote = await RealStackSteps.PutMemberAsync(_owner.Client, Project, _lead.PersonId, "contributor");
        using var after = await _lead.Client.GetAsync($"/v1/assistant/conversations/{conversationId}");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error, Is.False, body.ToJsonString());
            Assert.That(KeysOf(body), Is.EquivalentTo(new[] { _othersItemKey }));
            Assert.That(peopleError, Is.False);
            Assert.That(before.StatusCode, Is.EqualTo(HttpStatusCode.OK), "權限沒變時對話照常");
            Assert.That(demote.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "讀過別人的項目的對話，在他只剩看自己的之後整則刪掉");
        }
    }

    private static string[] KeysOf(JsonNode body) =>
        [.. body["items"]!.AsArray().Select(item => item!["key"]!.GetValue<string>())];

    private async Task<McpClient> ConnectMcpAsync(string secret, Guid? jobId = null)
    {
        var headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {secret}" };
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

    private async Task<JsonNode?> TryCheckInAsync(int capacity)
    {
        using var response = await _agent.PostAsync(
            "/v1/agent/check-in",
            ApiRequests.Json(new
            {
                machineKey = "machine-find-1",
                machineName = "測試電腦",
                agentVersion = "0.19.0",
                engines = new[] { new { kind = "claude_code", version = "2.1.330", models = new[] { new { id = "default", displayName = "預設", efforts = Array.Empty<string>() } } } },
                capacity,
            }));
        return await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
    }

    private async Task<(Guid ConversationId, RunningJob Job)> StartOrganizationJobAsync()
    {
        using var created = await _lead.Client.PostAsync("/v1/assistant/conversations", null);
        var conversation = await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created);
        var id = conversation!["id"]!.GetValue<Guid>();

        // 回覆是 SSE，要等這件工作結束才讀得完：先放著，領到工作、測完、結束它之後才收。
        var send = _lead.Client.PostAsync(
            $"/v1/assistant/conversations/{id}/messages",
            ApiRequests.Json(new { text = "Worker 有哪些工作", selection = new { connectionId = _engineId, model = "default" } }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!timeout.IsCancellationRequested)
        {
            var data = await TryCheckInAsync(capacity: 1);
            if (data?["job"] is JsonObject claimed)
            {
                return (id, new RunningJob(this, send, claimed["jobId"]!.GetValue<Guid>()));
            }
        }

        throw new TimeoutException("等不到本機代理的工作。");
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

    private sealed class RunningJob(FindItemsFlowTests owner, Task<HttpResponseMessage> send, Guid jobId)
    {
        private bool _finished;

        public Guid JobId { get; } = jobId;

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
    }
}
