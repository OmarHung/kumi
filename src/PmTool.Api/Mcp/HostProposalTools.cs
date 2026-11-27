namespace PmTool.Api.Mcp;

/// <summary>
/// 提出類的工具（變更集、項目的修改建議、新增項目的建議、成員建議、刪除建議、批次修改建議、新專案建議）：在主機代理的同一件工作裡，同樣的參數只能建立一次（結果不明不重跑，worker 網路重送、模型重試都不會多出一份提案）。
/// </summary>
internal static class HostProposalTools
{
    private static readonly HashSet<string> Names = new(StringComparer.Ordinal) { "propose_changeset", "propose_item_changes", "propose_new_items", "propose_member_changes", "propose_item_deletions", "propose_bulk_item_changes", "propose_project" };

    /// <summary>撤回（withdraw_proposal）不算提出、不佔名額，但要知道這件工作的對話，所以主機授權也要找到對話編號。</summary>
    /// <summary>提出類的建議也要記下對話：撤回要用建議自己的對話編號，才撤得掉同一則回覆裡剛提出、對話的工具段落還沒存下的那一份。</summary>
    public static bool NeedsConversation(string toolName) => toolName == "withdraw_proposal" || IsProposal(toolName);

    public static bool IsProposal(string toolName) => Names.Contains(toolName);
}
