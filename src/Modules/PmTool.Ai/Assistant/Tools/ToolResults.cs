using System.Text.Json;
using PmTool.Abstractions;

namespace PmTool.Ai.Assistant.Tools;

/// <summary>工具回給模型的 JSON：成功 <c>{"ok":true,…}</c>，預期中的失敗 <c>{"ok":false,"error":…,"issues":[…]}</c>（第 5.3 節）。</summary>
internal static class ToolResults
{
    private const int TruncationReserve = 300;

    public static string Ok(object payload)
    {
        var values = new Dictionary<string, object?> { ["ok"] = true };
        foreach (var property in JsonSerializer.SerializeToElement(payload, AssistantJson.Options).EnumerateObject())
        {
            values[property.Name] = property.Value;
        }

        return AssistantJson.Serialize(values);
    }

    public static string Error(string error) => AssistantJson.Serialize(new { ok = false, error });

    public static string ErrorWithIssues(string error, IReadOnlyList<FailureDetail> issues) =>
        AssistantJson.Serialize(new
        {
            ok = false,
            error,
            issues = issues.Select(issue => new { code = issue.Code, path = issue.Path, message = issue.Message }),
        });

    /// <summary>其他失敗照 <see cref="Failure.Message"/>，有細項就一起帶。</summary>
    public static string FromFailure(Failure failure) =>
        failure.Details.Count == 0 ? Error(failure.Message) : ErrorWithIssues(failure.Message, failure.Details);

    /// <summary>
    /// 結果太長時只留前面一段並註明：整段塞給模型只會把對話撐爆。截斷後仍是合法的 JSON、保留原本的 ok，
    /// 回覆的迴圈才讀得出成功或失敗。
    /// </summary>
    public static string Limit(string json)
    {
        if (json.Length <= AiRules.MaxToolResultLength)
        {
            return json;
        }

        return AssistantJson.Serialize(new
        {
            ok = IsOk(json),
            truncated = true,
            note = $"結果太長（{json.Length} 個字元），只留下前面 {AiRules.MaxToolResultLength - TruncationReserve} 個字元。",
            partial = AiText.Cut(json, AiRules.MaxToolResultLength - TruncationReserve),
        });
    }

    private static bool IsOk(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
