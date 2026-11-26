using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Work;

namespace PmTool.Ai.NewItems;

/// <summary>建立的結果：和項目一一對應的編號（沒建立是 null），以及沒建立的說明（全部建立是 null）。</summary>
internal sealed record NewItemApplyOutcome(IReadOnlyList<string?> Keys, string? Error);

/// <summary>
/// 照順序把一份建議的項目寫進工作引擎，一律用套用的人的身分。建立不是冪等的，所以每建好一個就立刻把編號記進資料庫
/// （程序在中途結束時，已經建立的才查得到）。第一個就失敗時什麼都沒建立，回傳它的失敗；之後的失敗不復原已經建立的，
/// 掛在失敗的項目底下的跳過，說明在 <see cref="NewItemApplyOutcome.Error"/>。
/// </summary>
internal sealed class NewItemApplier(IDbContextFactory<AiDbContext> contexts, IWorkItemService items, ILogger<NewItemApplier> logger)
{
    /// <summary>每個項目建立時的時限。不跟著請求取消：使用者關掉頁面不能讓建立停在一半。</summary>
    internal static readonly TimeSpan CreateTimeout = TimeSpan.FromSeconds(60);

    private const string SkippedReason = "上層沒有建立成功";

    public async Task<Result<NewItemApplyOutcome>> ApplyAsync(
        Actor actor, Guid proposalId, string projectKey, IReadOnlyList<NewItemDraft> drafts)
    {
        var keys = new string?[drafts.Count];
        var reasons = new List<(int Index, string Reason)>();
        var anyCreated = false;
        for (var i = 0; i < drafts.Count; i++)
        {
            var parent = ParentOf(drafts[i], drafts, keys);
            if (parent.Skip)
            {
                reasons.Add((i, SkippedReason));
                continue;
            }

            var created = await CreateOneAsync(actor, projectKey, drafts[i], parent.Key, anyCreated);
            if (created.Failure is { } failure)
            {
                if (!anyCreated)
                {
                    return failure;
                }

                reasons.Add((i, failure.Message));
                continue;
            }

            keys[i] = created.Value.Key;
            anyCreated = true;
            await SaveKeysAsync(proposalId, keys);
        }

        return new NewItemApplyOutcome(keys, reasons.Count == 0 ? null : NewItemApplyReport.PartlyFailed(drafts, reasons));
    }

    // 已經有項目建立之後，後面的項目丟例外不能讓整個請求變成 500、建議維持待決定（再按一次會重複建立）：
    // 當作那一個失敗，標成已套用並說明。還沒有任何項目建立時照常丟出去，什麼都沒建。
    private async Task<Result<WorkItem>> CreateOneAsync(Actor actor, string projectKey, NewItemDraft draft, string? parentKey, bool somethingDone)
    {
        using var timeout = new CancellationTokenSource(CreateTimeout);
        try
        {
            return await items.CreateAsync(actor, projectKey, ToNewWorkItem(draft, parentKey), timeout.Token);
        }
        catch (Exception exception) when (somethingDone && exception is not OutOfMemoryException)
        {
            // 只記例外的種類：訊息可能帶內部資訊。
            logger.LogError("套用新增項目的建議時有一個項目丟了例外：{ExceptionType}", exception.GetType().Name);
            return new Failure(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "發生未預期的錯誤。");
        }
    }

    private static (bool Skip, string? Key) ParentOf(NewItemDraft draft, IReadOnlyList<NewItemDraft> drafts, string?[] keys)
    {
        if (draft.ParentRef is not { } reference)
        {
            return (false, draft.ParentKey);
        }

        // 檢查時保證 parentRef 指到排在前面的項目；資料被改過、找不到時也當作上層沒建立成功。
        var index = drafts.ToList().FindIndex(d => d.Ref == reference);
        return index >= 0 && keys[index] is { } parentKey ? (false, parentKey) : (true, null);
    }

    private async Task SaveKeysAsync(Guid proposalId, IReadOnlyList<string?> keys)
    {
        var json = NewItemMapper.WriteKeys(keys);
        await using var db = await contexts.CreateDbContextAsync(CancellationToken.None);
        await db.NewItemProposals.Where(p => p.Id == proposalId)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.CreatedKeys, json), CancellationToken.None);
    }

    private static NewWorkItem ToNewWorkItem(NewItemDraft draft, string? parentKey) => new()
    {
        Type = draft.Type,
        Title = draft.Title,
        Description = draft.Description,
        AssigneeId = draft.AssigneeId,
        Priority = draft.Priority is null ? Priority.None : Enum.Parse<Priority>(draft.Priority, ignoreCase: true),
        StartDate = ParseDate(draft.StartDate),
        DueDate = ParseDate(draft.DueDate),
        Estimate = draft.Estimate,
        ParentKey = parentKey,
        Fields = draft.Fields,
    };

    private static DateOnly? ParseDate(string? text) =>
        text is null ? null : DateOnly.ParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
