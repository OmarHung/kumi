namespace PmTool.Api.Mcp;

/// <summary>
/// 一次 MCP 請求碰得到的資料範圍的種類。順序有意義：<c>default</c>（值 0）是最嚴的「只限組織」，
/// 沒初始化的 <see cref="McpScope"/> 出錯時封閉，不會變成不限專案。
/// </summary>
internal enum McpScopeKind
{
    /// <summary>不選專案的組織對話：不碰任何專案或項目的資料（「沒有專案」不等於「全部專案」）。</summary>
    OrganizationOnly = 0,

    /// <summary>本機代理與主機代理替專案的對話做事：只准碰那個專案。</summary>
    Project,

    /// <summary>一般的權杖（OAuth、個人存取權杖）：授權的人看得到的全部專案。</summary>
    Unrestricted,
}

/// <summary>
/// 一次請求的範圍（交接 <c>internal/HANDOFF-org-assistant.md</c> 第 6 節）。三種狀態明確分開，不用「null 是不限」：
/// 組織對話沒有專案，用 null 表示就會被當成不限而碰到所有專案。
/// </summary>
internal readonly record struct McpScope
{
    private McpScope(McpScopeKind kind, string? projectKey, bool tracksProjects = false)
    {
        Kind = kind;
        ProjectKey = projectKey;
        TracksProjects = tracksProjects;
    }

    /// <summary>
    /// 只有「替組織對話做事的代理」（本機代理、主機授權）是 true：專案的工具可以用，但每次碰的專案（參數的專案代號、項目編號的前綴）
    /// 都由伺服器記下、工具跑完後記進對話讀過的專案（<c>IAssistantConversationReads</c>）；看不到的專案仍由模組照授權的人的權限擋下。
    /// 預設值（沒初始化）是 false：出錯時封閉。
    /// </summary>
    public bool TracksProjects { get; }

    public McpScopeKind Kind { get; }

    /// <summary>只有 <see cref="McpScopeKind.Project"/> 有值（大寫的代號）。</summary>
    public string? ProjectKey { get; }

    public static McpScope Unrestricted { get; } = new(McpScopeKind.Unrestricted, null);

    public static McpScope OrganizationOnly { get; } = new(McpScopeKind.OrganizationOnly, null);

    /// <summary>
    /// 組織對話裡的代理：種類仍是 <see cref="McpScopeKind.OrganizationOnly"/>（不選專案、不能搜整個組織的項目、能提新專案），
    /// 但專案的工具可以用，要帶 projectKey（或項目編號），碰到的專案會被記下（<see cref="TracksProjects"/>）。
    /// </summary>
    public static McpScope OrganizationWithProjects { get; } = new(McpScopeKind.OrganizationOnly, null, tracksProjects: true);

    public static McpScope ForProject(string projectKey) =>
        string.IsNullOrWhiteSpace(projectKey)
            ? throw new ArgumentException("限定專案的範圍要有專案代號。", nameof(projectKey))
            : new McpScope(McpScopeKind.Project, projectKey.Trim().ToUpperInvariant());

    /// <summary>主機授權的專案代號：有值是限定專案，null 是不選專案的組織對話。</summary>
    public static McpScope ForJob(string? projectKey) => projectKey is null ? OrganizationWithProjects : ForProject(projectKey);

    /// <summary>
    /// 範圍是不是受限的（限定專案或只限組織）：受限的請求只能用代理的工具清單，而且清單依範圍不同
    /// （<see cref="AllowsTool"/>）。
    /// </summary>
    public bool IsRestricted => Kind != McpScopeKind.Unrestricted;

    /// <summary>這個範圍下，工具名稱在不在代理能用的清單裡（不受限時不設限）。</summary>
    public bool AllowsTool(string toolName) => Kind switch
    {
        McpScopeKind.Unrestricted => true,
        McpScopeKind.Project => PmTool.Ai.LocalAgentTools.IsAllowed(toolName),
        _ => PmTool.Ai.LocalAgentTools.IsAllowedForOrganization(toolName),
    };

    /// <summary>專案代號（和限定的那個比，不分大小寫）能不能碰：只限組織一律不行，除非是組織對話裡的代理（<see cref="TracksProjects"/>，碰到的專案要記下）。</summary>
    public bool AllowsProject(string projectKey) => Kind switch
    {
        McpScopeKind.Unrestricted => true,
        McpScopeKind.Project => string.Equals(projectKey.Trim(), ProjectKey, StringComparison.OrdinalIgnoreCase),
        _ => TracksProjects && !string.IsNullOrWhiteSpace(projectKey),
    };
}
