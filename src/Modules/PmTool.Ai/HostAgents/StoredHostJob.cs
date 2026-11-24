namespace PmTool.Ai.HostAgents;

/// <summary><c>ai.host_jobs.payload</c> 的形狀：派給 worker 的工作內容，不含工具授權、租約與 MCP 網址（領走時才有）。</summary>
internal sealed record StoredHostJob(
    AiProviderKind Engine,
    string Model,
    AiEffort? Effort,
    string SystemPrompt,
    string Prompt,
    IReadOnlyList<string> AllowedTools,
    int MaxTurns);
