using System.Collections.Frozen;
using PmTool.Ai.Assistant;

namespace PmTool.Ai.Proposals;

/// <summary>
/// 把建議編號放在通用的 <see cref="AssistantToolPart.ProposalId"/> 的工具，與它建立的建議種類。工具段落、本機代理的回報、帳本都照這一份認種類：
/// 名稱不在這裡的工具帶了 ProposalId 一律當作沒有。之後新增的建議工具加在這裡，不再在工具段落加欄位。
/// </summary>
internal static class ProposalTools
{
    private static readonly FrozenDictionary<string, ProposalKind> GenericKinds = new Dictionary<string, ProposalKind>(StringComparer.Ordinal)
    {
        [AssistantToolNames.ProposePlanningChanges] = ProposalKind.Planning,
        [AssistantToolNames.ProposeCommentChanges] = ProposalKind.Comments,
        [AssistantToolNames.ProposeItemRestores] = ProposalKind.ItemRestores,
        [AssistantToolNames.ProposeProjectChanges] = ProposalKind.ProjectChanges,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>用通用編號的工具建立的種類；不是這種工具是 null。</summary>
    public static ProposalKind? GenericKindOf(string? toolName) =>
        toolName is not null && GenericKinds.TryGetValue(toolName, out var kind) ? kind : null;

    public static bool UsesGenericId(string? toolName) => GenericKindOf(toolName) is not null;
}
