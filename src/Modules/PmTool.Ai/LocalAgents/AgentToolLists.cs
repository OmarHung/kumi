using PmTool.Ai.Assistant;

namespace PmTool.Ai.LocalAgents;

/// <summary>
/// 一件本機、主機工作能用的工具清單。kumi 看到不認得的工具會整件工作不跑，所以新的工具只放給夠新的版本；
/// 舊版照樣能用，只是少了新工具。**之後新增給代理的工具，在 <see cref="MinimumVersions"/> 加一行**（沒列的是一開始就有的）。
/// </summary>
internal static class AgentToolLists
{
    private static readonly IReadOnlyDictionary<string, string> MinimumVersions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [AssistantToolNames.ProposeNewItems] = AiRules.MinAgentVersionForNewItemProposals,
        [AssistantToolNames.GetProject] = AiRules.MinAgentVersionForStaffingTools,
        [AssistantToolNames.ListPeople] = AiRules.MinAgentVersionForStaffingTools,
        [AssistantToolNames.ProposeMemberChanges] = AiRules.MinAgentVersionForStaffingTools,
        [AssistantToolNames.ProposeItemDeletions] = AiRules.MinAgentVersionForRevisionTools,
        [AssistantToolNames.ProposeBulkItemChanges] = AiRules.MinAgentVersionForRevisionTools,
        [AssistantToolNames.WithdrawProposal] = AiRules.MinAgentVersionForRevisionTools,
        [AssistantToolNames.MyWork] = AiRules.MinAgentVersionForMyWork,
    };

    public static bool SupportsNewItems(string? agentVersion) => Supports(AssistantToolNames.ProposeNewItems, agentVersion);

    public static bool SupportsStaffing(string? agentVersion) => Supports(AssistantToolNames.GetProject, agentVersion);

    public static bool SupportsRevision(string? agentVersion) => Supports(AssistantToolNames.WithdrawProposal, agentVersion);

    public static bool SupportsMyWork(string? agentVersion) => Supports(AssistantToolNames.MyWork, agentVersion);

    public static IReadOnlyList<string> For(string? projectKey, string? agentVersion)
    {
        var all = projectKey is null ? LocalAgentTools.AllowedForOrganization : LocalAgentTools.Allowed;
        return [.. all.Where(tool => Supports(tool, agentVersion))];
    }

    private static bool Supports(string tool, string? agentVersion) =>
        !MinimumVersions.TryGetValue(tool, out var minimum) || AgentVersions.IsAtLeast(agentVersion, minimum);
}
