using PmTool.Abstractions;
using PmTool.Blueprint.Model;
using PmTool.Work;

namespace PmTool.Ai.ItemFinding;

/// <summary>檢查 <see cref="ItemFindQuery"/>，轉成工作引擎的條件。模型給的值一律不信任，不合格回 <see cref="AiErrorCodes.FieldInvalid"/>。</summary>
internal static class ItemFindArguments
{
    private static readonly StateCategory[] OpenCategories = [StateCategory.Backlog, StateCategory.NotStarted, StateCategory.InProgress];

    public static Failure Invalid(string path, string message) =>
        new(FailureKind.Invalid, AiErrorCodes.FieldInvalid, message) { Details = [new FailureDetail(AiErrorCodes.FieldInvalid, path, message)] };

    /// <summary>不看人員（人員是否在組織裡由呼叫的人另外確認）的檢查。成功時回傳要查的條件（Limit 已加一，多要一筆才知道還有沒有）。</summary>
    public static Result<WorkItemQuery> ToWorkQuery(ItemFindQuery query, Guid actorPersonId, DateOnly today)
    {
        if (query.Limit < 1 || query.Limit > AiRules.MaxFindItemsLimit)
        {
            return Invalid("limit", $"筆數要在 1 到 {AiRules.MaxFindItemsLimit} 之間。");
        }

        if (query.Assignee == ItemFindAssignee.Person && query.AssigneeId is null)
        {
            return Invalid("assignee", "要查某個人負責的項目，請給人員編號。");
        }

        if (!IsPlainText(query.ProjectKey))
        {
            return Invalid("projectKey", "專案代號不能有控制字元。");
        }

        // 控制字元要在去掉前後空白之前檢查：結尾的換行不能靠 Trim 躲過去。
        if (!IsPlainText(query.Text))
        {
            return Invalid("text", "關鍵字不能有控制字元。");
        }

        var text = query.Text?.Trim();
        if (text is { Length: > AiRules.MaxFindItemsTextLength })
        {
            return Invalid("text", $"關鍵字最長 {AiRules.MaxFindItemsTextLength} 個字。");
        }

        var categories = ParseCategories(query.StateCategories);
        if (categories is null)
        {
            return Invalid("stateCategory", "狀態類別只能是 backlog、not_started、in_progress、done 或 canceled。");
        }

        if (query.Overdue && categories.Any(c => c is StateCategory.Done or StateCategory.Canceled))
        {
            return Invalid("overdue", "overdue 只看還沒結束的項目，不能和 done、canceled 一起用。");
        }

        if (query.DueFrom is { } from && query.DueTo is { } to && from > to)
        {
            return Invalid("dueFrom", "dueFrom 不能晚於 dueTo。");
        }

        // 逾期是「到期日在今天之前」：今天到期的還沒逾期。和給的 DueTo 取較早的那天。
        var dueTo = query.Overdue ? Earlier(query.DueTo, today.AddDays(-1)) : query.DueTo;
        return new WorkItemQuery
        {
            AssigneeId = query.Assignee switch
            {
                ItemFindAssignee.Me => actorPersonId,
                ItemFindAssignee.Person => query.AssigneeId,
                _ => null,
            },
            OnlyUnassigned = query.Assignee == ItemFindAssignee.Nobody,
            CreatedBy = query.CreatedBy,
            StateCategories = categories.Count == 0 ? OpenCategories : categories,
            DueFrom = query.DueFrom,
            DueTo = dueTo,
            Search = string.IsNullOrEmpty(text) ? null : text,
            SortBy = WorkItemSortField.DueDate,
            Limit = query.Limit + 1,
        };
    }

    public static string CategoryName(StateCategory category) => category switch
    {
        StateCategory.Backlog => "backlog",
        StateCategory.NotStarted => "not_started",
        StateCategory.InProgress => "in_progress",
        StateCategory.Done => "done",
        _ => "canceled",
    };

    public static string PriorityName(Priority priority) => priority.ToString().ToLowerInvariant();

    private static List<StateCategory>? ParseCategories(IReadOnlyList<string> names)
    {
        var parsed = new List<StateCategory>();
        foreach (var name in names)
        {
            StateCategory? category = name switch
            {
                "backlog" => StateCategory.Backlog,
                "not_started" => StateCategory.NotStarted,
                "in_progress" => StateCategory.InProgress,
                "done" => StateCategory.Done,
                "canceled" => StateCategory.Canceled,
                _ => null,
            };
            if (category is not { } value)
            {
                return null;
            }

            if (!parsed.Contains(value))
            {
                parsed.Add(value);
            }
        }

        return parsed;
    }

    private static bool IsPlainText(string? text) => text is null || !text.Any(char.IsControl);

    private static DateOnly Earlier(DateOnly? first, DateOnly second) => first is { } value && value < second ? value : second;
}
