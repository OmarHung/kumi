using System.Text.Json;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.NewItems;

/// <summary>資料列與合約型別之間的轉換，以及 <c>items</c>、<c>created_keys</c> 欄位的 JSON。</summary>
internal static class NewItemMapper
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string WriteItems(IReadOnlyList<NewItemDraft> items) => JsonSerializer.Serialize(items, Options);

    public static IReadOnlyList<NewItemDraft> ReadItems(string json) =>
        JsonSerializer.Deserialize<List<NewItemDraft>>(json, Options) ?? [];

    public static string WriteKeys(IReadOnlyList<string?> keys) => JsonSerializer.Serialize(keys, Options);

    public static IReadOnlyList<string?> ReadKeys(string json) =>
        JsonSerializer.Deserialize<List<string?>>(json, Options) ?? [];

    public static string StatusName(NewItemProposalStatus status) => status switch
    {
        NewItemProposalStatus.Pending => NewItemProposalRow.PendingStatus,
        NewItemProposalStatus.Applying => NewItemProposalRow.ApplyingStatus,
        NewItemProposalStatus.Applied => NewItemProposalRow.AppliedStatus,
        NewItemProposalStatus.Dismissed => NewItemProposalRow.DismissedStatus,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "不認得的建議狀態。"),
    };

    /// <summary>
    /// 停在「建立中」超過 <see cref="AiRules.NewItemProposalApplyStale"/> 的（程序在中途結束）讀出來一律是已套用，
    /// 說明建立的過程中斷了；已經建立的項目編號照給。
    /// </summary>
    public static NewItemProposal ToProposal(NewItemProposalRow row, DateTimeOffset now)
    {
        var items = ReadItems(row.Items);
        var keys = ReadKeys(row.CreatedKeys);
        var isStale = IsStale(row, now);
        var status = isStale ? NewItemProposalStatus.Applied : row.Status switch
        {
            NewItemProposalRow.ApplyingStatus => NewItemProposalStatus.Applying,
            NewItemProposalRow.AppliedStatus => NewItemProposalStatus.Applied,
            NewItemProposalRow.DismissedStatus => NewItemProposalStatus.Dismissed,
            _ => NewItemProposalStatus.Pending,
        };
        return new NewItemProposal(row.Id, row.ProjectKey, items, keys, status, row.CreatedAt)
        {
            DecidedAt = row.DecidedAt,
            Error = isStale ? AiText.Cut(NewItemApplyReport.Interrupted(items, keys), AiRules.MaxErrorLength) : row.Error,
        };
    }

    /// <summary>程序中途結束的判斷，套用時也要用：這份建議還在「建立中」而且沒有過期，就是別人正在建。</summary>
    public static bool IsStale(NewItemProposalRow row, DateTimeOffset now) =>
        row.Status == NewItemProposalRow.ApplyingStatus && now - (row.DecidedAt ?? row.CreatedAt) > AiRules.NewItemProposalApplyStale;
}
