using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 主機代理端到端測試的共用步驟：用真的模組、真的 PostgreSQL，由測試用 HTTP 假裝 worker
/// （配對、登記、開始登入與完成、領工作、回報、結束、用工作的工具授權打 /mcp）。不碰真的 Claude Code 或 Codex。
/// </summary>
public abstract class HostAgentFlowBase : RealStackTestBase
{
    protected const string Project = "HOSTA";

    protected const string ServerUrl = "http://localhost";

    protected const string Root = "/v1/ai/host";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    internal SignedInClient Owner { get; private set; } = null!;

    internal SignedInClient Admin { get; private set; } = null!;

    internal SignedInClient Member { get; private set; } = null!;

    /// <summary>專案的「執行者」：只看得到自己負責或建立的項目。</summary>
    internal SignedInClient Contributor { get; private set; } = null!;

    internal SignedInClient Guest { get; private set; } = null!;

    protected Guid AgentId { get; private set; }

    protected Guid SourceId { get; private set; }

    private readonly List<IDisposable> _owned = [];
    private HttpClient? _worker;

    protected HttpClient Worker => _worker ?? throw new InvalidOperationException("還沒配對。");

    protected string Credential { get; private set; } = string.Empty;

    protected Guid LoginId { get; private set; }

    /// <summary>建立專案、各種角色的人、配對一台代理、登入 Claude Code、開一個來源。要在 OneTimeSetUp 裡呼叫。</summary>
    internal async Task SetUpHostAsync()
    {
        Owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(Owner.Client, Project, "simple_tasks");
        Admin = await AddPersonAsync("admin", "Admin Example", projectRole: "editor");
        Member = await AddMemberAsync(Owner, Project, "editor", "Member Example", RealStackSteps.UniqueEmail("host-member"));
        Contributor = await AddMemberAsync(Owner, Project, "contributor", "Contributor Example", RealStackSteps.UniqueEmail("host-contributor"));
        Guest = await AddPersonAsync("guest", "Guest Example", projectRole: "viewer");

        var pairing = await PostOwnerAsync($"{Root}/agents", new { name = "主機一號" }, HttpStatusCode.Created);
        AgentId = pairing["agent"]!["id"]!.GetValue<Guid>();
        await PairWorkerAsync(pairing["pairingCode"]!.GetValue<string>());
        await LoginEngineAsync("claude_code");
        var source = await PostOwnerAsync($"{Root}/sources", SourceBody(), HttpStatusCode.Created);
        SourceId = source["id"]!.GetValue<Guid>();
    }

    [OneTimeTearDown]
    public void TearDownHost()
    {
        foreach (var client in new[] { Owner, Admin, Member, Contributor, Guest })
        {
            client?.Dispose();
        }

        foreach (var owned in _owned)
        {
            owned.Dispose();
        }
    }

    protected object SourceBody(bool enabled = true, int? dailyPerPerson = null) => new
    {
        agentId = AgentId,
        engine = "claude_code",
        name = "主機上的 Claude Code",
        enabled,
        enabledModels = new[] { "default" },
        defaultModel = "default",
        defaultEffort = (string?)null,
        limits = new { dailyRequestsPerPerson = dailyPerPerson, monthlyRequests = (int?)null, monthlyTokens = (long?)null },
        acknowledgeApiBilling = true,
    };

    internal async Task<SignedInClient> AddPersonAsync(string role, string name, string projectRole)
    {
        var email = RealStackSteps.UniqueEmail($"host-{role}");
        var person = await RealStackSteps.CreateMemberAsync(Owner.Client, name, email);
        var personId = person["personId"]!.GetValue<Guid>();
        using var changed = await Owner.Client.PutAsync($"/v1/people/{personId}/role", ApiRequests.Json(new { role }));
        await ApiRequests.AssertSuccessEnvelopeAsync(changed, HttpStatusCode.OK);
        await RealStackSteps.SetMemberAsync(Owner.Client, Project, personId, projectRole);
        return await SignInAsAsync(email, RealStackSteps.MemberPassword);
    }

    protected async Task RemoveAgentAsync(Guid agentId)
    {
        using var response = await Owner.Client.DeleteAsync($"{Root}/agents/{agentId}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    protected async Task<JsonNode> PostOwnerAsync(string route, object? body, HttpStatusCode expected)
    {
        using var response = await Owner.Client.PostAsync(route, body is null ? null : ApiRequests.Json(body));
        return await ApiRequests.AssertSuccessEnvelopeAsync(response, expected) ?? throw new AssertionException("沒有 data");
    }

    // ---- worker ----

    protected async Task PairWorkerAsync(string code)
    {
        using var anonymous = Factory.CreateClient();
        using var response = await anonymous.PostAsync(
            "/v1/host-agent/pair",
            ApiRequests.Json(new { code, machineKey = "machine-0001", machineName = "host-01", agentVersion = "0.9.0" }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
        Credential = data!["credential"]!.GetValue<string>();
        var worker = Factory.CreateClient();
        worker.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Credential);
        worker.DefaultRequestHeaders.TryAddWithoutValidation("X-Kumi-Machine", "machine-0001");
        worker.Timeout = TimeSpan.FromSeconds(60);
        _owned.Add(worker);
        _worker = worker;
    }

    /// <summary>擁有者開始登入 → worker 認領 → CLI 登入完成（假的）→ 帶著登入編號登記，引擎就能接工作。</summary>
    protected async Task LoginEngineAsync(string engine, string billing = "api")
    {
        var start = await PostOwnerAsync($"{Root}/agents/{AgentId}/engines/{engine}/login", null, HttpStatusCode.OK);
        var code = start["code"]!.GetValue<string>();
        var claim = await WorkerPostAsync("/v1/host-agent/logins/claim", new { code }, HttpStatusCode.OK);
        LoginId = claim["loginId"]!.GetValue<Guid>();
        var completed = await WorkerPostAsync($"/v1/host-agent/logins/{LoginId}/complete", new { loggedIn = true, billing }, HttpStatusCode.OK);
        Assert.That(completed["engine"]!.GetValue<string>(), Is.EqualTo(engine));
        await CheckInAsync(capacity: 0);
    }

    protected object CheckInBody(int capacity, Guid? loginId = null, string billing = "api", bool loggedIn = true) => new
    {
        agentVersion = "0.9.0",
        serverUrl = ServerUrl,
        machineKey = "machine-0001",
        machineName = "host-01",
        capacity,
        engines = new object[]
        {
            new
            {
                kind = "claude_code",
                installed = true,
                version = "2.1.330",
                supported = true,
                loggedIn,
                billing,
                loginId = loginId ?? (LoginId == Guid.Empty ? (Guid?)null : LoginId),
                models = new[] { new { id = "default", displayName = "預設", efforts = new[] { "low", "high" }, defaultEffort = (string?)"high" } },
                problem = (string?)null,
            },
            new { kind = "codex", installed = false, supported = false, loggedIn = false, billing = "unknown", models = Array.Empty<object>() },
        },
    };

    protected async Task<JsonNode> CheckInAsync(int capacity, Guid? loginId = null, string billing = "api", bool loggedIn = true) =>
        await WorkerPostAsync("/v1/host-agent/check-in", CheckInBody(capacity, loginId, billing, loggedIn), HttpStatusCode.OK);

    /// <summary>反覆登記（長輪詢）直到領到工作；等太久就失敗。</summary>
    protected async Task<JsonNode> NextJobAsync()
    {
        var deadline = DateTimeOffset.UtcNow + Patience;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var result = await CheckInAsync(capacity: 1);
            if (result["job"] is JsonObject job)
            {
                return job;
            }
        }

        throw new AssertionException("等不到工作。");
    }

    protected async Task<JsonNode> WorkerPostAsync(string route, object body, HttpStatusCode expected)
    {
        using var response = await Worker.PostAsync(route, ApiRequests.Json(body));
        return await ApiRequests.AssertSuccessEnvelopeAsync(response, expected) ?? throw new AssertionException("沒有 data");
    }

    protected async Task<HttpResponseMessage> WorkerRawAsync(string route, object body) =>
        await Worker.PostAsync(route, ApiRequests.Json(body));

    protected Task<JsonNode> ReportTextAsync(JsonNode job, string text, int firstSeq = 1) =>
        WorkerPostAsync(
            $"/v1/host-agent/jobs/{job["jobId"]!.GetValue<Guid>()}/events",
            new { lease = job["lease"]!.GetValue<int>(), firstSeq, events = new object[] { new { type = "text", delta = text } } },
            HttpStatusCode.OK);

    protected async Task FinishAsync(JsonNode job, string status = "complete")
    {
        using var response = await WorkerRawAsync(
            $"/v1/host-agent/jobs/{job["jobId"]!.GetValue<Guid>()}/finish",
            new { lease = job["lease"]!.GetValue<int>(), status, error = (string?)null, usage = new { inputTokens = 10, outputTokens = 5 } });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    // ---- 成員送出 ----

    internal async Task<string> NewConversationRouteAsync(SignedInClient person)
    {
        using var created = await person.Client.PostAsync($"/v1/projects/{Project}/assistant/conversations", null);
        var conversation = await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created);
        return $"/v1/projects/{Project}/assistant/conversations/{conversation!["id"]!.GetValue<Guid>()}";
    }

    /// <summary>送一則訊息，回傳回應（已經讀到標頭；成功是事件串流，失敗是一般的 JSON 錯誤）。</summary>
    internal async Task<HttpResponseMessage> SendAsync(
        SignedInClient person, string text = "請看一下專案", string? itemKey = null, string? conversationRoute = null)
    {
        var route = conversationRoute ?? await NewConversationRouteAsync(person);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{route}/messages")
        {
            Content = ApiRequests.Json(new { text, selection = new { connectionId = SourceId, model = "default" }, itemKey }),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return await person.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }

    /// <summary>用工作的工具授權連到 /mcp。用完要 DisposeAsync。</summary>
    protected async Task<McpClient> ConnectToolsAsync(string toolToken)
    {
        var http = Factory.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(http.BaseAddress!, "/mcp"),
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {toolToken}" },
            },
            http,
            loggerFactory: null,
            ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    internal static async Task<IReadOnlyList<AssistantStreamTests.SseFrame>> ReadAllAsync(HttpResponseMessage response) =>
        await new AssistantStreamTests.SseReader(response).ReadToEndAsync();
}
