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
        [AssistantToolNames.FindItems] = AiRules.MinAgentVersionForFindItems,
        [AssistantToolNames.ListComments] = AiRules.MinAgentVersionForProjectWrites,
        [AssistantToolNames.ListDeletedItems] = AiRules.MinAgentVersionForProjectWrites,
        [AssistantToolNames.GetProjectInfo] = AiRules.MinAgentVersionForProjectWrites,
        [AssistantToolNames.GetPlanning] = AiRules.MinAgentVersionForProjectWrites,
        [AssistantToolNames.ProposePlanningChanges] = AiRules.MinAgentVersionForProjectWrites,
        [AssistantToolNames.ProposeCommentChanges] = AiRules.MinAgentVersionForProjectWrites,
        [AssistantToolNames.ProposeItemRestores] = AiRules.MinAgentVersionForProjectWrites,
        [AssistantToolNames.ProposeProjectChanges] = AiRules.MinAgentVersionForProjectWrites,
        [AssistantToolNames.ListSpaces] = AiRules.MinAgentVersionForEverything,
        [AssistantToolNames.GetPageTree] = AiRules.MinAgentVersionForEverything,
        [AssistantToolNames.GetPage] = AiRules.MinAgentVersionForEverything,
        [AssistantToolNames.ListPageTrash] = AiRules.MinAgentVersionForEverything,
        [AssistantToolNames.ListSavedViews] = AiRules.MinAgentVersionForEverything,
        [AssistantToolNames.ListDescriptionTemplates] = AiRules.MinAgentVersionForEverything,
        [AssistantToolNames.GetOverview] = AiRules.MinAgentVersionForEverything,
        [AssistantToolNames.ListNotifications] = AiRules.MinAgentVersionForEverything,
        [AssistantToolNames.ListChangeSets] = AiRules.MinAgentVersionForEverything,
        [AssistantToolNames.ProposeKnowledgeChanges] = AiRules.MinAgentVersionForEverything,
        [AssistantToolNames.ProposePeopleChanges] = AiRules.MinAgentVersionForEverything,
        [AssistantToolNames.ProposeAttachmentChanges] = AiRules.MinAgentVersionForEverything,
        [AssistantToolNames.ProposeSavedViewChanges] = AiRules.MinAgentVersionForEverything,
        [AssistantToolNames.ProposeDescriptionTemplateChanges] = AiRules.MinAgentVersionForEverything,
    };

    public static bool SupportsNewItems(string? agentVersion) => Supports(AssistantToolNames.ProposeNewItems, agentVersion);

    public static bool SupportsStaffing(string? agentVersion) => Supports(AssistantToolNames.GetProject, agentVersion);

    public static bool SupportsRevision(string? agentVersion) => Supports(AssistantToolNames.WithdrawProposal, agentVersion);

    public static bool SupportsMyWork(string? agentVersion) => Supports(AssistantToolNames.MyWork, agentVersion);

    public static bool SupportsFindItems(string? agentVersion) => Supports(AssistantToolNames.FindItems, agentVersion);

    /// <summary>八個「專案裡的寫入與讀取」工具（<see cref="AiRules.MinAgentVersionForProjectWrites"/>）一起放、一起不放。</summary>
    public static bool SupportsProjectWrites(string? agentVersion) => Supports(AssistantToolNames.ProposeProjectChanges, agentVersion);

    public static IReadOnlyList<string> For(string? projectKey, string? agentVersion)
    {
        var all = projectKey is null ? LocalAgentTools.AllowedForOrganization : LocalAgentTools.Allowed;
        return [.. all.Where(tool => Supports(tool, agentVersion) && (projectKey is not null || SupportsInOrganization(tool, agentVersion)))];
    }

    /// <summary>組織對話的工作放專案的工具（帶 projectKey）要 <see cref="AiRules.MinAgentVersionForEverything"/> 以上：舊版的 kumi 對組織工作只認組織的那幾個工具。</summary>
    public static bool SupportsEverything(string? agentVersion) => AgentVersions.IsAtLeast(agentVersion, AiRules.MinAgentVersionForEverything);

    private static readonly HashSet<string> OrganizationBase = new(StringComparer.Ordinal)
    {
        AssistantToolNames.ListTemplates, AssistantToolNames.GetTemplate, AssistantToolNames.ListProjects, AssistantToolNames.ProposeProject,
        AssistantToolNames.WithdrawProposal, AssistantToolNames.MyWork, AssistantToolNames.FindItems, AssistantToolNames.ListPeople,
    };

    private static bool SupportsInOrganization(string tool, string? agentVersion) =>
        OrganizationBase.Contains(tool) || SupportsEverything(agentVersion);

    private static bool Supports(string tool, string? agentVersion) =>
        !MinimumVersions.TryGetValue(tool, out var minimum) || AgentVersions.IsAtLeast(agentVersion, minimum);
}
