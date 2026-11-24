using System.Threading.RateLimiting;

namespace PmTool.Api.Mcp;

/// <summary>每個人每分鐘最多 <see cref="McpLimits.ToolCallsPerMinute"/> 個工具呼叫。閒置的分割區由限流器自己回收。</summary>
internal sealed class McpToolRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<Guid> _limiter = PartitionedRateLimiter.Create<Guid, Guid>(personId =>
        RateLimitPartition.GetFixedWindowLimiter(personId, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = McpLimits.ToolCallsPerMinute,
            Window = TimeSpan.FromSeconds(McpLimits.ToolCallWindowSeconds),
            QueueLimit = 0,
            AutoReplenishment = true,
        }));

    public bool TryAcquire(Guid personId)
    {
        using var lease = _limiter.AttemptAcquire(personId);
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
