using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Identity.Persistence;

namespace PmTool.Identity.People;

/// <summary>
/// 這個人在不在這個組織裡（停用的也算，和 <see cref="IPeopleDirectory.FindManyAsync"/> 一致）。
/// 其他模組只問「是不是同一個組織」，不需要人員的內容。
/// </summary>
internal sealed class OrganizationMembers(IDbContextFactory<IdentityDbContext> contexts) : IOrganizationMembers
{
    public async Task<bool> ContainsAsync(Guid organizationId, Guid personId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.People.AsNoTracking().AnyAsync(p => p.OrganizationId == organizationId && p.Id == personId, cancellationToken);
    }
}
