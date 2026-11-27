namespace PmTool.Api.Mcp;

/// <summary>
/// 提出類的工具（變更集、項目的修改建議、新增項目的建議、成員建議、刪除建議、批次修改建議、新專案建議、週期與里程碑建議、留言建議、復原項目建議、改專案建議、知識庫、人員、附件、存的檢視與說明格式的動作建議）：在主機代理的同一件工作裡，同樣的參數只能建立一次（結果不明不重跑，worker 網路重送、模型重試都不會多出一份提案）。
/// </summary>
internal static class HostProposalTools
{
    private static readonly HashSet<string> Names = new(StringComparer.Ordinal) { "propose_changeset", "propose_item_changes", "propose_new_items", "propose_member_changes", "propose_item_deletions", "propose_bulk_item_changes", "propose_project", "propose_planning_changes", "propose_comment_changes", "propose_item_restores", "propose_project_changes", "propose_knowledge_changes", "propose_people_changes", "propose_attachment_changes", "propose_saved_view_changes", "propose_description_template_changes" };

    /// <summary>撤回（withdraw_proposal）不算提出、不佔名額，但要知道這件工作的對話，所以主機授權也要找到對話編號；查項目（find_items）要記下對話讀過的專案，也一樣。</summary>
    /// <summary>提出類的建議也要記下對話：撤回要用建議自己的對話編號，才撤得掉同一則回覆裡剛提出、對話的工具段落還沒存下的那一份。</summary>
    public static bool NeedsConversation(string toolName) => toolName is "withdraw_proposal" or "find_items" || IsProposal(toolName);

    public static bool IsProposal(string toolName) => Names.Contains(toolName);
}
