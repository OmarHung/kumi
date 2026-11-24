using PmTool.Ai.Persistence;

namespace PmTool.Ai.HostAgents;

/// <summary>主機代理的列舉在資料表裡的固定文字。</summary>
internal static class HostEnums
{
    public static string NameOf(HostBilling billing) => billing switch
    {
        HostBilling.Api => "api",
        HostBilling.Subscription => "subscription",
        _ => "unknown",
    };

    public static HostBilling ParseBilling(string? name) => name switch
    {
        "api" => HostBilling.Api,
        "subscription" => HostBilling.Subscription,
        _ => HostBilling.Unknown,
    };

    public static string NameOf(HostAgentState state) => state switch
    {
        HostAgentState.AwaitingPairing => HostAgentStates.AwaitingPairing,
        HostAgentState.Active => HostAgentStates.Active,
        _ => HostAgentStates.Disabled,
    };

    public static HostAgentState ParseState(string name) => name switch
    {
        HostAgentStates.AwaitingPairing => HostAgentState.AwaitingPairing,
        HostAgentStates.Active => HostAgentState.Active,
        _ => HostAgentState.Disabled,
    };

    public static HostLoginState ParseLoginState(string name) => name switch
    {
        HostLoginStates.Pending => HostLoginState.Pending,
        HostLoginStates.Claimed => HostLoginState.Claimed,
        HostLoginStates.Completed => HostLoginState.Completed,
        HostLoginStates.Failed => HostLoginState.Failed,
        _ => HostLoginState.Cancelled,
    };

    public static bool IsEngine(AiProviderKind kind) => kind is AiProviderKind.ClaudeCode or AiProviderKind.Codex;

    public static string EngineLabel(AiProviderKind kind) => kind == AiProviderKind.ClaudeCode ? "Claude Code" : "Codex";
}
