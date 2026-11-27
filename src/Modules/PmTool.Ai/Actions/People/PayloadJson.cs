using System.Text.Json;
using PmTool.Abstractions;

namespace PmTool.Ai.Actions.People;

/// <summary>
/// 動作建議的參數怎麼讀：工具收到的是模型寫的 JSON，一律不信任。不認得的屬性要明講（拼錯名稱時模型才知道，而不是被默默丟掉），
/// 每個問題一個細項、路徑像 <c>changes[2].email</c>，一次回完。人員與附件兩個處理器共用。
/// </summary>
internal static class PayloadJson
{
    /// <summary>讀 <c>changes</c> 陣列；<c>reason</c> 由工具放在草稿上，這裡容許它出現但不使用。</summary>
    public static IReadOnlyList<JsonElement>? Changes(JsonElement payload, List<FailureDetail> issues)
    {
        Unknown(payload, ["changes", "reason"], "", issues);
        if (!payload.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
        {
            issues.Add(Issue("changes", "changes 要是陣列。"));
            return null;
        }

        var count = changes.GetArrayLength();
        if (count is < 1 or > AiRules.MaxActionSteps)
        {
            issues.Add(Issue("changes", $"一份建議要有 1 到 {AiRules.MaxActionSteps} 筆，現在有 {count} 筆。"));
            return null;
        }

        return [.. changes.EnumerateArray().Select(element => element.Clone())];
    }

    /// <summary>屬性名稱不在允許的清單裡就記一筆問題（<paramref name="extra"/> 是補充說明）。</summary>
    public static void Unknown(JsonElement element, IReadOnlyCollection<string> allowed, string path, List<FailureDetail> issues, string? extra = null)
    {
        foreach (var property in element.EnumerateObject().Where(p => !allowed.Contains(p.Name)))
        {
            var at = path.Length == 0 ? property.Name : $"{path}.{property.Name}";
            issues.Add(Issue(at, $"不認得的屬性：{property.Name}。{extra}".TrimEnd()));
        }
    }

    /// <summary>
    /// 讀字串屬性。沒給或是 null 回 null；型別不對記問題、回 null。<paramref name="required"/> 時沒給也算問題。
    /// 去掉前後空白；擋控制字元（NUL 與換行存不進資料庫或會讓畫面錯亂）。
    /// </summary>
    public static string? Text(JsonElement element, string name, string path, List<FailureDetail> issues, bool required = false)
    {
        var at = $"{path}.{name}";
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            if (required)
            {
                issues.Add(Issue(at, $"缺少 {name}。"));
            }

            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            issues.Add(Issue(at, $"{name} 要是字串。"));
            return null;
        }

        var text = value.GetString()!.Trim();
        if (text.Any(char.IsControl))
        {
            issues.Add(Issue(at, $"{name} 不能含有換行、NUL 或其他控制字元。"));
            return null;
        }

        if (required && text.Length == 0)
        {
            issues.Add(Issue(at, $"{name} 不能是空的。"));
            return null;
        }

        return text;
    }

    /// <summary>零寬字元、方向覆寫這類看不見的格式字元（名稱用，身分模組自己也會擋，這裡先擋讓模型早點知道）。</summary>
    public static bool HasInvisibleCharacters(string text) =>
        text.EnumerateRunes().Any(rune => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(rune.Value)
            is System.Globalization.UnicodeCategory.Format
            or System.Globalization.UnicodeCategory.LineSeparator
            or System.Globalization.UnicodeCategory.ParagraphSeparator);

    public static FailureDetail Issue(string path, string message) => new(AiErrorCodes.FieldInvalid, path, message);

    public static Failure Invalid(string summary, IReadOnlyList<FailureDetail> issues) =>
        new(FailureKind.Invalid, AiErrorCodes.FieldInvalid, summary) { Details = issues };
}
