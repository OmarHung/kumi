namespace PmTool.Ai.HostAgents;

/// <summary><c>ai.host_jobs.payload</c> 的形狀：派給 worker 的工作內容，不含工具授權、租約與 MCP 網址（領走時才有）。</summary>
internal sealed record StoredHostJob(
    AiProviderKind Engine,
    string Model,
    AiEffort? Effort,
    string SystemPrompt,
    string Prompt,
    IReadOnlyList<string> AllowedTools,
    int MaxTurns)
{
    /// <summary>要交給引擎下載的圖片與 PDF（<see cref="HostJob.Attachments"/>）；舊的工作列沒有這個欄位，讀出來是空的。</summary>
    public IReadOnlyList<LocalAgentJobAttachment> Attachments { get; init; } = [];
}
