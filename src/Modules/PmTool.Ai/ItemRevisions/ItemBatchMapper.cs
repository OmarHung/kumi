using System.Text.Json;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.ItemRevisions;

/// <summary>批次修改建議的資料列與合約型別之間的轉換，以及 <c>items</c> 欄位的 JSON。</summary>
internal static class ItemBatchMapper
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>存起來的一筆。<c>Error</c> 套用後才有；<c>ExpectedDescriptionRevision</c> 只有改說明的才有。</summary>
    internal sealed record StoredEntry(
        string ItemKey,
        string Title,
        List<StoredChange> Changes,
        string? Comment,
        string? ExpectedDescriptionRevision,
        string? Error,
        CommentReplyTarget? ReplyTo = null);

    internal sealed record StoredChange(string Field, JsonElement Value);

    public static string Write(IEnumerable<StoredEntry> entries) => JsonSerializer.Serialize(entries, Options);

    public static IReadOnlyList<StoredEntry> Read(string json) =>
        JsonSerializer.Deserialize<List<StoredEntry>>(json, Options) ?? [];

    public static StoredEntry Store(
        string itemKey,
        string title,
        IEnumerable<ItemFieldChange> changes,
        string? comment,
        string? descriptionRevision,
        CommentReplyTarget? replyTo = null) =>
        new(itemKey, title, [.. changes.Select(c => new StoredChange(c.Field, c.Value))], comment, descriptionRevision, null, replyTo);

    public static IReadOnlyList<ItemFieldChange> ChangesOf(StoredEntry entry) =>
        [.. entry.Changes.Select(c => new ItemFieldChange(c.Field, c.Value))];

    public static ItemBatchProposal ToProposal(ItemBatchProposalRow row) => new(
        row.Id,
        row.ProjectKey,
        [.. Read(row.Items).Select(e => new ItemBatchEntry(e.ItemKey, e.Title, ChangesOf(e), e.Comment, e.Error) { ReplyTo = e.ReplyTo })],
        row.Status switch
        {
            ItemBatchProposalRow.AppliedStatus => ItemProposalStatus.Applied,
            ItemBatchProposalRow.DismissedStatus => ItemProposalStatus.Dismissed,
            _ => ItemProposalStatus.Pending,
        },
        row.CreatedAt)
    {
        DecidedAt = row.DecidedAt,
        Error = row.Error,
    };
}
