using System.Globalization;
using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Blueprint.Model;
using PmTool.Work;

namespace PmTool.Ai.Items;

/// <summary>
/// 修改建議的檢查（internal/HANDOFF-ai-items.md 4.1）：只擋明顯不對的、讓模型看得懂為什麼；欄位值的細節（選項、人員是否存在等）
/// 套用時由工作引擎判斷。每個問題一個細項，一次回給模型，它才能一次修好。
/// </summary>
internal static class ItemChangeChecker
{
    private const string DateFormat = "yyyy-MM-dd";

    private static readonly string[] PriorityNames = ["none", "low", "medium", "high", "urgent"];

    public static Result<IReadOnlyList<ItemFieldChange>> Check(
        IReadOnlyList<ItemFieldChange> changes, string? comment, string? descriptionRevision, WorkItem item, ProjectDetail project)
    {
        var issues = new List<FailureDetail>();
        var cleaned = new List<ItemFieldChange>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (changes.Count > AiRules.MaxItemProposalChanges)
        {
            issues.Add(Issue("changes", $"一份建議最多改 {AiRules.MaxItemProposalChanges} 個欄位。"));
        }

        for (var i = 0; i < changes.Count && i < AiRules.MaxItemProposalChanges; i++)
        {
            var path = $"changes[{i}]";
            var change = changes[i];
            if (change is null || string.IsNullOrWhiteSpace(change.Field))
            {
                issues.Add(Issue(path, "缺少欄位名稱 field。"));
            }
            else if (!seen.Add(change.Field))
            {
                issues.Add(Issue(path, $"欄位 {change.Field} 重複了，同一個欄位只能出現一次。"));
            }
            else if (CheckValue(change, item, project) is { } problem)
            {
                issues.Add(Issue(path, problem));
            }
            else
            {
                cleaned.Add(new ItemFieldChange(change.Field, change.Value.Clone()));
            }
        }

        if (cleaned.Any(c => c.Field == ItemFieldNames.Description) && CheckDescriptionBase(descriptionRevision, item) is { } baseProblem)
        {
            issues.Add(Issue("descriptionRevision", baseProblem));
        }

        if (CheckComment(comment) is { } commentProblem)
        {
            issues.Add(Issue("comment", commentProblem));
        }

        if (issues.Count == 0 && changes.Count == 0 && string.IsNullOrWhiteSpace(comment))
        {
            issues.Add(Issue("changes", "至少要有一個要改的欄位，或一則留言。"));
        }

        return issues.Count == 0
            ? cleaned
            : new Failure(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "修改建議不合規定，沒有建立。")
            {
                Details = issues,
            };
    }

    /// <summary>留言去掉前後空白；空白的當作沒有。</summary>
    public static string? NormalizeComment(string? comment) => string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

    private static string? CheckValue(ItemFieldChange change, WorkItem item, ProjectDetail project)
    {
        var (field, value) = (change.Field, change.Value);
        if (!IsPlainText(value, allowLineBreaks: field != ItemFieldNames.Title))
        {
            return "文字不能含有 NUL 或其他控制字元。";
        }

        return field switch
        {
            ItemFieldNames.Title => CheckTitle(value),
            ItemFieldNames.Description => CheckDescription(value),
            ItemFieldNames.Status => CheckStatus(value, item, project),
            ItemFieldNames.Assignee => CheckAssignee(value, project),
            ItemFieldNames.Priority => CheckPriority(value),
            ItemFieldNames.StartDate or ItemFieldNames.DueDate => CheckDate(value),
            ItemFieldNames.Estimate => CheckEstimate(value),
            _ when field.StartsWith(ItemFieldNames.CustomPrefix, StringComparison.Ordinal) => CheckCustom(field, value, item, project),
            _ => $"沒有欄位 {field}。可以改的欄位：title、description、status、assignee、priority、startDate、dueDate、estimate、fields.<自訂欄位的 key>。",
        };
    }

    private static string? CheckTitle(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            return "title 要是不空白的字串。";
        }

        return value.GetString()!.Trim().Length > WorkRules.MaxTitleLength ? $"title 最長 {WorkRules.MaxTitleLength} 個字元。" : null;
    }

    private static string? CheckDescription(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String when value.GetString()!.Length > WorkRules.MaxDescriptionLength =>
            $"description 最長 {WorkRules.MaxDescriptionLength} 個字元。",
        JsonValueKind.String => null,
        _ => "description 要是字串（Markdown），null 是清掉。",
    };

    /// <summary>改說明的前提：給了讀項目時的說明版本，而且現在的說明不會長到模型讀不完整。</summary>
    public static string? CheckDescriptionBase(string? descriptionRevision, WorkItem item)
    {
        if (string.IsNullOrWhiteSpace(descriptionRevision) || descriptionRevision.Length > 64 || descriptionRevision.Any(char.IsControl))
        {
            return "改說明要帶 descriptionRevision（get_item 回的那個）。請先用 get_item 重讀項目，再帶著它提出。";
        }

        return item.Description?.Length > ItemProposalLimits.MaxEditableDescriptionLength
            ? $"這個項目的說明超過 {ItemProposalLimits.MaxEditableDescriptionLength} 個字元，不能用建議改說明，請改用留言。"
            : null;
    }

    private static string? CheckStatus(JsonElement value, WorkItem item, ProjectDetail project)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return "status 要是狀態的 key。";
        }

        var states = StatesOf(item, project);
        var key = value.GetString();
        return states.Any(state => state.Key == key)
            ? null
            : $"這個項目的流程沒有狀態「{key}」。可用的狀態：{string.Join("、", states.Select(state => state.Key))}。";
    }

    private static IReadOnlyList<WorkflowState> StatesOf(WorkItem item, ProjectDetail project)
    {
        var type = project.Blueprint.ItemTypes.FirstOrDefault(t => t.Key == item.Type);
        return project.Blueprint.Workflows.FirstOrDefault(w => w.Key == type?.Workflow)?.States ?? [];
    }

    private static string? CheckAssignee(JsonElement value, ProjectDetail project)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String || !Guid.TryParse(value.GetString(), out var personId))
        {
            return "assignee 要是人員編號（GUID），null 是清掉負責人。";
        }

        return project.Members.Any(member => member.PersonId == personId) ? null : "assignee 不是這個專案的成員。";
    }

    private static string? CheckPriority(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && PriorityNames.Contains(value.GetString())
            ? null
            : $"priority 要是 {string.Join("、", PriorityNames)} 其中之一。";

    private static string? CheckDate(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String when IsDate(value.GetString()) => null,
        _ => "日期要寫成 yyyy-MM-dd，null 是清掉。",
    };

    private static string? CheckEstimate(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.Number when value.TryGetDecimal(out var number) && number >= 0 && number <= WorkRules.MaxNumberMagnitude => null,
        _ => "estimate 要是不小於 0 的數字，null 是清掉。",
    };

    private static string? CheckCustom(string field, JsonElement value, WorkItem item, ProjectDetail project)
    {
        var key = field[ItemFieldNames.CustomPrefix.Length..];
        var type = project.Blueprint.ItemTypes.FirstOrDefault(t => t.Key == item.Type);
        var definition = type is null || !type.Fields.Contains(key)
            ? null
            : project.Blueprint.Fields.FirstOrDefault(f => f.Key == key);
        if (definition is null)
        {
            return $"這個項目的類型沒有自訂欄位「{key}」。";
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (!KindFits(definition.Type, value))
        {
            return $"欄位「{key}」是{definition.Type}，值的 JSON 種類不對。";
        }

        return OverLimit(value) is { } over ? $"欄位「{key}」的值太大：{over}" : null;
    }

    private static string? OverLimit(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String when value.GetString()!.Length > ItemProposalLimits.MaxCustomStringLength =>
            $"文字最長 {ItemProposalLimits.MaxCustomStringLength} 個字元。",
        JsonValueKind.Array when value.GetArrayLength() > ItemProposalLimits.MaxCustomArrayItems =>
            $"最多 {ItemProposalLimits.MaxCustomArrayItems} 項。",
        JsonValueKind.Array => value.EnumerateArray().Select(OverLimit).FirstOrDefault(problem => problem is not null),
        _ => null,
    };

    private static bool KindFits(FieldType type, JsonElement value) => type switch
    {
        FieldType.Text or FieldType.LongText or FieldType.Url or FieldType.SingleSelect or FieldType.Person =>
            value.ValueKind == JsonValueKind.String,
        FieldType.Date => value.ValueKind == JsonValueKind.String && IsDate(value.GetString()),
        FieldType.Number or FieldType.Currency => value.ValueKind == JsonValueKind.Number,
        FieldType.Checkbox => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        FieldType.MultiSelect => value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(e => e.ValueKind == JsonValueKind.String),
        _ => false,
    };

    private static string? CheckComment(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return null;
        }

        if (comment.Trim().Length > WorkRules.MaxCommentLength)
        {
            return $"留言最長 {WorkRules.MaxCommentLength} 個字元。";
        }

        return IsPlainText(comment, allowLineBreaks: true) ? null : "留言不能含有 NUL 或其他控制字元。";
    }

    private static bool IsDate(string? text) =>
        DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static bool IsPlainText(string text, bool allowLineBreaks) =>
        !text.Any(c => char.IsControl(c) && !(allowLineBreaks && c is '\n' or '\r' or '\t'));

    // 值裡任何一層的字串都要乾淨：自訂欄位的值可以是陣列。
    private static bool IsPlainText(JsonElement value, bool allowLineBreaks) => value.ValueKind switch
    {
        JsonValueKind.String => IsPlainText(value.GetString()!, allowLineBreaks),
        JsonValueKind.Array => value.EnumerateArray().All(e => IsPlainText(e, allowLineBreaks)),
        JsonValueKind.Object => value.EnumerateObject().All(p => IsPlainText(p.Name, allowLineBreaks: false) && IsPlainText(p.Value, allowLineBreaks)),
        _ => true,
    };

    private static FailureDetail Issue(string path, string message) => new(AiErrorCodes.FieldInvalid, path, message);
}
