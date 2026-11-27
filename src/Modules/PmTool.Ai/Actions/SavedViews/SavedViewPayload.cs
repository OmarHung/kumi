using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Ai.Actions.SavedViews;

internal enum SavedViewAction
{
    Create,
    Update,
    Delete,
}

/// <summary>一步：<see cref="Draft"/> 是 update 合併之後的完整內容（delete 沒有）；<see cref="Previous"/> 是改之前的樣子（create 沒有）。</summary>
internal sealed record SavedViewStep(SavedViewAction Action, Guid? ViewId, SavedViewDraft? Draft, SavedView? Previous);

/// <summary>
/// 讀 propose_saved_view_changes 的參數。每個問題一個細項（路徑 <c>changes[1].layout</c>）、一次回齊。
/// update 只改有給的屬性（給 null 是清掉那個條件），其餘沿用現在的檢視：服務的 Replace 要整份，合併在這裡做。
/// </summary>
internal static class SavedViewPayload
{
    private const int MaxTextLength = 200;

    private static readonly string[] StringProperties =
        ["type", "state", "assignee", "search", "sort", "due", "priority", "cycle", "milestone", "group", "category", "project", "createdBy", "mine"];

    private static readonly string[] BoolProperties = ["open", "stale"];

    private static readonly string[] MyWorkOnly = ["category", "project", "mine"];

    private static readonly string[] ProjectOnly = ["cycle", "milestone", "createdBy", "open", "stale"];

    private static readonly HashSet<string> Known =
        new(["action", "viewId", "name", "layout", "fields", .. StringProperties, .. BoolProperties]);

    public static Result<IReadOnlyList<SavedViewStep>> Read(JsonElement payload, bool isMyWork, IReadOnlyList<SavedView> existing)
    {
        var issues = new List<FailureDetail>();
        foreach (var property in payload.EnumerateObject())
        {
            if (property.Name is not ("changes" or "projectKey" or "reason"))
            {
                issues.Add(Issue(property.Name, $"不認得的參數「{property.Name}」。"));
            }
        }

        if (!payload.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array
            || changes.GetArrayLength() is 0 or > AiRules.MaxActionSteps)
        {
            issues.Add(Issue("changes", $"changes 要是 1 到 {AiRules.MaxActionSteps} 筆的陣列。"));
            return Invalid(issues);
        }

        var steps = new List<SavedViewStep>();
        var touched = new HashSet<Guid>();
        var index = 0;
        foreach (var change in changes.EnumerateArray())
        {
            var step = ReadOne(change, $"changes[{index++}]", isMyWork, existing, touched, issues);
            if (step is not null)
            {
                steps.Add(step);
            }
        }

        var limit = isMyWork ? SavedViewRules.MaxMyWork : SavedViewRules.MaxPerProject;
        var finalCount = existing.Count + steps.Count(s => s.Action == SavedViewAction.Create) - steps.Count(s => s.Action == SavedViewAction.Delete);
        if (finalCount > limit)
        {
            issues.Add(Issue("changes", $"檢視最多 {limit} 個，這份建議做完會有 {finalCount} 個；先刪掉不用的。"));
        }

        return issues.Count == 0 ? steps : Invalid(issues);
    }

    private static SavedViewStep? ReadOne(
        JsonElement change, string path, bool isMyWork, IReadOnlyList<SavedView> existing, HashSet<Guid> touched, List<FailureDetail> issues)
    {
        if (change.ValueKind != JsonValueKind.Object)
        {
            issues.Add(Issue(path, "每一筆要是物件。"));
            return null;
        }

        var before = issues.Count;
        foreach (var property in change.EnumerateObject().Where(p => !Known.Contains(p.Name)))
        {
            issues.Add(Issue($"{path}.{property.Name}", $"不認得的屬性「{property.Name}」。"));
        }

        var action = change.TryGetProperty("action", out var a) && a.ValueKind == JsonValueKind.String
            ? a.GetString() switch
            {
                "create" => (SavedViewAction?)SavedViewAction.Create,
                "update" => SavedViewAction.Update,
                "delete" => SavedViewAction.Delete,
                _ => null,
            }
            : null;
        if (action is null)
        {
            issues.Add(Issue($"{path}.action", "action 只能是 create、update 或 delete。"));
            return null;
        }

        SavedView? previous = null;
        if (action != SavedViewAction.Create)
        {
            previous = FindExisting(change, path, existing, touched, issues);
            if (previous is null)
            {
                return null;
            }
        }

        if (action == SavedViewAction.Delete)
        {
            return issues.Count == before ? new SavedViewStep(action.Value, previous!.Id, null, previous) : null;
        }

        var draft = BuildDraft(change, path, isMyWork, previous, issues);
        return draft is null || issues.Count != before ? null : new SavedViewStep(action.Value, previous?.Id, draft, previous);
    }

    private static SavedView? FindExisting(
        JsonElement change, string path, IReadOnlyList<SavedView> existing, HashSet<Guid> touched, List<FailureDetail> issues)
    {
        if (!change.TryGetProperty("viewId", out var id) || id.ValueKind != JsonValueKind.String || !Guid.TryParse(id.GetString(), out var viewId))
        {
            issues.Add(Issue($"{path}.viewId", "update 與 delete 要給檢視的 viewId（list_saved_views 回的）。"));
            return null;
        }

        var view = existing.FirstOrDefault(v => v.Id == viewId);
        if (view is null)
        {
            issues.Add(Issue($"{path}.viewId", "找不到這個檢視；只能改或刪自己的、這個範圍裡的檢視（list_saved_views 查得到）。"));
            return null;
        }

        if (!touched.Add(viewId))
        {
            issues.Add(Issue($"{path}.viewId", "同一個檢視在一份建議裡只能出現一次。"));
            return null;
        }

        return view;
    }

    private static SavedViewDraft? BuildDraft(JsonElement change, string path, bool isMyWork, SavedView? previous, List<FailureDetail> issues)
    {
        var name = previous?.Name;
        if (change.TryGetProperty("name", out var n))
        {
            name = n.ValueKind == JsonValueKind.String ? n.GetString()?.Trim() : null;
        }

        if (string.IsNullOrEmpty(name) || name.Length > SavedViewRules.MaxNameLength || name.Any(char.IsControl))
        {
            issues.Add(Issue($"{path}.name", $"name 要是 1 到 {SavedViewRules.MaxNameLength} 個字、沒有控制字元的名稱。"));
        }

        var layout = previous?.Layout ?? SavedViewLayout.Table;
        if (change.TryGetProperty("layout", out var l))
        {
            var parsed = l.ValueKind == JsonValueKind.String
                ? l.GetString() switch
                {
                    "table" => (SavedViewLayout?)SavedViewLayout.Table,
                    "board" => SavedViewLayout.Board,
                    "calendar" => SavedViewLayout.Calendar,
                    "timeline" => SavedViewLayout.Timeline,
                    _ => null,
                }
                : null;
            if (parsed is { } value)
            {
                layout = value;
            }
            else
            {
                issues.Add(Issue($"{path}.layout", "layout 只能是 table、board、calendar 或 timeline。"));
            }
        }

        var query = ReadQuery(change, path, isMyWork, previous?.Query ?? EmptyQuery, issues);
        return name is null ? null : new SavedViewDraft(name, layout, query);
    }

    private static readonly SavedViewQuery EmptyQuery = new(null, null, null, null, null, null, null);

    private static SavedViewQuery ReadQuery(JsonElement change, string path, bool isMyWork, SavedViewQuery start, List<FailureDetail> issues)
    {
        var query = start;
        foreach (var property in change.EnumerateObject())
        {
            var name = property.Name;
            var scopeIssue = (isMyWork ? ProjectOnly : MyWorkOnly).Contains(name);
            if (scopeIssue)
            {
                issues.Add(Issue($"{path}.{name}", isMyWork ? $"「我的工作」的檢視沒有「{name}」這個條件。" : $"專案的檢視沒有「{name}」這個條件。"));
                continue;
            }

            if (StringProperties.Contains(name))
            {
                query = SetString(query, name, property.Value, $"{path}.{name}", issues);
            }
            else if (BoolProperties.Contains(name))
            {
                query = SetBool(query, name, property.Value, $"{path}.{name}", issues);
            }
            else if (name == "fields")
            {
                query = query with { Fields = ReadFields(property.Value, $"{path}.fields", issues) };
            }
        }

        return query;
    }

    private static SavedViewQuery SetString(SavedViewQuery query, string name, JsonElement value, string path, List<FailureDetail> issues)
    {
        string? text = null;
        if (value.ValueKind == JsonValueKind.String)
        {
            text = value.GetString()?.Trim();
        }
        else if (value.ValueKind != JsonValueKind.Null)
        {
            issues.Add(Issue(path, $"{name} 要是字串，或 null 表示清掉這個條件。"));
            return query;
        }

        if (text is { Length: > MaxTextLength } || text?.Any(char.IsControl) == true)
        {
            issues.Add(Issue(path, $"{name} 最多 {MaxTextLength} 個字、不能有控制字元。"));
            return query;
        }

        text = string.IsNullOrEmpty(text) ? null : text;
        return name switch
        {
            "type" => query with { Type = text },
            "state" => query with { State = text },
            "assignee" => query with { Assignee = text },
            "search" => query with { Search = text },
            "sort" => query with { Sort = text },
            "due" => query with { Due = text },
            "priority" => query with { Priority = text },
            "cycle" => query with { Cycle = text },
            "milestone" => query with { Milestone = text },
            "group" => query with { Group = text },
            "category" => query with { Category = text },
            "project" => query with { Project = text },
            "createdBy" => query with { CreatedBy = text },
            _ => query with { Mine = text },
        };
    }

    private static SavedViewQuery SetBool(SavedViewQuery query, string name, JsonElement value, string path, List<FailureDetail> issues)
    {
        bool? flag = value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => null,
            JsonValueKind.Null => null,
            _ => null,
        };
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null))
        {
            issues.Add(Issue(path, $"{name} 要是 true，或 false／null 表示不開。"));
            return query;
        }

        return name == "open" ? query with { Open = flag } : query with { Stale = flag };
    }

    private static IReadOnlyDictionary<string, string>? ReadFields(JsonElement value, string path, List<FailureDetail> issues)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() > FieldFilter.MaxPerQuery)
        {
            issues.Add(Issue(path, $"fields 要是欄位 key 對到條件字串的物件，最多 {FieldFilter.MaxPerQuery} 個。"));
            return null;
        }

        var fields = new Dictionary<string, string>();
        foreach (var field in value.EnumerateObject())
        {
            var text = field.Value.ValueKind == JsonValueKind.String ? field.Value.GetString() : null;
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength || text.Any(char.IsControl) || field.Name.Any(char.IsControl))
            {
                issues.Add(Issue($"{path}.{field.Name}", "欄位的條件要是 1 到 200 個字、沒有控制字元的字串。"));
                continue;
            }

            fields[field.Name] = text.Trim();
        }

        return fields.Count == 0 ? null : fields;
    }

    private static FailureDetail Issue(string path, string message) => new(AiErrorCodes.FieldInvalid, path, message);

    private static Failure Invalid(List<FailureDetail> issues) =>
        new(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "檢視建議不合規定，沒有建立。") { Details = issues };
}
