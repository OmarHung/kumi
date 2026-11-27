using System.Text.Json;
using System.Text.Json.Serialization;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.CommentProposals;

/// <summary>留言建議的資料列與合約型別之間的轉換，以及 <c>changes</c> 欄位的 JSON（動作寫成小寫字串）。</summary>
internal static class CommentProposalMapper
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string WriteChanges(IEnumerable<CommentChange> changes) => JsonSerializer.Serialize(changes, Options);

    public static IReadOnlyList<CommentChange> ReadChanges(string json) =>
        JsonSerializer.Deserialize<List<CommentChange>>(json, Options) ?? [];

    public static CommentProposal ToProposal(CommentProposalRow row) => new(
        row.Id,
        row.ProjectKey,
        ReadChanges(row.Changes),
        row.Status switch
        {
            CommentProposalRow.AppliedStatus => ItemProposalStatus.Applied,
            CommentProposalRow.DismissedStatus => ItemProposalStatus.Dismissed,
            _ => ItemProposalStatus.Pending,
        },
        row.CreatedAt)
    {
        DecidedAt = row.DecidedAt,
        Error = row.Error,
    };
}
