namespace PmTool.Ai.Persistence;

/// <summary>
/// 列舉在資料表與 JSON 裡的固定文字（snake_case）。存文字不存數字：列舉改了順序或名字，舊資料的意思也不會跟著變。
/// API 的 JSON 用同樣的寫法（JsonStringEnumConverter 的 SnakeCaseLower），兩邊一致。
/// </summary>
internal static class AiEnumNames
{
    public static string NameOf(AiProviderKind kind) => kind switch
    {
        AiProviderKind.Anthropic => "anthropic",
        AiProviderKind.Openai => "openai",
        AiProviderKind.Google => "google",
        AiProviderKind.OpenaiCompatible => "openai_compatible",
        AiProviderKind.Openrouter => "openrouter",
        AiProviderKind.ClaudeCode => "claude_code",
        AiProviderKind.Codex => "codex",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "不認得的供應商。"),
    };

    public static AiProviderKind ParseKind(string name) => name switch
    {
        "anthropic" => AiProviderKind.Anthropic,
        "openai" => AiProviderKind.Openai,
        "google" => AiProviderKind.Google,
        "openai_compatible" => AiProviderKind.OpenaiCompatible,
        "openrouter" => AiProviderKind.Openrouter,
        "claude_code" => AiProviderKind.ClaudeCode,
        "codex" => AiProviderKind.Codex,
        _ => throw new FormatException($"不認得的供應商：{name}"),
    };

    public static string NameOf(AiEffort effort) => effort switch
    {
        AiEffort.None => "none",
        AiEffort.Low => "low",
        AiEffort.Medium => "medium",
        AiEffort.High => "high",
        AiEffort.Xhigh => "xhigh",
        AiEffort.Max => "max",
        _ => throw new ArgumentOutOfRangeException(nameof(effort), effort, "不認得的思考深度。"),
    };

    public static AiEffort ParseEffort(string name) => name switch
    {
        "none" => AiEffort.None,
        "low" => AiEffort.Low,
        "medium" => AiEffort.Medium,
        "high" => AiEffort.High,
        "xhigh" => AiEffort.Xhigh,
        "max" => AiEffort.Max,
        _ => throw new FormatException($"不認得的思考深度：{name}"),
    };

    public static string? NameOf(AiEffort? effort) => effort is { } value ? NameOf(value) : null;

    public static AiEffort? ParseOptionalEffort(string? name) => name is null ? null : ParseEffort(name);
}

internal static class AiProviderKinds
{
    /// <summary>使用者自己電腦上的引擎（Claude Code、Codex）：只出現在能選的清單，不能存成連線。</summary>
    public static bool IsLocalEngine(this AiProviderKind kind) => kind is AiProviderKind.ClaudeCode or AiProviderKind.Codex;
}
