using System.Text.Json;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Work;

namespace PmTool.Ai.Items;

/// <summary>
/// 把一份建議照順序寫進工作引擎，一律用套用的人的身分：欄位一次（含說明，帶建立時的說明版本）→ 改狀態 → 留言。
/// 第一個真的要做的步驟失敗時什麼都沒改，回傳它的失敗；之後的步驟失敗時已經改的不復原，回傳說明哪些沒套用的文字。
/// </summary>
internal sealed class ItemProposalApplier(IWorkItemService items, IItemActivityService activity, ILogger<ItemProposalApplier> logger)
{
    /// <summary>成功時是空字串（全部套用）或「部分沒套用」的說明。</summary>
    public async Task<Result<string>> ApplyAsync(Actor actor, ItemProposalRow row, CancellationToken cancellationToken)
    {
        var changes = ItemProposalMapper.ReadChanges(row.Changes);
        var status = changes.FirstOrDefault(c => c.Field == ItemFieldNames.Status);
        var patch = ToPatch(changes.Where(c => c.Field != ItemFieldNames.Status), row.DescriptionRevision);
        var failures = new List<string>();
        var done = false;

        if (patch is not null)
        {
            var updated = await items.UpdateAsync(actor, row.ItemKey, patch, cancellationToken);
            if (updated.Failure is { } first)
            {
                return first;
            }

            done = true;
        }

        var statusFailed = false;
        if (status is not null)
        {
            var moved = await RunStepAsync(
                () => items.TransitionAsync(actor, row.ItemKey, status.Value.GetString() ?? string.Empty, cancellationToken), done);
            if (Settle(moved.Failure, "狀態", ref done, failures) is { } stop)
            {
                return stop;
            }

            statusFailed = moved.Failure is not null;
        }

        if (row.Comment is not null)
        {
            // 狀態沒套用成功時留言多半是在說明那次狀態變更，沒有變更就不要留。
            if (statusFailed)
            {
                failures.Add("留言（狀態沒有套用成功，所以沒有加）");
            }
            else
            {
                var added = await RunStepAsync(
                    () => activity.AddCommentAsync(actor, row.ItemKey, row.Comment, replyTo: null, cancellationToken), done);
                if (Settle(added.Failure, "留言", ref done, failures) is { } stop)
                {
                    return stop;
                }
            }
        }

        return failures.Count == 0 ? string.Empty : "有幾項沒有套用：" + string.Join("；", failures) + "。其餘已經套用。";
    }

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

    private static Priority ParsePriority(string name) => Enum.Parse<Priority>(name, ignoreCase: true);

    private static DateOnly? ParseDate(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null ? null : DateOnly.ParseExact(value.GetString()!, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
