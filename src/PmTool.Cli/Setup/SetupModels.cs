namespace PmTool.Cli.Setup;

internal enum SetupStatus
{
    Done,
    Skipped,
    Failed,
}

/// <summary>總結裡的一行：<paramref name="Name"/> 是項目，<paramref name="Detail"/> 是結果或怎麼補。</summary>
internal sealed record SetupResult(string Name, SetupStatus Status, string Detail);

/// <summary>這台電腦上找到的引擎。<paramref name="Kind"/> 是 <see cref="Agent.EngineKinds"/>。</summary>
internal sealed record DetectedEngine(string Kind, string DisplayName, string Command, string Executable, string Version);

/// <summary>引擎裡已經有的 kumi MCP 伺服器：沒有的話 <paramref name="Url"/> 是 null。</summary>
internal sealed record ExistingMcp(bool Exists, string? Url)
{
    /// <summary>
    /// 已經授權過（Codex 的 <c>auth_status</c> 是 o_auth 或 bearer_token），不必再跑 <c>mcp login</c>。
    /// Claude Code 的 <c>mcp get</c> 沒有穩定的授權欄位，一律是 false（照樣登入，已經登入過的只是再按一次允許）。
    /// </summary>
    public bool IsAuthorized { get; init; }
}

/// <summary>要對引擎的 MCP 設定做什麼。</summary>
internal enum McpAction
{
    /// <summary>還沒有，加進去。</summary>
    Add,

    /// <summary>已經指到這個伺服器，不用加（還是要登入）。</summary>
    KeepAndLogin,

    /// <summary>指到別處、使用者同意換成這個伺服器：先移除再加。</summary>
    Replace,
}
