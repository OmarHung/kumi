using System.Collections.Concurrent;
using PmTool.Abstractions;

namespace PmTool.Work.Tests;

/// <summary>
/// 測試用的組織成員查詢：預設每個人都算在組織裡（測試不建身分模組的人員），要測「不在組織裡」的把他排除。
/// 排除只作用在指定的人，不分組織。
/// </summary>
internal sealed class FakeOrganizationMembers : IOrganizationMembers
{
    private readonly ConcurrentDictionary<Guid, byte> _excluded = new();

    public void Exclude(Guid personId) => _excluded[personId] = 0;

    public void Include(Guid personId) => _excluded.TryRemove(personId, out _);

    public Task<bool> ContainsAsync(Guid organizationId, Guid personId, CancellationToken cancellationToken) =>
        Task.FromResult(!_excluded.ContainsKey(personId));
}
