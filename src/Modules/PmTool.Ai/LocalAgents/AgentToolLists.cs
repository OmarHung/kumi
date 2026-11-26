using PmTool.Ai.Assistant;

namespace PmTool.Ai.LocalAgents;

/// <summary>
/// 一件本機、主機工作能用的工具清單。kumi 看到不認得的工具會整件工作不跑，所以新的工具只放給夠新的版本
/// （<see cref="AiRules.MinAgentVersionForNewItemProposals"/>）；舊版照樣能用，只是少了新工具。
/// </summary>
internal static class AgentToolLists
{
    private static readonly IReadOnlyList<string> WithoutNewItems =
        [.. LocalAgentTools.Allowed.Where(tool => tool != AssistantToolNames.ProposeNewItems)];

    public static bool SupportsNewItems(string? agentVersion) =>
        AgentVersions.IsAtLeast(agentVersion, AiRules.MinAgentVersionForNewItemProposals);

    public static IReadOnlyList<string> For(string? projectKey, string? agentVersion) =>
        projectKey is null ? LocalAgentTools.AllowedForOrganization : SupportsNewItems(agentVersion) ? LocalAgentTools.Allowed : WithoutNewItems;
}
