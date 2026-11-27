using System.Collections.Frozen;
using PmTool.Api.Mcp.Tools;

namespace PmTool.Api.Mcp;

/// <summary>
/// 所有工具，依範圍分成六組：讀項目、改項目、讀與提出藍圖變更、引擎用的項目修改建議、派工（專案成員與組織的人）與新專案建議。工具的名稱是對外的合約（代理的設定與提示會寫死它），不能改名。
/// </summary>
internal static class McpToolCatalog
{
    public static IReadOnlyList<McpTool> All { get; } =
    [
        .. ItemReadTools.All,
        .. FindItemsTools.All,
        .. ItemWriteTools.All,
        .. BlueprintReadTools.All,
        .. ChangeSetTools.All,
        .. ItemProposalTools.All,
        .. StaffingTools.All,
        .. ItemRevisionTools.All,
        .. ProjectProposalTools.All,
        .. KnowledgeTools.All,
        .. PeopleAttachmentTools.All,
        .. WorkspaceTools.All,
        .. ProjectDetailReadTools.All,
        .. ProjectWriteProposalTools.All,
    ];

    private static readonly FrozenDictionary<string, McpTool> ByName = All.ToFrozenDictionary(tool => tool.Name, StringComparer.Ordinal);

    public static McpTool? Find(string? name) => name is not null && ByName.TryGetValue(name, out var tool) ? tool : null;
}
