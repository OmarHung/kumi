using System.Text.Json;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.ProjectChangeProposals;

/// <summary>資料列與合約型別之間的轉換，以及 <c>before</c> 欄位的 JSON。</summary>
internal static class ProjectChangeMapper
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string WriteBefore(ProjectSnapshot before) => JsonSerializer.Serialize(before, Options);

    public static ProjectSnapshot ReadBefore(string json) =>
        JsonSerializer.Deserialize<ProjectSnapshot>(json, Options) ?? new ProjectSnapshot(string.Empty, false, null, null);

    public static ProjectChangeProposal ToProposal(ProjectChangeProposalRow row) =>
        new(row.Id, row.ProjectKey, row.Name, row.Archived, row.ChangesDescription, row.Description, ReadBefore(row.Before), StatusOf(row.Status), row.CreatedAt)
        {
            Reason = row.Reason,
            DecidedAt = row.DecidedAt,
            Error = row.Error,
        };

    private static ItemProposalStatus StatusOf(string status) => status switch
    {
        ProjectChangeProposalRow.AppliedStatus => ItemProposalStatus.Applied,
        ProjectChangeProposalRow.DismissedStatus => ItemProposalStatus.Dismissed,
        _ => ItemProposalStatus.Pending,
    };
}
