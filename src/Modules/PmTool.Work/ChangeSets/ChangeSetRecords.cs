using PmTool.Work.Persistence;

namespace PmTool.Work.ChangeSets;

/// <summary>變更集的資料表列與合約的型別之間的轉換。</summary>
internal static class ChangeSetRecords
{
    public const string Pending = "pending";
    public const string Applied = "applied";
    public const string Rejected = "rejected";
    public const string Expired = "expired";

    public static ChangeSetSummary ToSummary(ChangeSetEntity entity) =>
        new(entity.Number, ParseStatus(entity.Status), entity.BaseVersion, entity.Summary, entity.ProposedBy, entity.ProposedAt)
        {
            DecidedBy = entity.DecidedBy,
            DecidedAt = entity.DecidedAt,
            AppliedVersion = entity.AppliedVersion,
            Origin = ParseOrigin(entity.ProposedVia),
        };

    private const string AssistantOrigin = "assistant";

    private const string AgentOrigin = "agent";

    /// <summary>人自己送的存 null（舊的資料也是 null），只有 AI 助理提出的寫 <c>assistant</c>。</summary>
    public static string? FormatOrigin(ChangeSetOrigin origin) => origin switch
    {
        ChangeSetOrigin.Direct => null,
        ChangeSetOrigin.Assistant => AssistantOrigin,
        ChangeSetOrigin.Agent => AgentOrigin,
        _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, "不認得的來源。"),
    };

    public static ChangeSetOrigin ParseOrigin(string? text) => text switch
    {
        AssistantOrigin => ChangeSetOrigin.Assistant,
        AgentOrigin => ChangeSetOrigin.Agent,
        _ => ChangeSetOrigin.Direct,
    };

    // 存的是固定的文字，不是列舉的數字或名稱：列舉改了順序或名字，舊資料的意思也不會跟著變。
    public static ChangeSetStatus ParseStatus(string text) => text switch
    {
        Pending => ChangeSetStatus.Pending,
        Applied => ChangeSetStatus.Applied,
        Rejected => ChangeSetStatus.Rejected,
        Expired => ChangeSetStatus.Expired,
        _ => throw new InvalidOperationException($"資料表裡有不認得的變更集狀態 {text}。"),
    };
}
