using PmTool.Ai.LocalAgents;

namespace PmTool.Ai.HostAgents;

/// <summary>
/// 同一個程序內叫醒正在等工作的 worker 長輪詢（依代理）：工作排進隊伍、或上一件結束讓出名額時發訊號。
/// 別的 API 程序收進來的工作靠長輪詢的備援查詢。
/// </summary>
internal sealed class HostAgentSignals
{
    private readonly LocalAgentSignals _signals = new();

    public Task Next(Guid agentId) => _signals.Next(agentId);

    public void Signal(Guid agentId) => _signals.Signal(agentId);
}
