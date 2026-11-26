using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Cli.Agent;

namespace PmTool.Cli.Tests.Agent;

internal static class TestJobs
{
    public static AgentJob Make(string engine = EngineKinds.ClaudeCode, string model = "default", string? effort = null, Guid? id = null) => new(
        id ?? Guid.NewGuid(), engine, model, effort, "DEMO", "系統提示：只讀藍圖。", "使用者：列出狀態", ["get_blueprint", "propose_changeset"],
        "https://pm.example.com/mcp", 9);

    public static JsonObject ToWire(AgentJob job) => new()
    {
        ["jobId"] = job.JobId,
        ["engine"] = job.Engine,
        ["model"] = job.Model,
        ["effort"] = job.Effort,
        ["projectKey"] = job.ProjectKey,
        ["systemPrompt"] = job.SystemPrompt,
        ["prompt"] = job.Prompt,
        ["allowedTools"] = new JsonArray([.. job.AllowedTools.Select(tool => (JsonNode?)tool)]),
        ["mcpUrl"] = job.McpUrl,
        ["maxTurns"] = job.MaxTurns,
    };
}

/// <summary>測試用的引擎：照給的函式跑，不碰真的程式。</summary>
internal sealed class FakeEngine(Func<AgentJob, IEventSink, CancellationToken, Task<JobOutcome>> run, string kind = EngineKinds.ClaudeCode) : IAgentEngine
{
    public string Kind => kind;

    public string Version => "9.9.9";

    public IReadOnlyList<EngineModel> Models { get; } = [new(EngineModel.DefaultId, "預設", []), new("sonnet", "Sonnet", ["low", "high"])];

    public Task<JobOutcome> RunAsync(AgentJob job, IEventSink sink, CancellationToken cancellation, IAttachmentSource? attachments = null) => run(job, sink, cancellation);
}

internal sealed class ListSink : IEventSink
{
    private readonly List<AgentEvent> _events = [];

    public IReadOnlyList<AgentEvent> Events
    {
        get
        {
            lock (_events)
            {
                return [.. _events];
            }
        }
    }

    public void Emit(AgentEvent agentEvent)
    {
        lock (_events)
        {
            _events.Add(agentEvent);
        }
    }
}

internal sealed record AgentRequest(string Path, JsonNode? Body);

/// <summary>
/// 假的伺服器（本機代理那一頭）：記下每個請求（執行緒安全：兩件工作同時回報），回應交給測試寫的函式。
/// 沒設定的路徑回 404，和真的伺服器不認得工作時一樣。
/// </summary>
internal sealed class FakeAgentServer : HttpMessageHandler
{
    private readonly List<AgentRequest> _requests = [];
    private int _checkIns;

    public Func<JsonNode, int, CancellationToken, Task<HttpResponseMessage>> CheckIn { get; set; } =
        (_, _, _) => Task.FromResult(Ok(new { job = (object?)null }));

    public Func<Guid, JsonNode, HttpResponseMessage> Events { get; set; } = (_, _) => Ok(new { cancelled = false });

    public Func<Guid, JsonNode, HttpResponseMessage> Finish { get; set; } = (_, _) => new HttpResponseMessage(HttpStatusCode.NoContent);

    public IReadOnlyList<AgentRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    public IReadOnlyList<JsonNode> CheckInBodies => [.. Requests.Where(request => request.Path == "/v1/agent/check-in").Select(request => request.Body!)];

    public IReadOnlyList<JsonNode> EventBodies(Guid jobId) =>
        [.. Requests.Where(request => request.Path == $"/v1/agent/jobs/{jobId}/events").Select(request => request.Body!)];

    public IReadOnlyList<JsonNode> FinishBodies(Guid jobId) =>
        [.. Requests.Where(request => request.Path == $"/v1/agent/jobs/{jobId}/finish").Select(request => request.Body!)];

    public static HttpResponseMessage Ok(object? data) => FakeServer.Envelope(HttpStatusCode.OK, data);

    public static HttpResponseMessage Failure(HttpStatusCode status, string code, string message) => new(status)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { success = false, data = (object?)null, error = new { code, message, details = Array.Empty<object>() } }),
            Encoding.UTF8, "application/json"),
    };

    public static HttpResponseMessage JobResponse(AgentJob job) => Ok(new { job = TestJobs.ToWire(job) });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var text = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var body = text.Length == 0 ? null : JsonNode.Parse(text);
        var path = request.RequestUri!.AbsolutePath;
        lock (_requests)
        {
            _requests.Add(new AgentRequest(path, body));
        }

        if (path == "/v1/agent/check-in")
        {
            return await CheckIn(body!, Interlocked.Increment(ref _checkIns), cancellationToken);
        }

        var parts = path.Split('/');
        if (parts is ["", "v1", "agent", "jobs", var id, var action] && Guid.TryParse(id, out var jobId))
        {
            return action == "events" ? Events(jobId, body!) : Finish(jobId, body!);
        }

        return Failure(HttpStatusCode.NotFound, "not_found", "沒有這個端點");
    }
}
