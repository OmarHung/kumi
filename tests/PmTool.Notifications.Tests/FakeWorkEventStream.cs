using PmTool.Work;

namespace PmTool.Notifications.Tests;

/// <summary>
/// 通知只用事件流的位置換算。預設原樣回傳；測試給一個規則，模擬還原到新資料庫之後舊位置被換算成新的。
/// </summary>
internal sealed class FakeWorkEventStream(Func<WorkEventPosition, WorkEventPosition>? resolve = null) : IWorkEventStream
{
    public Task<WorkEventPosition> ResolvePositionAsync(WorkEventPosition stored, CancellationToken cancellationToken) =>
        Task.FromResult(resolve?.Invoke(stored) ?? stored);

    public Task<WorkEventPosition> GetLatestPositionAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException("通知的讀者不自己讀事件流。");

    public Task<IReadOnlyList<WorkEvent>> ReadAfterAsync(WorkEventPosition after, int limit, CancellationToken cancellationToken) =>
        throw new NotSupportedException("通知的讀者不自己讀事件流。");
}
