namespace PmTool.Api.Mcp;

/// <summary>
/// 提出類的工具：在主機代理的同一件工作裡，同樣的參數只能建立一次（結果不明不重跑，worker 網路重送、模型重試都不會多出一份提案）。
/// </summary>
internal static class HostProposalTools
{
    private static readonly HashSet<string> Names = new(StringComparer.Ordinal) { "propose_changeset", "propose_item_changes" };

    public static bool IsProposal(string toolName) => Names.Contains(toolName);
}
