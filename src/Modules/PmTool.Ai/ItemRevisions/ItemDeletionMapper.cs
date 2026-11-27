using System.Text.Json;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.ItemRevisions;

/// <summary>刪除建議的資料列與合約型別之間的轉換，以及 <c>items</c>、<c>deleted_keys</c> 欄位的 JSON。</summary>
internal static class ItemDeletionMapper
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string WriteItems(IEnumerable<ItemDeletionTarget> items) => JsonSerializer.Serialize(items, Options);

    public static IReadOnlyList<ItemDeletionTarget> ReadItems(string json) =>
        JsonSerializer.Deserialize<List<ItemDeletionTarget>>(json, Options) ?? [];

    public static string WriteKeys(IEnumerable<string> keys) => JsonSerializer.Serialize(keys, Options);

    public static IReadOnlyList<string> ReadKeys(string json) => JsonSerializer.Deserialize<List<string>>(json, Options) ?? [];

    public static ItemDeletionProposal ToProposal(ItemDeletionProposalRow row) => new(
        row.Id,
        row.ProjectKey,
        ReadItems(row.Items),
        row.Status switch
        {
            ItemDeletionProposalRow.AppliedStatus => ItemProposalStatus.Applied,
            ItemDeletionProposalRow.DismissedStatus => ItemProposalStatus.Dismissed,
            _ => ItemProposalStatus.Pending,
        },
        row.CreatedAt)
    {
        Reason = row.Reason,
        DecidedAt = row.DecidedAt,
        DeletedKeys = ReadKeys(row.DeletedKeys),
        Error = row.Error,
    };
}
