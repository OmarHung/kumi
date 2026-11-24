using PmTool.Identity;

namespace PmTool.Ai.HostAgents;

/// <summary>
/// 向身分模組確認「現在」的角色與帳號狀態，不看登入當時的 <c>Actor</c>：擁有者被降權、成員被停用之後，下一個請求就不能再用。
/// 擁有者才能管理；擁有者、管理員、一般成員（帳號沒有停用）才能使用，訪客不行。
/// </summary>
internal sealed class HostPeople(IPeopleDirectory directory)
{
    public async Task<bool> IsOwnerAsync(Guid organizationId, Guid personId, CancellationToken cancellationToken) =>
        await RoleOfAsync(organizationId, personId, cancellationToken) == OrganizationRole.Owner;

    public async Task<bool> IsMemberAsync(Guid organizationId, Guid personId, CancellationToken cancellationToken) =>
        await RoleOfAsync(organizationId, personId, cancellationToken) is not null;

    /// <summary>有效成員現在的角色；不在組織裡、停用、訪客是 null。</summary>
    public async Task<OrganizationRole?> RoleOfAsync(Guid organizationId, Guid personId, CancellationToken cancellationToken)
    {
        var found = await directory.FindManyAsync(organizationId, [personId], cancellationToken);
        return found.TryGetValue(personId, out var person) && !person.IsDisabled && person.Role != OrganizationRole.Guest
            ? person.Role
            : null;
    }
}
