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
    public static Result<IReadOnlyList<ItemFieldChange>> Check(
        IReadOnlyList<ItemFieldChange> changes,
        string? comment,
        string? descriptionRevision,
        WorkItem item,
        ProjectDetail project,
        ItemChangeFacts? facts = null,
        Guid? replyTo = null)
    {
        facts ??= ItemChangeFacts.None;
        var context = new CheckContext(item, project, facts, EffectiveTypeKey(changes, item, project));
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
            else if (CheckValue(change, context, changes) is { } problem)
            {
                issues.Add(Issue(path, problem));
            }
            else
            {
                cleaned.Add(Normalize(change, facts));
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

        if (CheckReply(comment, replyTo, facts) is { } replyProblem)
        {
            issues.Add(Issue("replyTo", replyProblem));
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

    /// <summary>檢查時一個項目要用到的東西；<paramref name="TypeKey"/> 是套用後項目的類型（有改類型時是新的）。</summary>
    private sealed record CheckContext(WorkItem Item, ProjectDetail Project, ItemChangeFacts Facts, string TypeKey);

    // 有改類型時，狀態與自訂欄位照新的類型檢查（套用時先改類型）。類型不存在的錯誤由 type 那一項回報。
    private static string EffectiveTypeKey(IReadOnlyList<ItemFieldChange> changes, WorkItem item, ProjectDetail project) =>
        changes.FirstOrDefault(c => c is not null && c.Field == ItemFieldNames.Type) is { Value.ValueKind: JsonValueKind.String } change
            && ItemStructureChecks.TypeOf(change.Value.GetString(), project) is { } type
                ? type.Key
                : item.Type;

    // 上層存成正式的編號（模型可能寫小寫），套用與卡片都用同一個寫法。
    private static ItemFieldChange Normalize(ItemFieldChange change, ItemChangeFacts facts) =>
        change.Field == ItemFieldNames.ParentKey && facts.NewParent is { } parent && change.Value.ValueKind == JsonValueKind.String
            ? new ItemFieldChange(change.Field, JsonSerializer.SerializeToElement(parent.Key))
            : new ItemFieldChange(change.Field, change.Value.Clone());

    private static string? CheckReply(string? comment, Guid? replyTo, ItemChangeFacts facts)
    {
        if (replyTo is null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(comment) ? "replyTo 是回覆留言用的，要一起給 comment。" : facts.ReplyProblem;
    }

    private static string? CheckValue(ItemFieldChange change, CheckContext context, IReadOnlyList<ItemFieldChange> all)
    {
        var (item, project, facts) = (context.Item, context.Project, context.Facts);
        var (field, value) = (change.Field, change.Value);
        if (!ItemValueRules.IsPlainText(value, allowLineBreaks: field != ItemFieldNames.Title))
        {
            return "文字不能含有 NUL 或其他控制字元。";
        }

        return field switch
        {
            ItemFieldNames.Title => CheckTitle(value),
            ItemFieldNames.Description => CheckDescription(value),
            ItemFieldNames.Status => CheckStatus(value, context),
            ItemFieldNames.Assignee => CheckAssignee(value, project),
            ItemFieldNames.Priority => CheckPriority(value),
            ItemFieldNames.StartDate or ItemFieldNames.DueDate => CheckDate(value),
            ItemFieldNames.Estimate => CheckEstimate(value),
            ItemFieldNames.Following => CheckFollowing(value),
            ItemFieldNames.Type => ItemStructureChecks.CheckType(
                value, project, all.Any(c => c is not null && c.Field == ItemFieldNames.ParentKey), facts),
            ItemFieldNames.ParentKey => ItemStructureChecks.CheckParent(
                value, item, ItemStructureChecks.TypeOf(context.TypeKey, project), project, facts),
            ItemFieldNames.CycleId => ItemStructureChecks.CheckPlanningId(value, field, "週期", facts.Cycles),
            ItemFieldNames.MilestoneId => ItemStructureChecks.CheckPlanningId(value, field, "里程碑", facts.Milestones),
            _ when field.StartsWith(ItemFieldNames.CustomPrefix, StringComparison.Ordinal) => CheckCustom(field, value, context),
            _ => $"沒有欄位 {field}。可以改的欄位：title、description、status、assignee、priority、startDate、dueDate、estimate、type、parentKey、cycleId、milestoneId、following（true 關注、false 取消關注）、fields.<自訂欄位的 key>。",
        };
    }

    private static string? CheckTitle(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? ItemValueRules.CheckTitle(value.GetString()) : ItemValueRules.CheckTitle(null);

    private static string? CheckDescription(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => ItemValueRules.CheckDescriptionLength(value.GetString()!),
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

    private static string? CheckStatus(JsonElement value, CheckContext context)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return "status 要是狀態的 key。";
        }

        var states = StatesOf(context.TypeKey, context.Project);
        var key = value.GetString();
        return states.Any(state => state.Key == key)
            ? null
            : $"這個項目的流程沒有狀態「{key}」。可用的狀態：{string.Join("、", states.Select(state => state.Key))}。";
    }

    private static IReadOnlyList<WorkflowState> StatesOf(string typeKey, ProjectDetail project)
    {
        var type = project.Blueprint.ItemTypes.FirstOrDefault(t => t.Key == typeKey);
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
        value.ValueKind == JsonValueKind.String ? ItemValueRules.CheckPriority(value.GetString()) : ItemValueRules.CheckPriority(null);

    private static string? CheckDate(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String when ItemValueRules.IsDate(value.GetString()) => null,
        _ => ItemValueRules.DateMessage,
    };

    private static string? CheckFollowing(JsonElement value) =>
        value.ValueKind is JsonValueKind.True or JsonValueKind.False ? null : "following 要是 true（關注）或 false（取消關注）。";

    private static string? CheckEstimate(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.Number when value.TryGetDecimal(out var number) => ItemValueRules.CheckEstimate(number),
        _ => ItemValueRules.EstimateMessage,
    };

    private static string? CheckCustom(string field, JsonElement value, CheckContext context)
    {
        var (project, typeKey) = (context.Project, context.TypeKey);
        var key = field[ItemFieldNames.CustomPrefix.Length..];
        var type = project.Blueprint.ItemTypes.FirstOrDefault(t => t.Key == typeKey);
        var definition = type is null || !type.Fields.Contains(key)
            ? null
            : project.Blueprint.Fields.FirstOrDefault(f => f.Key == key);
        if (definition is null)
        {
            return $"這個項目的類型（{typeKey}）沒有自訂欄位「{key}」。";
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return ItemValueRules.CheckCustomValue(key, definition, value);
    }

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

        return ItemValueRules.IsPlainText(comment, allowLineBreaks: true) ? null : "留言不能含有 NUL 或其他控制字元。";
    }

    private static FailureDetail Issue(string path, string message) => new(AiErrorCodes.FieldInvalid, path, message);
}
