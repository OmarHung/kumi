using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Cli.Api;

namespace PmTool.Cli.Agent;

/// <summary>伺服器的 <c>AiProviderKind</c> 在這個指令裡只會碰到兩個值（JSON 是 snake_case）。</summary>
internal static class EngineKinds
{
    public const string ClaudeCode = "claude_code";
    public const string Codex = "codex";

    public static string DisplayName(string kind) => kind switch
    {
        ClaudeCode => "Claude Code",
        Codex => "Codex",
        _ => kind,
    };

    /// <summary>--engine 選項的寫法換成伺服器的種類；不認得的回 null。</summary>
    public static string? FromOption(string option) => option.Trim().ToLowerInvariant() switch
    {
        "claude-code" => ClaudeCode,
        "codex" => Codex,
        _ => null,
    };
}

/// <summary>工作的結束狀態，字串就是伺服器的 <c>AssistantMessageStatus</c>（snake_case）。</summary>
internal static class JobStatus
{
    public const string Complete = "complete";
    public const string Stopped = "stopped";
    public const string Failed = "failed";
}

internal static class ToolStatus
{
    public const string Running = "running";
    public const string Done = "done";
    public const string Failed = "failed";
}

internal sealed record EngineModel(string Id, string DisplayName, IReadOnlyList<string> Efforts, string? DefaultEffort = null)
{
    public const string DefaultId = "default";

    public JsonObject ToJson() => new()
    {
        ["id"] = Id,
        ["displayName"] = DisplayName,
        ["efforts"] = new JsonArray([.. Efforts.Select(effort => (JsonNode?)JsonValue.Create(effort))]),
        // 一定要是 Efforts 之一，不然伺服器會拒絕整份登記。
        ["defaultEffort"] = DefaultEffort is not null && Efforts.Contains(DefaultEffort) ? DefaultEffort : null,
    };
}

/// <summary>伺服器派來的一件工作（<c>check-in</c> 回的 <c>job</c>），欄位意思見 <c>LocalAgentJob</c>。</summary>
internal sealed record AgentJob(
    Guid JobId,
    string Engine,
    string Model,
    string? Effort,
    string ProjectKey,
    string SystemPrompt,
    string Prompt,
    IReadOnlyList<string> AllowedTools,
    string McpUrl,
    int MaxTurns)
{
    /// <summary>要交給引擎的圖片與 PDF（依序）；舊的伺服器沒有這個欄位，當成空的。</summary>
    public IReadOnlyList<AgentAttachment> Attachments { get; init; } = [];

    /// <summary>缺了必要的欄位就回 null：這樣的工作沒辦法跑，也沒辦法回報，只能略過。</summary>
    public static AgentJob? Parse(JsonElement job)
    {
        if (!Guid.TryParse(job.Text("jobId"), out var id)
            || job.Text("engine") is not { } engine
            || job.Text("projectKey") is not { } project
            || job.Text("systemPrompt") is not { } system
            || job.Text("prompt") is not { } prompt
            || job.Text("mcpUrl") is not { } mcpUrl)
        {
            return null;
        }

        var tools = job.List("allowedTools")
            .Where(tool => tool.ValueKind == JsonValueKind.String)
            .Select(tool => tool.GetString()!)
            .ToArray();
        return new AgentJob(
            id, engine, job.Text("model") ?? EngineModel.DefaultId, job.Text("effort"), project, system, prompt, tools, mcpUrl,
            (int)Math.Clamp(job.Number("maxTurns") ?? 0, 0, int.MaxValue))
        {
            Attachments = [.. job.List("attachments").Select(AgentAttachment.Parse)],
        };
    }
}

/// <summary>引擎做的事，轉成伺服器的進度。</summary>
internal abstract record AgentEvent
{
    public abstract JsonObject ToJson();
}

internal sealed record TextEvent(string Delta) : AgentEvent
{
    public override JsonObject ToJson() => new() { ["type"] = "text", ["delta"] = Delta };
}

internal sealed record ToolEvent(string CallId, string Name, string Status, long? ChangeSetNumber = null, string? Error = null, string? ItemProposalId = null) : AgentEvent
{
    public override JsonObject ToJson() => new()
    {
        ["type"] = "tool",
        ["callId"] = CallId,
        ["name"] = Name,
        ["status"] = Status,
        ["changeSetNumber"] = ChangeSetNumber,
        ["error"] = Error,
        ["itemProposalId"] = ItemProposalId,
    };
}

/// <summary>一件工作怎麼結束的。Error 是繁體中文、已經拿掉權杖與本機路徑，可以直接給人看。</summary>
internal sealed record JobOutcome(string Status, string? Error = null, long? InputTokens = null, long? OutputTokens = null)
{
    public static JobOutcome Failed(string error) => new(JobStatus.Failed, error);

    public static JobOutcome Stopped(string? error = null) => new(JobStatus.Stopped, error);

    public JsonObject ToFinishJson() => new()
    {
        ["status"] = Status,
        ["error"] = Error,
        ["usage"] = InputTokens is { } input && OutputTokens is { } output
            ? new JsonObject { ["inputTokens"] = input, ["outputTokens"] = output }
            : null,
    };
}

/// <summary>引擎把進度交到哪裡去（批次回報給伺服器）。</summary>
internal interface IEventSink
{
    void Emit(AgentEvent agentEvent);
}

/// <summary>一種能跑工作的引擎：已經確認過裝了、登入了。</summary>
internal interface IAgentEngine
{
    string Kind { get; }

    string Version { get; }

    IReadOnlyList<EngineModel> Models { get; }

    /// <summary>啟動時順便印出的補充（例如預設模型實際是哪一個）；沒有就是 null。</summary>
    string? Detail => null;

    /// <summary>
    /// 跑完一件工作；工作帶附件時從 <paramref name="attachments"/> 下載。<paramref name="cancellation"/> 取消時中斷引擎並回「中斷」；引擎的失敗都轉成結果回傳，不丟例外。
    /// </summary>
    Task<JobOutcome> RunAsync(AgentJob job, IEventSink sink, CancellationToken cancellation, IAttachmentSource? attachments = null);
}
