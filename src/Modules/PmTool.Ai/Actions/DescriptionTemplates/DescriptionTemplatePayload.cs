using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Ai.Actions.DescriptionTemplates;

internal enum TemplateAction
{
    Create,
    Update,
    Delete,
}

/// <summary><see cref="Input"/> 是 update 合併之後的完整內容（delete 沒有）；<see cref="Previous"/> 是改之前的樣子（create 沒有）。</summary>
internal sealed record TemplateStep(TemplateAction Action, Guid? TemplateId, DescriptionTemplateInput? Input, DescriptionTemplate? Previous);

/// <summary>
/// 讀 propose_description_template_changes 的參數，每個問題一個細項、一次回齊。update 只改有給的屬性，其餘沿用現在的內容
/// （服務的 Update 要整份，合併在這裡做）。內建的格式不能改、不能刪。
/// </summary>
internal static class DescriptionTemplatePayload
{
    private static readonly HashSet<string> Known = new(["action", "templateId", "name", "body", "isDefault"]);

    public static Result<IReadOnlyList<TemplateStep>> Read(JsonElement payload, IReadOnlyList<DescriptionTemplate> existing)
    {
        var issues = new List<FailureDetail>();
        foreach (var property in payload.EnumerateObject().Where(p => p.Name is not ("changes" or "reason")))
        {
            issues.Add(Issue(property.Name, $"不認得的參數「{property.Name}」。"));
        }

        if (!payload.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array
            || changes.GetArrayLength() is 0 or > AiRules.MaxActionSteps)
        {
            issues.Add(Issue("changes", $"changes 要是 1 到 {AiRules.MaxActionSteps} 筆的陣列。"));
            return Invalid(issues);
        }

        var steps = new List<TemplateStep>();
        var touched = new HashSet<Guid>();
        var index = 0;
        foreach (var change in changes.EnumerateArray())
        {
            if (ReadOne(change, $"changes[{index++}]", existing, touched, issues) is { } step)
            {
                steps.Add(step);
            }
        }

        if (issues.Count == 0)
        {
            CheckFinalSet(steps, existing, issues);
        }

        return issues.Count == 0 ? steps : Invalid(issues);
    }

    // 做完之後整組的名稱不能重複（不分大小寫，內建的也算）、自己的最多 MaxTemplatesPerOrganization 份：先在這裡擋，不用等套用一半才失敗。
    private static void CheckFinalSet(List<TemplateStep> steps, IReadOnlyList<DescriptionTemplate> existing, List<FailureDetail> issues)
    {
        var final = existing.Where(t => !t.IsBuiltIn).ToDictionary(t => t.Id, t => t.Name);
        var builtIn = existing.Where(t => t.IsBuiltIn).Select(t => t.Name).ToList();
        foreach (var step in steps.Where(s => s.Action == TemplateAction.Delete))
        {
            final.Remove(step.TemplateId!.Value.ToString());
        }

        foreach (var step in steps.Where(s => s.Action == TemplateAction.Update))
        {
            final[step.TemplateId!.Value.ToString()] = step.Input!.Name;
        }

        var created = steps.Where(s => s.Action == TemplateAction.Create).Select(s => s.Input!.Name).ToList();
        var names = final.Values.Concat(created).Concat(builtIn).ToList();
        var duplicate = names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            issues.Add(Issue("changes", $"說明格式的名稱不能重複（不分大小寫，內建的也算）：「{duplicate.Key}」。"));
        }

        if (final.Count + created.Count > ProjectDescriptionRules.MaxTemplatesPerOrganization)
        {
            issues.Add(Issue("changes", $"組織自己的說明格式最多 {ProjectDescriptionRules.MaxTemplatesPerOrganization} 份；先刪掉不用的。"));
        }
    }

    private static TemplateStep? ReadOne(
        JsonElement change, string path, IReadOnlyList<DescriptionTemplate> existing, HashSet<Guid> touched, List<FailureDetail> issues)
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
                "create" => (TemplateAction?)TemplateAction.Create,
                "update" => TemplateAction.Update,
                "delete" => TemplateAction.Delete,
                _ => null,
            }
            : null;
        if (action is null)
        {
            issues.Add(Issue($"{path}.action", "action 只能是 create、update 或 delete。"));
            return null;
        }

        DescriptionTemplate? previous = null;
        if (action != TemplateAction.Create)
        {
            previous = FindExisting(change, path, existing, touched, issues);
            if (previous is null)
            {
                return null;
            }
        }

        if (action == TemplateAction.Delete)
        {
            return issues.Count == before ? new TemplateStep(action.Value, Guid.Parse(previous!.Id), null, previous) : null;
        }

        var input = BuildInput(change, path, previous, issues);
        return input is null || issues.Count != before ? null : new TemplateStep(action.Value, previous is null ? null : Guid.Parse(previous.Id), input, previous);
    }

    private static DescriptionTemplate? FindExisting(
        JsonElement change, string path, IReadOnlyList<DescriptionTemplate> existing, HashSet<Guid> touched, List<FailureDetail> issues)
    {
        if (!change.TryGetProperty("templateId", out var id) || id.ValueKind != JsonValueKind.String)
        {
            issues.Add(Issue($"{path}.templateId", "update 與 delete 要給說明格式的 templateId（list_description_templates 回的）。"));
            return null;
        }

        var text = id.GetString();
        var template = existing.FirstOrDefault(t => string.Equals(t.Id, text, StringComparison.OrdinalIgnoreCase));
        if (template is null || !Guid.TryParse(template.Id, out var templateId))
        {
            issues.Add(Issue($"{path}.templateId", template is { IsBuiltIn: true }
                ? "內建的說明格式不能改、不能刪；要改請新增一份自己的。"
                : "找不到這份說明格式（list_description_templates 查得到）。"));
            return null;
        }

        if (!touched.Add(templateId))
        {
            issues.Add(Issue($"{path}.templateId", "同一份說明格式在一份建議裡只能出現一次。"));
            return null;
        }

        return template;
    }

    private static DescriptionTemplateInput? BuildInput(JsonElement change, string path, DescriptionTemplate? previous, List<FailureDetail> issues)
    {
        var name = previous?.Name;
        if (change.TryGetProperty("name", out var n))
        {
            name = n.ValueKind == JsonValueKind.String ? n.GetString()?.Trim() : null;
        }

        var body = previous?.Body;
        if (change.TryGetProperty("body", out var b))
        {
            body = b.ValueKind == JsonValueKind.String ? b.GetString() : null;
        }

        var isDefault = previous?.IsDefault ?? false;
        if (change.TryGetProperty("isDefault", out var d))
        {
            if (d.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                isDefault = d.GetBoolean();
            }
            else
            {
                issues.Add(Issue($"{path}.isDefault", "isDefault 要是 true 或 false。"));
            }
        }

        // 內建格式沒有自己的預設旗標以外的東西可改，所以這裡的 previous 一定是組織自己的。
        CheckName(name, path, issues);
        CheckBody(body, path, issues);
        return name is null || body is null ? null : new DescriptionTemplateInput(name, body, isDefault);
    }

    private static void CheckName(string? name, string path, List<FailureDetail> issues)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > ProjectDescriptionRules.MaxTemplateNameLength || name.Any(char.IsControl))
        {
            issues.Add(Issue($"{path}.name", $"name 要是 1 到 {ProjectDescriptionRules.MaxTemplateNameLength} 個字、沒有控制字元的名稱。"));
        }
    }

    private static void CheckBody(string? body, string path, List<FailureDetail> issues)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            issues.Add(Issue($"{path}.body", "body（說明格式的內容）不能是空白。"));
        }
        else if (body.Length > AiRules.MaxEditableProjectDescriptionLength)
        {
            issues.Add(Issue($"{path}.body", $"body 最多 {AiRules.MaxEditableProjectDescriptionLength} 個字。"));
        }
        else if (body.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
        {
            issues.Add(Issue($"{path}.body", "body 不能有換行與 Tab 以外的控制字元。"));
        }
        else if (SecretText.ContainsSecret(body))
        {
            issues.Add(Issue($"{path}.body", "body 裡有私鑰或存取權杖，不能存進說明格式。"));
        }
    }

    private static FailureDetail Issue(string path, string message) => new(AiErrorCodes.FieldInvalid, path, message);

    private static Failure Invalid(List<FailureDetail> issues) =>
        new(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "說明格式建議不合規定，沒有建立。") { Details = issues };
}
