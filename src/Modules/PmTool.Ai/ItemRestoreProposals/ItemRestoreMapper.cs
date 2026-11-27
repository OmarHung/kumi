using System.Text.Json;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.ItemRestoreProposals;

/// <summary>復原建議的資料列與合約型別之間的轉換，以及 <c>items</c>、<c>restored_keys</c> 欄位的 JSON。</summary>
internal static class ItemRestoreMapper
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string WriteItems(IEnumerable<ItemRestoreTarget> items) => JsonSerializer.Serialize(items, Options);

    public static IReadOnlyList<ItemRestoreTarget> ReadItems(string json) =>
        JsonSerializer.Deserialize<List<ItemRestoreTarget>>(json, Options) ?? [];

    public static string WriteKeys(IEnumerable<string> keys) => JsonSerializer.Serialize(keys, Options);

    public static IReadOnlyList<string> ReadKeys(string json) => JsonSerializer.Deserialize<List<string>>(json, Options) ?? [];

    public static ItemRestoreProposal ToProposal(ItemRestoreProposalRow row) => new(
        row.Id,
        row.ProjectKey,
        ReadItems(row.Items),
        row.Status switch
        {
            ItemRestoreProposalRow.AppliedStatus => ItemProposalStatus.Applied,
            ItemRestoreProposalRow.DismissedStatus => ItemProposalStatus.Dismissed,
            _ => ItemProposalStatus.Pending,
        },
        row.CreatedAt)
    {
        Reason = row.Reason,
        DecidedAt = row.DecidedAt,
        RestoredKeys = ReadKeys(row.RestoredKeys),
        Error = row.Error,
    };
}
