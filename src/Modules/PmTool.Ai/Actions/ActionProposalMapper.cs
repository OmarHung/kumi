using System.Text.Json;
using System.Text.Json.Serialization;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.Actions;

/// <summary>動作建議的資料列與合約型別之間的轉換，以及 <c>steps</c>、<c>inputs</c> 欄位的 JSON。</summary>
internal static class ActionProposalMapper
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string WriteSteps(IReadOnlyList<ActionStep> steps) => JsonSerializer.Serialize(steps, Options);

    public static IReadOnlyList<ActionStep> ReadSteps(string json) => JsonSerializer.Deserialize<List<ActionStep>>(json, Options) ?? [];

    public static string WriteInputs(IReadOnlyList<ActionInput> inputs) => JsonSerializer.Serialize(inputs, Options);

    public static IReadOnlyList<ActionInput> ReadInputs(string json) => JsonSerializer.Deserialize<List<ActionInput>>(json, Options) ?? [];

    /// <summary>停在「建立中」超過 <see cref="AiRules.NewItemProposalApplyStale"/> 的（程序在中途結束）讀出來一律是已套用。</summary>
    public static bool IsStale(ActionProposalRow row, DateTimeOffset now) =>
        row.Status == ActionProposalRow.ApplyingStatus && now - (row.DecidedAt ?? row.CreatedAt) > AiRules.NewItemProposalApplyStale;

    public static ActionProposal ToProposal(ActionProposalRow row, DateTimeOffset now)
    {
        var steps = ReadSteps(row.Steps);
        var isStale = IsStale(row, now);
        var status = isStale ? NewItemProposalStatus.Applied : row.Status switch
        {
            ActionProposalRow.ApplyingStatus => NewItemProposalStatus.Applying,
            ActionProposalRow.AppliedStatus => NewItemProposalStatus.Applied,
            ActionProposalRow.DismissedStatus => NewItemProposalStatus.Dismissed,
            _ => NewItemProposalStatus.Pending,
        };
        return new ActionProposal(row.Id, row.Kind, row.ProjectKey, row.Title, steps, status, row.CreatedAt)
        {
            Reason = row.Reason,
            // 已經不能再填了：套用中、套用後都不再給輸入欄位。
            Inputs = status == NewItemProposalStatus.Pending ? ReadInputs(row.Inputs) : [],
            DecidedAt = row.DecidedAt,
            Error = isStale ? Interrupted(steps) : row.Error,
        };
    }

    public static string Interrupted(IReadOnlyList<ActionStep> steps) =>
        $"套用的過程中斷了：{steps.Count} 步裡做完 {steps.Count(s => s.Done)} 步，請到對應的頁面確認現況。";

    public static string? Summarize(IReadOnlyList<ActionStep> steps)
    {
        var failed = steps.Count(s => s.Error is not null);
        return failed == 0 ? null : AiText.Cut($"{steps.Count} 步裡有 {failed} 步沒成功。", AiRules.MaxErrorLength);
    }
}
