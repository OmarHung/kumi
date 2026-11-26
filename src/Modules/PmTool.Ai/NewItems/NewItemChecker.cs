using System.Text.Json;
using System.Text.RegularExpressions;
using PmTool.Abstractions;
using PmTool.Ai.Items;
using PmTool.Blueprint.Model;
using PmTool.Work;

namespace PmTool.Ai.NewItems;

/// <summary>
/// 新增項目建議的檢查（internal/HANDOFF-ai-new-items.md 4.2）：只擋明顯不對的、讓模型看得懂為什麼；選項、必填這些細節套用時由工作引擎判斷。
/// 每個問題一個細項（路徑 <c>items[2].type</c>），一次回給模型，它才能一次修好。值的規則和修改建議共用 <see cref="ItemValueRules"/>。
/// </summary>
internal static partial class NewItemChecker
{
    private const int MaxRefLength = 20;

    /// <summary>
    /// <paramref name="existingParents"/> 是已經查到的既有上層（以 <see cref="NewItemDraft.ParentKey"/> 為鍵，不分大小寫）；
    /// <paramref name="assignable"/> 是現在能被指派的人（專案成員且帳號沒有停用，<see cref="NewItemMembers.ActiveAsync"/>）；
    /// <paramref name="membersText"/> 是不認得的負責人時附上的成員名單（呼叫的人先用 <see cref="HasUnknownAssignee"/> 判斷要不要列）。
    /// </summary>
    public static Result<IReadOnlyList<NewItemDraft>> Check(
        IReadOnlyList<NewItemDraft>? items,
        ProjectDetail project,
        IReadOnlyDictionary<string, WorkItem> existingParents,
        IReadOnlySet<Guid> assignable,
        string? membersText)
    {
        var drafts = items ?? [];
        var issues = new List<FailureDetail>();
        if (drafts.Count is 0 or > AiRules.MaxNewItemsPerProposal)
        {
            issues.Add(Issue("items", $"一份建議要有 1 到 {AiRules.MaxNewItemsPerProposal} 個項目。"));
        }

        var cleaned = new List<NewItemDraft>();
        var refTypes = new Dictionary<string, string?>(StringComparer.Ordinal);
        var allRefs = drafts.Select(d => d?.Ref?.Trim()).Where(r => !string.IsNullOrEmpty(r)).ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i < drafts.Count && i < AiRules.MaxNewItemsPerProposal; i++)
        {
            if (drafts[i] is null)
            {
                issues.Add(Issue($"items[{i}]", "這一項是空的。"));
                continue;
            }

            var found = new ItemCheck(project, existingParents, assignable, membersText, refTypes, allRefs, $"items[{i}]", issues);
            cleaned.Add(found.Run(drafts[i]));
        }

        return issues.Count == 0
            ? cleaned
            : new Failure(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "新增項目的建議不合規定，沒有建立。") { Details = issues };
    }

    /// <summary>有項目的負責人不能被指派（不是專案成員、或帳號已停用；錯誤訊息要附成員名單）。</summary>
    public static bool HasUnknownAssignee(IReadOnlyList<NewItemDraft>? items, IReadOnlySet<Guid> assignable) =>
        (items ?? []).Any(d => d?.AssigneeId is { } id && !assignable.Contains(id));

    internal static FailureDetail Issue(string path, string message) => new(AiErrorCodes.FieldInvalid, path, message);

    [GeneratedRegex("^[a-z0-9_-]{1,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex RefPattern();

    /// <summary>檢查一個項目；把問題記進共用的清單、回傳整理過的項目。</summary>
    private sealed class ItemCheck(
        ProjectDetail project,
        IReadOnlyDictionary<string, WorkItem> existingParents,
        IReadOnlySet<Guid> assignable,
        string? membersText,
        Dictionary<string, string?> refTypes,
        HashSet<string?> allRefs,
        string path,
        List<FailureDetail> issues)
    {
        public NewItemDraft Run(NewItemDraft draft)
        {
            var type = CheckType(draft.Type);
            var title = draft.Title?.Trim() ?? string.Empty;
            Problem("title", ItemValueRules.IsPlainText(title, allowLineBreaks: false) ? ItemValueRules.CheckTitle(title) : "文字不能含有 NUL 或其他控制字元。");
            var description = CheckDescription(draft.Description);
            var priority = draft.Priority?.Trim().ToLowerInvariant();
            if (priority is not null)
            {
                Problem("priority", ItemValueRules.CheckPriority(priority));
            }

            CheckDate("startDate", draft.StartDate);
            CheckDate("dueDate", draft.DueDate);
            if (draft.Estimate is { } estimate)
            {
                Problem("estimate", ItemValueRules.CheckEstimate(estimate));
            }

            CheckAssignee(draft.AssigneeId);
            var fields = CheckFields(draft.Fields, type);
            var parentKey = CheckParents(draft, type);
            var reference = CheckRef(draft.Ref, type);
            return new NewItemDraft(draft.Type?.Trim() ?? string.Empty, title)
            {
                Ref = reference,
                Description = description,
                AssigneeId = draft.AssigneeId,
                Priority = priority,
                StartDate = draft.StartDate?.Trim(),
                DueDate = draft.DueDate?.Trim(),
                Estimate = draft.Estimate,
                ParentKey = parentKey,
                ParentRef = string.IsNullOrWhiteSpace(draft.ParentRef) ? null : draft.ParentRef.Trim(),
                Fields = fields,
            };
        }

        private ItemType? CheckType(string? key)
        {
            var type = project.Blueprint.ItemTypes.FirstOrDefault(t => t.Key == key?.Trim());
            if (type is null)
            {
                var available = string.Join("、", project.Blueprint.ItemTypes.Select(t => $"{t.Key}（{t.Name}）"));
                Problem("type", $"這個專案沒有項目類型「{key}」。可用的類型：{available}。");
            }

            return type;
        }

        private string? CheckDescription(string? description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return null;
            }

            var text = description.Trim();
            Problem("description", !ItemValueRules.IsPlainText(text, allowLineBreaks: true) ? "文字不能含有 NUL 或其他控制字元。" : ItemValueRules.CheckDescriptionLength(text));
            return text;
        }

        private void CheckDate(string name, string? value)
        {
            if (value is not null && !ItemValueRules.IsDate(value.Trim()))
            {
                Problem(name, $"{name} 要寫成 yyyy-MM-dd。");
            }
        }

        private void CheckAssignee(Guid? assigneeId)
        {
            if (assigneeId is { } id && !assignable.Contains(id))
            {
                Problem("assigneeId", "assigneeId 不是這個專案的成員。" + membersText);
            }
        }

        private Dictionary<string, JsonElement> CheckFields(IReadOnlyDictionary<string, JsonElement>? fields, ItemType? type)
        {
            var cleaned = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var (key, value) in fields ?? new Dictionary<string, JsonElement>())
            {
                var problem = CheckField(key, value, type);
                Problem($"fields.{key}", problem);
                if (problem is null && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                {
                    cleaned[key] = value.Clone();
                }
            }

            return cleaned;
        }

        private string? CheckField(string key, JsonElement value, ItemType? type)
        {
            if (type is null)
            {
                return null;
            }

            var definition = type.Fields.Contains(key) ? project.Blueprint.Fields.FirstOrDefault(f => f.Key == key) : null;
            if (definition is null)
            {
                return $"這個類型沒有自訂欄位「{key}」。這個類型有的欄位：{(type.Fields.Count == 0 ? "（沒有）" : string.Join("、", type.Fields))}。";
            }

            if (!ItemValueRules.IsPlainText(value, allowLineBreaks: true))
            {
                return "文字不能含有 NUL 或其他控制字元。";
            }

            return value.ValueKind == JsonValueKind.Null ? null : ItemValueRules.CheckCustomValue(key, definition, value);
        }

        private string? CheckRef(string? reference, ItemType? type)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                return null;
            }

            var text = reference.Trim();
            if (!RefPattern().IsMatch(text))
            {
                Problem("ref", $"ref 要是 1～{MaxRefLength} 個小寫英文、數字、- 或 _。");
            }
            else if (!refTypes.TryAdd(text, type?.Key))
            {
                Problem("ref", $"ref「{text}」重複了，同一份建議裡每個 ref 只能出現一次。");
            }

            return text;
        }

        // 回傳整理過的 parentKey（既有項目的正式編號）。parentRef 要排在前面，所以 ref 在檢查完這一項之後才登記。
        private string? CheckParents(NewItemDraft draft, ItemType? type)
        {
            var parentKey = string.IsNullOrWhiteSpace(draft.ParentKey) ? null : draft.ParentKey.Trim();
            var parentRef = string.IsNullOrWhiteSpace(draft.ParentRef) ? null : draft.ParentRef.Trim();
            if (parentKey is not null && parentRef is not null)
            {
                Problem("parentRef", "parentKey 與 parentRef 只能給一個。");
                return parentKey;
            }

            if (parentKey is not null)
            {
                return CheckParentKey(parentKey, type);
            }

            if (parentRef is not null)
            {
                CheckParentRef(parentRef, type);
            }

            return null;
        }

        private string CheckParentKey(string parentKey, ItemType? type)
        {
            if (!existingParents.TryGetValue(parentKey, out var parent))
            {
                Problem("parentKey", $"找不到上層項目「{parentKey}」；上層項目要在這個專案裡、你看得到。");
                return parentKey;
            }

            Problem("parentKey", ParentTypeProblem(type, parent.Type));
            return parent.Key;
        }

        private void CheckParentRef(string parentRef, ItemType? type)
        {
            if (!refTypes.TryGetValue(parentRef, out var parentType))
            {
                var later = allRefs.Contains(parentRef);
                Problem("parentRef", later
                    ? $"parentRef「{parentRef}」的項目排在這一項後面；上層要排在前面。"
                    : $"找不到 ref 是「{parentRef}」的項目；parentRef 要是排在前面的項目的 ref。");
                return;
            }

            Problem("parentRef", ParentTypeProblem(type, parentType));
        }

        // 規則照 PmTool.Work/Items/ParentRules.cs：空的 Parents 是只能放在最上層。
        private string? ParentTypeProblem(ItemType? type, string? parentTypeKey)
        {
            if (type is null || parentTypeKey is null || type.Parents.Contains(parentTypeKey))
            {
                return null;
            }

            if (type.Parents.Count == 0)
            {
                return $"「{type.Name}」只能放在最上層，不能掛在其他項目底下。";
            }

            var allowed = type.Parents.Select(p => $"「{project.Blueprint.ItemTypes.FirstOrDefault(t => t.Key == p)?.Name ?? p}」");
            var parentName = project.Blueprint.ItemTypes.FirstOrDefault(t => t.Key == parentTypeKey)?.Name ?? parentTypeKey;
            return $"「{type.Name}」不能掛在「{parentName}」底下，只能掛在{string.Join("、", allowed)}底下。";
        }

        private void Problem(string field, string? message)
        {
            if (message is not null)
            {
                issues.Add(Issue($"{path}.{field}", message));
            }
        }
    }
}
