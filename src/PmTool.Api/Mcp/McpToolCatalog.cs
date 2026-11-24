using System.Collections.Frozen;
using PmTool.Api.Mcp.Tools;

namespace PmTool.Api.Mcp;

/// <summary>
/// 所有工具，依範圍分成四組：讀項目、改項目、讀與提出藍圖變更、本機代理用的修改建議。工具的名稱是對外的合約（代理的設定與提示會寫死它），不能改名。
/// </summary>
internal static class McpToolCatalog
{
    public static IReadOnlyList<McpTool> All { get; } =
    [
        .. ItemReadTools.All,
        .. ItemWriteTools.All,
        .. BlueprintReadTools.All,
        .. ChangeSetTools.All,
        .. ItemProposalTools.All,
    ];

    private static readonly FrozenDictionary<string, McpTool> ByName = All.ToFrozenDictionary(tool => tool.Name, StringComparer.Ordinal);

    public static McpTool? Find(string? name) => name is not null && ByName.TryGetValue(name, out var tool) ? tool : null;
}
