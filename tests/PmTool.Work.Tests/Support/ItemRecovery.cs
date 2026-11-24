using PmTool.Abstractions;

namespace PmTool.Work.Tests;

/// <summary>測試裡刪除與復原項目的捷徑：失敗時直接讓測試失敗，說出是哪個代碼。</summary>
internal static class ItemRecovery
{
    public static async Task DeleteAsync(WorkTestHost host, Actor actor, params string[] itemKeys)
    {
        foreach (var key in itemKeys)
        {
            // 每一步各隔一秒：同一刻的活動排序不固定，測試要看得出先後。
            host.Clock.Advance(TimeSpan.FromSeconds(1));
            var result = await host.Items.DeleteAsync(actor, key, CancellationToken.None);
            Assert.That(result.Succeeded, Is.True, () => $"刪除 {key}：{result.Failure!.Code} {result.Failure.Message}");
        }
    }

    public static async Task<WorkItem> RestoreAsync(WorkTestHost host, Actor actor, string itemKey)
    {
        host.Clock.Advance(TimeSpan.FromSeconds(1));
        var result = await host.Items.RestoreAsync(actor, itemKey, CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => $"復原 {itemKey}：{result.Failure!.Code} {result.Failure.Message}");
        return result.Value;
    }

    public static async Task<IReadOnlyList<DeletedItemSummary>> ListDeletedAsync(WorkTestHost host, Actor actor, string projectKey)
    {
        var page = await host.Items.ListDeletedAsync(
            actor, projectKey, new DeletedItemQuery { Limit = DeletedItemQuery.MaxLimit }, CancellationToken.None);
        Assert.That(page.Succeeded, Is.True, () => page.Failure!.Message);
        return page.Value.Items;
    }
}
