using System.Text.Json;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Work;

namespace PmTool.Ai.Items;

/// <summary>
/// 把一份建議照順序寫進工作引擎，一律用套用的人的身分：改類型 → 欄位一次（含說明，帶建立時的說明版本；上層、週期、里程碑也在這裡）
/// → 改狀態 → 留言。類型排最前面：之後的欄位與狀態是照新類型檢查的。
/// 第一個真的要做的步驟失敗時什麼都沒改，回傳它的失敗；之後的步驟失敗時已經改的不復原，回傳說明哪些沒套用的文字。
/// </summary>
internal sealed class ItemProposalApplier(IWorkItemService items, IItemActivityService activity, ILogger<ItemProposalApplier> logger)
{
    /// <summary>成功時是空字串（全部套用）或「部分沒套用」的說明。</summary>
    public Task<Result<string>> ApplyAsync(Actor actor, ItemProposalRow row, CancellationToken cancellationToken) =>
        ApplyAsync(
            actor,
            row.ItemKey,
            ItemProposalMapper.ReadChanges(row.Changes),
            row.Comment,
            row.DescriptionRevision,
            ItemProposalMapper.ReadReply(row.ReplyTo)?.CommentId,
            cancellationToken);

    /// <summary>單一項目的建議與批次修改建議的每一筆共用的套用步驟。</summary>
    public async Task<Result<string>> ApplyAsync(
        Actor actor,
        string itemKey,
        IReadOnlyList<ItemFieldChange> changes,
        string? comment,
        string? descriptionRevision,
        Guid? replyTo,
        CancellationToken cancellationToken)
    {
        var type = changes.FirstOrDefault(c => c.Field == ItemFieldNames.Type);
        var status = changes.FirstOrDefault(c => c.Field == ItemFieldNames.Status);
        var patch = ToPatch(changes.Where(c => c.Field is not (ItemFieldNames.Status or ItemFieldNames.Type)), descriptionRevision);
        var failures = new List<string>();
        var done = false;

        // 類型和現在一樣時工作引擎照樣回成功，但什麼都沒改：跳過，才不會讓後面的失敗被當成「部分套用」。
        if (type is not null && !await IsCurrentTypeAsync(actor, itemKey, type, cancellationToken))
        {
            var movesParent = changes.Any(c => c.Field == ItemFieldNames.ParentKey);
            var changed = await RunStepAsync(
                () => ChangeTypeAsync(actor, itemKey, type.Value.GetString() ?? string.Empty, movesParent, cancellationToken), done);
            if (Settle(changed.Failure, "類型", ref done, failures) is { } stop)
            {
                return stop;
            }
        }

        if (patch is not null)
        {
            var updated = await RunStepAsync(() => items.UpdateAsync(actor, itemKey, patch, cancellationToken), done);
            if (Settle(updated.Failure, "欄位", ref done, failures) is { } stop)
            {
                return stop;
            }
        }

        var statusFailed = false;
        if (status is not null)
        {
            var moved = await RunStepAsync(
                () => items.TransitionAsync(actor, itemKey, status.Value.GetString() ?? string.Empty, cancellationToken), done);
            if (Settle(moved.Failure, "狀態", ref done, failures) is { } stop)
            {
                return stop;
            }

            statusFailed = moved.Failure is not null;
        }

        if (comment is not null)
        {
            // 狀態沒套用成功時留言多半是在說明那次狀態變更，沒有變更就不要留。
            if (statusFailed)
            {
                failures.Add("留言（狀態沒有套用成功，所以沒有加）");
            }
            else
            {
                var added = await RunStepAsync(
                    () => activity.AddCommentAsync(actor, itemKey, comment, replyTo, cancellationToken), done);
                if (Settle(added.Failure, "留言", ref done, failures) is { } stop)
                {
                    return stop;
                }
            }
        }

        return failures.Count == 0 ? string.Empty : "有幾項沒有套用：" + string.Join("；", failures) + "。其餘已經套用。";
    }

    // 讀不到項目時當作不同，照常交給改類型的步驟回報失敗。
    private async Task<bool> IsCurrentTypeAsync(Actor actor, string itemKey, ItemFieldChange type, CancellationToken cancellationToken)
    {
        var current = await items.GetAsync(actor, itemKey, cancellationToken);
        return current.Failure is null && string.Equals(current.Value.Type, type.Value.GetString(), StringComparison.Ordinal);
    }

    // 類型與上層一起改時（故事改成任務、同時從史詩搬到故事底下），先改類型會被現在的上層擋下，先改上層也會被現在的類型擋下：
    // 兩種順序的中間狀態都不合法。這份建議本來就要改上層時，先把上層清掉（任何類型都能放在最上層）再改類型，
    // 新的上層跟著後面的欄位寫。改類型還是失敗就把原本的上層放回去，不留下搬到最上層的半成品，整份照「第一步失敗」維持待決定。
    private async Task<Result<WorkItem>> ChangeTypeAsync(
        Actor actor, string itemKey, string typeKey, bool movesParent, CancellationToken cancellationToken)
    {
        var changed = await items.ChangeTypeAsync(actor, itemKey, typeKey, cancellationToken);
        if (!movesParent || changed.Failure is not { } refused || !IsParentRefusal(refused))
        {
            return changed;
        }

        var current = await items.GetAsync(actor, itemKey, cancellationToken);
        if (current.Failure is not null || current.Value.ParentKey is not { } originalParent)
        {
            return refused;
        }

        var detached = await items.UpdateAsync(actor, itemKey, new WorkItemPatch { ParentKey = new Optional<string?>(null) }, cancellationToken);
        if (detached.Failure is not null)
        {
            return refused;
        }

        var retried = await items.ChangeTypeAsync(actor, itemKey, typeKey, cancellationToken);
        if (retried.Failure is not null)
        {
            var restored = await items.UpdateAsync(
                actor, itemKey, new WorkItemPatch { ParentKey = new Optional<string?>(originalParent) }, cancellationToken);
            if (restored.Failure is not null)
            {
                logger.LogWarning("改類型失敗後放不回原本的上層：{Code}", restored.Failure.Code);
            }
        }

        return retried;
    }

    private static bool IsParentRefusal(Failure failure) =>
        failure.Code == WorkErrorCodes.ItemParentNotAllowed || failure.Details.Any(d => d.Code == WorkErrorCodes.ItemParentNotAllowed);

    // 已經有步驟寫進去之後，後面的步驟丟例外不能讓整個請求變成 500、建議維持待決定（再按一次會把前面的重做）：
    // 當作那一步失敗，標成已套用並說明。還沒有任何步驟成功時照常丟出去，什麼都沒改。
    private async Task<Result<T>> RunStepAsync<T>(Func<Task<Result<T>>> step, bool somethingDone)
    {
        try
        {
            return await step();
        }
        catch (Exception exception) when (somethingDone && exception is not (OutOfMemoryException or OperationCanceledException))
        {
            // 只記例外的種類：訊息可能帶內部資訊。
            logger.LogError("套用修改建議時有一步丟了例外：{ExceptionType}", exception.GetType().Name);
            return new Failure(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "發生未預期的錯誤。");
        }
    }

    // 還沒有任何步驟成功時，失敗就是整份的失敗（沒改任何東西，建議維持待決定）。
    private static Failure? Settle(Failure? failure, string step, ref bool done, List<string> failures)
    {
        if (failure is null)
        {
            done = true;
            return null;
        }

        if (!done)
        {
            return failure;
        }

        failures.Add($"{step}（{failure.Message}）");
        return null;
    }

    private static WorkItemPatch? ToPatch(IEnumerable<ItemFieldChange> changes, string? descriptionRevision)
    {
        var patch = new WorkItemPatch();
        var custom = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var any = false;
        foreach (var (field, value) in changes.Select(c => (c.Field, c.Value)))
        {
            any = true;
            patch = field switch
            {
                ItemFieldNames.Title => patch with { Title = new Optional<string>(value.GetString()!.Trim()) },
                ItemFieldNames.Description => patch with
                {
                    Description = new Optional<string?>(value.ValueKind == JsonValueKind.Null ? null : value.GetString()),
                    ExpectedDescriptionRevision = descriptionRevision,
                },
                ItemFieldNames.Assignee => patch with
                {
                    AssigneeId = new Optional<Guid?>(value.ValueKind == JsonValueKind.Null ? null : Guid.Parse(value.GetString()!)),
                },
                ItemFieldNames.Priority => patch with { Priority = new Optional<Priority>(ParsePriority(value.GetString()!)) },
                ItemFieldNames.StartDate => patch with { StartDate = new Optional<DateOnly?>(ParseDate(value)) },
                ItemFieldNames.DueDate => patch with { DueDate = new Optional<DateOnly?>(ParseDate(value)) },
                ItemFieldNames.Estimate => patch with
                {
                    Estimate = new Optional<decimal?>(value.ValueKind == JsonValueKind.Null ? null : value.GetDecimal()),
                },
                ItemFieldNames.ParentKey => patch with
                {
                    ParentKey = new Optional<string?>(value.ValueKind == JsonValueKind.Null ? null : value.GetString()),
                },
                ItemFieldNames.CycleId => patch with { CycleId = new Optional<Guid?>(ParseId(value)) },
                ItemFieldNames.MilestoneId => patch with { MilestoneId = new Optional<Guid?>(ParseId(value)) },
                _ => AddCustom(patch, custom, field, value),
            };
        }

        return any ? patch with { Fields = custom } : null;
    }

    private static WorkItemPatch AddCustom(WorkItemPatch patch, Dictionary<string, JsonElement> custom, string field, JsonElement value)
    {
        custom[field[ItemFieldNames.CustomPrefix.Length..]] = value;
        return patch;
    }

    private static Guid? ParseId(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : Guid.Parse(value.GetString()!);

    private static Priority ParsePriority(string name) => Enum.Parse<Priority>(name, ignoreCase: true);

    private static DateOnly? ParseDate(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null ? null : DateOnly.ParseExact(value.GetString()!, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
