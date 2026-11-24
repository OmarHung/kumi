using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Persistence;
using static PmTool.Work.Common.TextRules;

namespace PmTool.Work.Items;

/// <param name="Path">負責人是 assignee，成員欄位是欄位的 key。</param>
/// <param name="Name">放進訊息的欄位顯示名稱。</param>
internal sealed record PersonReference(string Path, string Name, Guid PersonId);

/// <summary>負責人與成員欄位指到的人都必須是專案成員。一次查完，不逐一查。</summary>
internal static class MemberRules
{
    public static async Task<IReadOnlyList<FailureDetail>> CheckAsync(
        WorkDbContext db, ProjectAccess access, IReadOnlyList<PersonReference> references, CancellationToken cancellationToken)
    {
        if (references.Count == 0)
        {
            return [];
        }

        var people = references.Select(r => r.PersonId).Distinct().ToArray();
        var members = await db.ProjectMembers
            .Where(m => m.OrganizationId == access.Project.OrganizationId
                && m.ProjectId == access.Project.Id
                && people.Contains(m.PersonId))
            .Select(m => m.PersonId)
            .ToListAsync(cancellationToken);
        return [.. references
            .Where(r => !members.Contains(r.PersonId))
            .Select(r => new FailureDetail(
                WorkErrorCodes.ItemUnknownAssignee, r.Path, $"{Quote(r.Name)}只能填這個專案的成員，請先把這個人加進專案。"))];
    }
}
