using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Npgsql;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 不選專案的組織對話從 HTTP 一路到資料庫：測試扮演 kumi 0.12.0 的本機代理（登記、領組織對話的工作、只帶 X-Kumi-Job 打 /mcp），
/// 用 propose_project 提出新專案，使用者按「建立專案」之後專案真的建立在他名下。只限組織的工作碰不到任何專案的工具，
/// 帶了 X-Kumi-Project 也不行；舊版 kumi 選不了組織對話。
/// </summary>
[TestFixture]
public sealed class ProjectProposalFlowTests : RealStackTestBase
{
    private const string Collection = "/v1/assistant/conversations";

    private SignedInClient _owner = null!;
    private Actor _actor = null!;
    private string _agentSecret = string.Empty;
    private Guid _engineId;
    private HttpClient _agent = null!;

    [OneTimeSetUp]
    public async Task SetUpAsync()
    {
        _owner = await SignInAsOwnerAsync();
        var session = await RealStackSteps.GetAsync(_owner.Client, "/v1/session");
        _actor = new Actor(session["user"]!["organizationId"]!.GetValue<Guid>(), _owner.PersonId, IsOrganizationAdmin: true);
        await RealStackSteps.CreateProjectAsync(_owner.Client, "ORGX", "simple_tasks");
        await AllowPersonalConnectionsAsync();

        using var created = await _owner.Client.PostAsync(
            "/v1/account/tokens",
            ApiRequests.Json(new { name = "agent", scopes = new[] { "agent:run", "items:read", "blueprint:read", "blueprint:write" }, expiresInDays = 30 }));
        _agentSecret = (await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created))!["secret"]!.GetValue<string>();
        _agent = Factory.CreateClient();
        _agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _agentSecret);

        await CheckInAsync(capacity: 0, version: "0.12.0");
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
    public async Task OrganizationJob_ProposeProjectThroughMcp_ThenApply_CreatesTheProjectAsTheApplyingPerson()
    {
        // Arrange
        await using var job = await StartOrganizationJobAsync("幫我開一個網站改版的專案");
        await using var mcp = await ConnectMcpAsync(job.JobId, project: null);

        // Act
        var tools = (await mcp.ListToolsAsync()).Select(tool => tool.Name).ToList();
        var blueprint = await CallAsync(mcp, "get_blueprint", new { projectKey = "ORGX" });
        var projects = await CallAsync(mcp, "list_projects", new { });
        var proposed = await CallAsync(mcp, "propose_project", new { key = "ORGNEW", name = "網站改版", template = "simple_tasks", description = "## 目標\n改版首頁" });
        await job.FinishAsync();
        var proposalId = JsonNode.Parse(proposed.Text)!["proposalId"]!.GetValue<Guid>();
        var pending = await RealStackSteps.GetAsync(_owner.Client, $"/v1/ai/project-proposals/{proposalId}");
        using var apply = await _owner.Client.PostAsync($"/v1/ai/project-proposals/{proposalId}/apply", null);
        var applied = await ApiRequests.AssertSuccessEnvelopeAsync(apply, HttpStatusCode.OK);
        var project = await RealStackSteps.GetAsync(_owner.Client, "/v1/projects/ORGNEW");
        var description = await RealStackSteps.GetAsync(_owner.Client, "/v1/projects/ORGNEW/description");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(job.ClaimedProjectKey, Is.Null, "組織對話的工作沒有專案。");
            Assert.That(tools, Is.EquivalentTo(LocalAgentTools.AllowedForOrganization));
            Assert.That(blueprint.IsError, Is.True, "只限組織的工作碰不到任何專案。");
            Assert.That(projects.Text, Does.Contain("ORGX"));
            Assert.That(proposed.IsError, Is.False, proposed.Text);
            Assert.That(pending["status"]!.GetValue<string>(), Is.EqualTo("pending"));
            Assert.That(pending["templateName"]!.GetValue<string>(), Is.Not.Empty);
            Assert.That(applied!["status"]!.GetValue<string>(), Is.EqualTo("applied"));
            Assert.That(applied["createdProjectKey"]!.GetValue<string>(), Is.EqualTo("ORGNEW"));
            Assert.That(project["project"]!["name"]!.GetValue<string>(), Is.EqualTo("網站改版"));
            Assert.That(description["text"]!.GetValue<string>(), Does.Contain("改版首頁"));
        }
    }

    [Test]
    public async Task OrganizationJob_WithAProjectHeader_IsRefused()
    {
        // Arrange：模型改不了標頭，但被改過的 kumi 或手打的請求帶了專案，也不能把組織工作變成專案工作。
        await using var job = await StartOrganizationJobAsync();
        await using var mcp = await ConnectMcpAsync(job.JobId, project: "ORGX");

        // Act
        var blueprint = await CallAsync(mcp, "get_blueprint", new { projectKey = "ORGX" });

        // Assert
        Assert.That(blueprint.IsError, Is.True, blueprint.Text);
    }

    [Test]
    public async Task OrganizationChat_WithAnAgentOlderThanTheMinimum_Is409AndNothingIsStored()
    {
        // Arrange：舊版 kumi 一定要有專案代號、也不認得新工具，送過去整件工作會失敗。
        await CheckInAsync(capacity: 0, version: "0.11.0");
        var route = await NewConversationAsync();

        // Act
        using var response = await _owner.Client.PostAsync(
            $"{route}/messages", ApiRequests.Json(new { text = "開新專案", selection = new { connectionId = _engineId, model = "default" } }));
        var read = await RealStackSteps.GetAsync(_owner.Client, route);
        await CheckInAsync(capacity: 0, version: "0.12.0");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Conflict, AiErrorCodes.AgentUpdateRequired);
        Assert.That(read["messages"]!.AsArray(), Is.Empty);
    }

    [Test]
    public async Task Proposal_ForANonAdmin_IsRefused_AndAKeyTakenByAnotherAdminLeavesItPending()
    {
        // Arrange：另一位管理員在建議之後先用掉代號。
        using var scope = Factory.Services.CreateScope();
        var creator = scope.ServiceProvider.GetRequiredService<IProjectProposalCreator>();
        var member = _actor with { PersonId = Guid.NewGuid(), IsOrganizationAdmin = false };
        var forbidden = await creator.CreateAsync(member, new ProjectProposalDraft("ORGNA", "成員的", "simple_tasks", false, null), CancellationToken.None);
        var created = await creator.CreateAsync(_actor, new ProjectProposalDraft("ORGRACE", "搶代號", "simple_tasks", false, null), CancellationToken.None);
        using var otherAdmin = await SignInAsNewAdminAsync();
        await RealStackSteps.CreateProjectAsync(otherAdmin.Client, "ORGRACE", "simple_tasks");

        // Act
        using var apply = await _owner.Client.PostAsync($"/v1/ai/project-proposals/{created.Value.Id}/apply", null);
        var read = await RealStackSteps.GetAsync(_owner.Client, $"/v1/ai/project-proposals/{created.Value.Id}");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(forbidden.Failure?.Code, Is.EqualTo(AiErrorCodes.AssistantForbidden));
            await ApiRequests.AssertErrorEnvelopeAsync(apply, HttpStatusCode.Conflict, WorkErrorCodes.ProjectKeyTaken);
            Assert.That(read["status"]!.GetValue<string>(), Is.EqualTo("pending"));
        }
    }

    [Test]
    public async Task Apply_KeyAlreadyTakenBySamePersonAfterTheProposal_IsMarkedApplied()
    {
        // Arrange：建好專案、但標記「已建立」那一步失敗時，再按一次要能收尾；判斷不出是不是這份建議建的，
        // 同一個人在建議之後自己建的同代號專案也一樣當成已建立（卡片連到他自己的專案，沒有壞處）。
        using var scope = Factory.Services.CreateScope();
        var creator = scope.ServiceProvider.GetRequiredService<IProjectProposalCreator>();
        var created = await creator.CreateAsync(_actor, new ProjectProposalDraft("ORGSELF", "自己建的", "simple_tasks", false, null), CancellationToken.None);
        await RealStackSteps.CreateProjectAsync(_owner.Client, "ORGSELF", "simple_tasks");

        // Act
        using var apply = await _owner.Client.PostAsync($"/v1/ai/project-proposals/{created.Value.Id}/apply", null);
        var applied = await ApiRequests.AssertSuccessEnvelopeAsync(apply, HttpStatusCode.OK);

        // Assert
        Assert.That((applied!["status"]!.GetValue<string>(), applied["createdProjectKey"]!.GetValue<string>()), Is.EqualTo(("applied", "ORGSELF")));
    }

    private async Task<SignedInClient> SignInAsNewAdminAsync()
    {
        var email = RealStackSteps.UniqueEmail("org-admin");
        using var response = await _owner.Client.PostAsync(
            "/v1/people", ApiRequests.Json(new { displayName = "Admin Example", email, password = RealStackSteps.MemberPassword, role = "admin" }));
        await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);
        return await SignInAsAsync(email, RealStackSteps.MemberPassword);
    }

    // ---- 扮演本機代理 ----

    private async Task<string> NewConversationAsync()
    {
        using var created = await _owner.Client.PostAsync(Collection, null);
        var conversation = await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created);
        return $"{Collection}/{conversation!["id"]!.GetValue<Guid>()}";
    }

    private async Task CheckInAsync(int capacity, string version)
    {
        var data = await TryCheckInAsync(capacity, version);
        Assert.That(data, Is.Not.Null);
    }

    private async Task<JsonNode?> TryCheckInAsync(int capacity, string version = "0.12.0")
    {
        using var response = await _agent.PostAsync(
            "/v1/agent/check-in",
            ApiRequests.Json(new
            {
                machineKey = "machine-org-0001",
                machineName = "測試電腦",
                agentVersion = version,
                engines = new[] { new { kind = "claude_code", version = "2.1.332", models = new[] { new { id = "default", displayName = "預設", efforts = Array.Empty<string>() } } } },
                capacity,
            }));
        return await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
    }

    private async Task<RunningJob> StartOrganizationJobAsync(string text = "有哪些模板")
    {
        var route = await NewConversationAsync();

        // 回覆是 SSE，要等這件工作結束才讀得完：先放著，領到工作、測完、結束它之後才收。
        var send = _owner.Client.PostAsync($"{route}/messages", ApiRequests.Json(new { text, selection = new { connectionId = _engineId, model = "default" } }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!timeout.IsCancellationRequested)
        {
            var data = await TryCheckInAsync(capacity: 1);
            if (data?["job"] is JsonObject claimed)
            {
                return new RunningJob(this, send, claimed["jobId"]!.GetValue<Guid>(), claimed["projectKey"]?.GetValue<string>());
            }
        }

        throw new TimeoutException("等不到本機代理的工作。");
    }

    private async Task<McpClient> ConnectMcpAsync(Guid jobId, string? project)
    {
        var headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {_agentSecret}", ["X-Kumi-Job"] = jobId.ToString() };
        if (project is not null)
        {
            headers["X-Kumi-Project"] = project;
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
    private sealed class RunningJob(ProjectProposalFlowTests owner, Task<HttpResponseMessage> send, Guid jobId, string? claimedProjectKey) : IAsyncDisposable
    {
        private bool _finished;

        public Guid JobId { get; } = jobId;

        public string? ClaimedProjectKey { get; } = claimedProjectKey;

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
