using PmTool.Abstractions;
using PmTool.Storage;

namespace PmTool.Api.Tests.Support;

/// <summary>測試用的儲存空間配額：回傳值由測試設定，記住問過什麼。預設全部放行、沒有用量。</summary>
internal sealed class FakeStorageQuota : IStorageQuota
{
    private readonly Lock _gate = new();
    private readonly List<(Guid OrganizationId, Guid? PersonId, long IncomingBytes)> _checks = [];

    /// <summary>檢查的結果。改成別的種類就能測「滿了」的回應。</summary>
    public StorageQuotaCheck Check { get; set; } = new(StorageQuotaVerdict.Allowed, 0, 0, 0, 0);

    /// <summary>用量報告；null 時依呼叫者是不是組織管理員回預設的報告或 Forbidden。</summary>
    public Result<StorageUsageReport>? Report { get; set; }

    public IReadOnlyList<(Guid OrganizationId, Guid? PersonId, long IncomingBytes)> Checks
    {
        get
        {
            lock (_gate)
            {
                return [.. _checks];
            }
        }
    }

    public Task<StorageQuotaCheck> CheckAsync(Guid organizationId, Guid? personId, long incomingBytes, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _checks.Add((organizationId, personId, incomingBytes));
        }

        return Task.FromResult(Check);
    }

    public Task<Result<StorageUsageReport>> ReportAsync(Actor actor, CancellationToken cancellationToken)
    {
        if (Report is { } configured)
        {
            return Task.FromResult(configured);
        }

        Result<StorageUsageReport> result = actor.IsOrganizationAdmin
            ? new StorageUsageReport(
                20L * StorageQuotaSettings.GiB,
                2L * StorageQuotaSettings.GiB,
                [new StorageAreaUsage(StorageArea.ItemAttachments, 0, 0), new StorageAreaUsage(StorageArea.KnowledgeAttachments, 0, 0)])
            : new Failure(FailureKind.Forbidden, StorageErrorCodes.Forbidden, "只有組織管理員能看儲存空間的用量。");
        return Task.FromResult(result);
    }
}
