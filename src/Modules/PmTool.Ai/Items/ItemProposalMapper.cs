using System.Text.Json;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.Items;

/// <summary>資料列與合約型別之間的轉換，以及 <c>changes</c> 欄位的 JSON。</summary>
internal static class ItemProposalMapper
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string WriteChanges(IEnumerable<ItemFieldChange> changes) =>
        JsonSerializer.Serialize(changes.Select(c => new Stored(c.Field, c.Value)), Options);

    public static IReadOnlyList<ItemFieldChange> ReadChanges(string json) =>
        [.. (JsonSerializer.Deserialize<List<Stored>>(json, Options) ?? []).Select(s => new ItemFieldChange(s.Field, s.Value))];

    public static string StatusName(ItemProposalStatus status) => status switch
    {
        ItemProposalStatus.Pending => ItemProposalRow.PendingStatus,
        ItemProposalStatus.Applied => ItemProposalRow.AppliedStatus,
        ItemProposalStatus.Dismissed => ItemProposalRow.DismissedStatus,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "不認得的建議狀態。"),
    };

    public static ItemProposal ToProposal(ItemProposalRow row) => new(
        row.Id,
        row.ProjectKey,
        row.ItemKey,
        row.ItemTitle,
        ReadChanges(row.Changes),
        row.Comment,
        row.Status switch
        {
            ItemProposalRow.AppliedStatus => ItemProposalStatus.Applied,
            ItemProposalRow.DismissedStatus => ItemProposalStatus.Dismissed,
            _ => ItemProposalStatus.Pending,
        },
        row.CreatedAt)
    {
        DecidedAt = row.DecidedAt,
        Error = row.Error,
    };

    private sealed record Stored(string Field, JsonElement Value);
}
