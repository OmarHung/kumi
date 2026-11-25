namespace PmTool.Abstractions;

/// <summary>
/// 身分模組實作；其他模組只用它確認「這個人在這個組織裡」（停用的也算，和 IPeopleDirectory.FindManyAsync 一致）。
/// 放在這裡是因為工作引擎不引用身分模組的合約，做法照 <see cref="IOrganizationClock"/>。
/// </summary>
public interface IOrganizationMembers
{
    Task<bool> ContainsAsync(Guid organizationId, Guid personId, CancellationToken cancellationToken);
}
