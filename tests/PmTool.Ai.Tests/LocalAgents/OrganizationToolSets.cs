namespace PmTool.Ai.Tests.LocalAgents;

/// <summary>組織對話一開始就有的八個工具（舊版的 kumi 只認這些）；第二段（0.21.0）起組織對話再加專案的工具與十四個共用的工具。</summary>
internal static class OrganizationToolSets
{
    public static readonly IReadOnlySet<string> Base = new HashSet<string>(StringComparer.Ordinal)
    {
        "list_templates", "get_template", "list_projects", "propose_project", "withdraw_proposal", "my_work", "find_items", "list_people",
    };

    /// <summary>舊版 kumi 的組織工作：組織清單裡屬於那八個、而且不在 <paramref name="notYet"/> 裡的（照組織清單的順序）。</summary>
    public static IEnumerable<string> OldKumi(params string[] notYet) =>
        LocalAgentTools.AllowedForOrganization.Where(tool => Base.Contains(tool) && !notYet.Contains(tool));
}
