using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Ai.Tests.Assistant.Fakes;

/// <summary>
/// 批次寫入的占位：刪除建議的服務要它才組得起來，助理一開始回覆就會解析整組服務。
/// 真的要看批次刪除怎麼被呼叫的測試（ItemRevisions）自己註冊有記錄的假物件蓋過它。
/// </summary>
internal sealed class FakeBulkStub : IBulkItemService
{
    public Task<Result<BulkUpdateResult>> UpdateAsync(Actor actor, string projectKey, BulkItemUpdate update, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<BulkDeleteResult>> DeleteAsync(Actor actor, string projectKey, IReadOnlyList<string> itemKeys, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<BulkRestoreResult>> RestoreAsync(Actor actor, string projectKey, IReadOnlyList<string> itemKeys, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
