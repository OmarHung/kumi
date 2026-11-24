using PmTool.Blueprint.Model;
using static PmTool.Blueprint.IssueText;

namespace PmTool.Blueprint.Validation.Rules;

/// <summary>
/// 規則 1：key 的格式與重複。依 Schema 的屬性順序走：metadata（含 basedOn.template）、fields（每個欄位接著它的選項）、
/// workflows（每個流程接著它的狀態）、itemTypes、views、automations、roles。
/// 每個 key 先查格式再查重複；重複時回報在後出現的那一個。
/// </summary>
internal static class KeyRules
{
    public static IEnumerable<ValidationIssue> Check(ProjectBlueprint blueprint)
    {
        IEnumerable<ValidationIssue>[] parts =
        [
            Metadata(blueprint.Metadata),
            Fields(blueprint.Fields),
            Workflows(blueprint.Workflows),
            Entries(blueprint.ItemTypes, t => t.Key, "/itemTypes", "類型"),
            Entries(blueprint.Views, v => v.Key, "/views", "檢視"),
            Entries(blueprint.Automations, a => a.Key, "/automations", "自動化"),
            Entries(blueprint.Roles, r => r.Key, "/roles", "角色"),
        ];
        return parts.SelectMany(part => part);
    }

    private static IEnumerable<ValidationIssue> Metadata(BlueprintMetadata metadata)
    {
        if (!StrictText.IsKey(metadata.Key))
        {
            yield return Malformed(metadata.Key, "/metadata/key");
        }

        // basedOn.template 在 Schema 裡也是 key。
        if (metadata.BasedOn is { } basedOn && !StrictText.IsKey(basedOn.Template))
        {
            yield return Malformed(basedOn.Template, "/metadata/basedOn/template");
        }
    }

    private static IEnumerable<ValidationIssue> Fields(IReadOnlyList<FieldDefinition> fields)
    {
        var seen = NewKeySet();
        for (var i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            foreach (var issue in Entry(field.Key, $"/fields/{i}/key", "欄位", seen))
            {
                yield return issue;
            }

            // 選項的 key 以欄位為範圍；非選項型欄位寫了選項也照樣檢查，options 本身的問題由規則 3 回報。
            var options = Entries(field.Options ?? [], o => o.Key, $"/fields/{i}/options", $"欄位{Quote(field.Key)}的選項");
            foreach (var issue in options)
            {
                yield return issue;
            }
        }
    }

    private static IEnumerable<ValidationIssue> Workflows(IReadOnlyList<Workflow> workflows)
    {
        var seen = NewKeySet();
        for (var i = 0; i < workflows.Count; i++)
        {
            var workflow = workflows[i];
            foreach (var issue in Entry(workflow.Key, $"/workflows/{i}/key", "流程", seen))
            {
                yield return issue;
            }

            // 狀態的 key 以流程為範圍。
            var states = Entries(workflow.States, s => s.Key, $"/workflows/{i}/states", $"流程{Quote(workflow.Key)}的狀態");
            foreach (var issue in states)
            {
                yield return issue;
            }
        }
    }

    private static IEnumerable<ValidationIssue> Entries<T>(
        IReadOnlyList<T> items, Func<T, string> keyOf, string listPath, string subject)
    {
        var seen = NewKeySet();
        for (var i = 0; i < items.Count; i++)
        {
            foreach (var issue in Entry(keyOf(items[i]), $"{listPath}/{i}/key", subject, seen))
            {
                yield return issue;
            }
        }
    }

    private static IEnumerable<ValidationIssue> Entry(string key, string path, string subject, HashSet<string> seen)
    {
        if (!StrictText.IsKey(key))
        {
            yield return Malformed(key, path);
        }

        if (!seen.Add(key))
        {
            yield return new ValidationIssue(
                IssueCodes.KeyDuplicate, path, $"{subject}的 key {Quote(key)}重複了，同一個清單裡的 key 不能相同。");
        }
    }

    private static ValidationIssue Malformed(string key, string path) =>
        new(
            IssueCodes.KeyFormat,
            path,
            $"{Quote(key)}不是合法的 key：要以小寫英文字母開頭，只能有小寫字母、數字與底線，長度 2 到 40 個字元。");

    private static HashSet<string> NewKeySet() => new(StringComparer.Ordinal);
}
