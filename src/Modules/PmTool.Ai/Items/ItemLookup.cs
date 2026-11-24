using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Ai.Items;

/// <summary>助理只碰對話所屬專案裡、使用者看得到的項目；其他一律當作不存在。</summary>
internal static class ItemLookup
{
    public static Failure NotFound(string itemKey) =>
        new(FailureKind.NotFound, AiErrorCodes.ItemNotFound, $"找不到項目「{itemKey}」。");

    /// <summary>
    /// 失敗一律是 <see cref="AiErrorCodes.ItemNotFound"/>：看不到、不在這個專案、編號寫錯，對外沒有差別，
    /// 不讓人用它探測別的專案有哪些項目。
    /// </summary>
    public static async Task<Result<WorkItem>> FindAsync(
        IWorkItemService items, Actor actor, string projectKey, string? itemKey, CancellationToken cancellationToken)
    {
        var key = itemKey?.Trim() ?? string.Empty;
        if (key.Length is 0 or > ItemProposalLimits.MaxItemKeyLength || key.Any(char.IsControl))
        {
            return NotFound("（編號不合規定）");
        }

        var found = await items.GetAsync(actor, key, cancellationToken);
        if (found.Failure is not null || !string.Equals(found.Value.ProjectKey, projectKey, StringComparison.OrdinalIgnoreCase))
        {
            return NotFound(key);
        }

        return found.Value;
    }
}
