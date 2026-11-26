using PmTool.Ai.Persistence;

namespace PmTool.Ai.ProjectProposals;

/// <summary>新專案建議的資料列與合約型別之間的轉換。</summary>
internal static class ProjectProposalMapper
{
    public static ProjectProposal ToProposal(ProjectProposalRow row) => new(
        row.Id,
        row.Key,
        row.Name,
        row.Template,
        row.TemplateName,
        row.IncludeExamples,
        row.Description,
        row.Status switch
        {
            ProjectProposalRow.AppliedStatus => ProjectProposalStatus.Applied,
            ProjectProposalRow.DismissedStatus => ProjectProposalStatus.Dismissed,
            _ => ProjectProposalStatus.Pending,
        },
        row.CreatedAt)
    {
        DecidedAt = row.DecidedAt,
        CreatedProjectKey = row.CreatedProjectKey,
    };
}
