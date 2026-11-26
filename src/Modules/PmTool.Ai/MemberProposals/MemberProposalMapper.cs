using System.Text.Json;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.MemberProposals;

/// <summary>資料列與合約型別之間的轉換，以及 <c>changes</c> 欄位的 JSON。</summary>
internal static class MemberProposalMapper
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string WriteChanges(IReadOnlyList<MemberChange> changes) => JsonSerializer.Serialize(changes, Options);

    public static IReadOnlyList<MemberChange> ReadChanges(string json) =>
        JsonSerializer.Deserialize<List<MemberChange>>(json, Options) ?? [];

    public static MemberProposal ToProposal(MemberProposalRow row) =>
        new(row.Id, row.ProjectKey, ReadChanges(row.Changes), StatusOf(row.Status), row.CreatedAt)
        {
            DecidedAt = row.DecidedAt,
            Error = row.Error,
        };

    private static MemberProposalStatus StatusOf(string status) => status switch
    {
        MemberProposalRow.AppliedStatus => MemberProposalStatus.Applied,
        MemberProposalRow.DismissedStatus => MemberProposalStatus.Dismissed,
        _ => MemberProposalStatus.Pending,
    };
}
