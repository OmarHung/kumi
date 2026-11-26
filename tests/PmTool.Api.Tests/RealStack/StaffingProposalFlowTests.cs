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
/// 派工的成員建議從 MCP 一路到資料庫：本機代理（測試扮演，不跑真的引擎）經 MCP 提出成員建議與帶 addMembers 的新增項目建議，
/// 使用者在網頁套用後，人真的成為專案成員、項目真的指派給他。用真的 AI 模組、工作引擎與身分模組。
/// </summary>
[TestFixture]
public sealed class StaffingProposalFlowTests : RealStackTestBase
{
    private const string Project = "STFP";

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
    public async Task MemberProposal_ProposedOverMcpAndApplied_MakesThePersonARealProjectMember()
    {
        // Arrange：新人在組織裡，還不是專案的成員。
        var person = await RealStackSteps.CreateMemberAsync(_owner.Client, "新同事", RealStackSteps.UniqueEmail("staff-member"));
        var personId = person["personId"]!.GetValue<Guid>();
        await using var job = await StartJobAsync();
        await using var mcp = await ConnectMcpAsync(job.JobId);

        // Act
        var proposed = await CallAsync(mcp, "propose_member_changes", new
        {
            projectKey = Project,
            changes = new[] { new { person = person["email"]!.GetValue<string>(), role = "editor" } },
        });
        var proposalId = JsonNode.Parse(proposed.Text)!["proposalId"]!.GetValue<Guid>();
        var before = await MemberRolesAsync();
        using var apply = await _owner.Client.PostAsync($"/v1/ai/member-proposals/{proposalId}/apply", null);
        var applied = await ApiRequests.AssertSuccessEnvelopeAsync(apply, HttpStatusCode.OK);
        var after = await MemberRolesAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(proposed.IsError, Is.False, proposed.Text);
            Assert.That(before.ContainsKey(personId), Is.False, "提出建議時不會真的加人");
            Assert.That(applied!["status"]!.GetValue<string>(), Is.EqualTo("applied"));
            Assert.That(after.GetValueOrDefault(personId), Is.EqualTo("editor"));
        }
    }

    [Test]
    public async Task NewItemProposalWithAddMembers_AppliedOnce_AddsThePersonFirstAndAssignsTheItemToThem()
    {
        // Arrange
        var person = await RealStackSteps.CreateMemberAsync(_owner.Client, "負責的人", RealStackSteps.UniqueEmail("staff-assignee"));
        var personId = person["personId"]!.GetValue<Guid>();
        await using var job = await StartJobAsync();
        await using var mcp = await ConnectMcpAsync(job.JobId);

        // Act
        var proposed = await CallAsync(mcp, "propose_new_items", new
        {
            projectKey = Project,
            addMembers = new[] { new { person = personId.ToString() } },
            items = new[] { new { type = "task", title = "WordPress 外掛更新", assignee = personId.ToString() } },
        });
        var proposalId = JsonNode.Parse(proposed.Text)!["proposalId"]!.GetValue<Guid>();
        using var apply = await _owner.Client.PostAsync($"/v1/ai/new-item-proposals/{proposalId}/apply", null);
        var applied = await ApiRequests.AssertSuccessEnvelopeAsync(apply, HttpStatusCode.OK);
        var key = applied!["createdKeys"]![0]!.GetValue<string>();
        var item = await RealStackSteps.GetAsync(_owner.Client, $"/v1/items/{key}");
        var members = await MemberRolesAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(proposed.IsError, Is.False, proposed.Text);
            Assert.That(applied["status"]!.GetValue<string>(), Is.EqualTo("applied"));
            Assert.That(members.GetValueOrDefault(personId), Is.EqualTo("contributor"), "沒給角色用 contributor");
            Assert.That(item["assignee"]?["personId"]?.GetValue<Guid>() ?? item["assigneeId"]?.GetValue<Guid>(), Is.EqualTo(personId));
        }
    }

    private async Task<Dictionary<Guid, string>> MemberRolesAsync()
    {
        var project = await RealStackSteps.GetAsync(_owner.Client, $"/v1/projects/{Project}");
        return project["members"]!.AsArray().ToDictionary(m => m!["personId"]!.GetValue<Guid>(), m => m!["role"]!.GetValue<string>());
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
                agentVersion = "0.16.0",
                engines = new[] { new { kind = "claude_code", version = "2.1.330", models = new[] { new { id = "default", displayName = "預設", efforts = Array.Empty<string>() } } } },
                capacity,
            }));
        return await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
    }

    private async Task<RunningJob> StartJobAsync()
    {
        using var created = await _owner.Client.PostAsync($"/v1/projects/{Project}/assistant/conversations", null);
        var conversation = await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created);
        var route = $"/v1/projects/{Project}/assistant/conversations/{conversation!["id"]!.GetValue<Guid>()}/messages";

        // 回覆是 SSE，要等這件工作結束才讀得完：先放著，領到工作、測完、結束它之後才收。
        var send = _owner.Client.PostAsync(route, ApiRequests.Json(new { text = "請把他加進專案", selection = new { connectionId = _engineId, model = "default" } }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!timeout.IsCancellationRequested)
        {
            var data = await TryCheckInAsync(capacity: 1);
            if (data?["job"] is JsonObject claimed)
            {
                return new RunningJob(this, send, claimed["jobId"]!.GetValue<Guid>());
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

    private sealed class RunningJob(StaffingProposalFlowTests owner, Task<HttpResponseMessage> send, Guid jobId) : IAsyncDisposable
    {
        private bool _finished;

        public Guid JobId { get; } = jobId;

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
