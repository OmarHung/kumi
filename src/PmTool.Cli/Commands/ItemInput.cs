using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Cli.Api;

namespace PmTool.Cli.Commands;

/// <summary>--set 欄位=值 與 --assignee 轉成請求內文。</summary>
internal static class ItemInput
{
    private const string AssigneeMe = "@me";
    private const string AssigneeNone = "none";

    /// <summary>這幾個是項目本身的屬性，值就是文字（estimate 例外，是數字）；其他的名稱都是藍圖自訂的欄位。</summary>
    private static readonly HashSet<string> CoreTextProperties = ["priority", "startDate", "dueDate", "parentKey", "description"];

    private const string EstimateProperty = "estimate";

    /// <summary>有沒有藍圖自訂的欄位：有才需要去查藍圖，知道欄位的型別。</summary>
    public static bool HasCustomFields(IEnumerable<string> settings) =>
        settings.Select(KeyOf).Any(key => key is not null && key != EstimateProperty && !CoreTextProperties.Contains(key));

    /// <summary>
    /// 把 --set 的每一項放進內文。項目本身的屬性照固定的規則；自訂欄位依藍圖裡欄位的型別轉：
    /// 文字、長文字、日期、選項、人員、網址一律是字串，數字與金額才是數字，勾選只收 true／false，多選是清單。
    /// 藍圖裡找不到這個欄位時（或藍圖取不到），退回舊的猜法：JSON 的數字、true、null、陣列就照 JSON，否則是文字。
    /// 值是 null 就是清掉。
    /// </summary>
    public static void ApplySettings(JsonObject body, IEnumerable<string> settings, BlueprintNames names)
    {
        JsonObject? fields = null;
        foreach (var setting in settings)
        {
            var key = KeyOf(setting) ?? throw CliException.Usage($"--set 要寫成「欄位=值」，收到：{setting}。");
            var raw = setting[(setting.IndexOf('=') + 1)..];
            if (key == EstimateProperty)
            {
                body[key] = raw == "null" ? null : ParseNumber(key, raw);
            }
            else if (CoreTextProperties.Contains(key))
            {
                body[key] = raw == "null" ? null : JsonValue.Create(raw);
            }
            else
            {
                (fields ??= [])[key] = raw == "null" ? null : ParseFieldValue(key, raw, names.Field(key)?.Text("type"));
            }
        }

        if (fields is not null)
        {
            body["fields"] = fields;
        }
    }

    private static string? KeyOf(string setting)
    {
        var separator = setting.IndexOf('=');
        return separator > 0 ? setting[..separator].Trim() : null;
    }

    private static JsonNode? ParseFieldValue(string key, string raw, string? fieldType) => fieldType switch
    {
        "text" or "long_text" or "url" or "date" or "single_select" or "person" => JsonValue.Create(raw),
        "number" or "currency" => ParseNumber(key, raw),
        "checkbox" => ParseCheckbox(key, raw),
        "multi_select" => ParseOptions(key, raw),
        _ => ParseValue(raw),
    };

    public static JsonNode? ParseValue(string raw)
    {
        try
        {
            return JsonNode.Parse(raw);
        }
        catch (JsonException)
        {
            return JsonValue.Create(raw);
        }
    }

    private static JsonNode ParseNumber(string key, string raw) =>
        decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? JsonValue.Create(number)
            : throw CliException.Usage($"{key} 要是數字，收到：{raw}。");

    private static JsonNode ParseCheckbox(string key, string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "true" => JsonValue.Create(true),
        "false" => JsonValue.Create(false),
        _ => throw CliException.Usage($"{key} 是勾選欄位，只收 true 或 false，收到：{raw}。"),
    };

    /// <summary>多選：寫成 JSON 的清單（["a","b"]），或用逗號隔開（a,b）。</summary>
    private static JsonNode ParseOptions(string key, string raw)
    {
        if (raw.TrimStart().StartsWith('['))
        {
            return ParseValue(raw) is JsonArray list && list.All(item => item is JsonValue value && value.TryGetValue<string>(out _))
                ? list
                : throw CliException.Usage($"{key} 是多選欄位，要寫成 [\"選項\",...] 或用逗號隔開的選項，收到：{raw}。");
        }

        return new JsonArray([.. raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(option => (JsonNode?)JsonValue.Create(option))]);
    }

    /// <summary>--created-by 的值轉成查詢參數：@me 交給伺服器換成自己，人員編號照用，其他當 Email 查。</summary>
    public static async Task<string> ResolveCreatedByAsync(CommandCall call, string value)
    {
        if (value == AssigneeMe || value.Equals("me", StringComparison.OrdinalIgnoreCase))
        {
            return "me";
        }

        if (Guid.TryParse(value, out var personId))
        {
            return personId.ToString();
        }

        return (await ResolveAssigneeAsync(call, value, allowNone: false))?.GetValue<string>()
            ?? throw CliException.Usage($"--created-by 要是 @me、人員編號或同事的 email，收到：{value}。");
    }

    /// <summary>--assignee 的值轉成人員編號；update 才允許 none（清掉負責人）。</summary>
    public static async Task<JsonNode?> ResolveAssigneeAsync(CommandCall call, string value, bool allowNone)
    {
        if (allowNone && value.Equals(AssigneeNone, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (value == AssigneeMe)
        {
            var session = await call.Api.GetAsync("session", null, call.CancellationToken);
            return JsonValue.Create(session.Get("user")?.Text("personId")
                ?? throw new CliException(ExitCodes.Error, "伺服器沒有說目前登入的是誰。"));
        }

        var people = await call.Api.GetAsync("people", null, call.CancellationToken);
        var match = people.Items().FirstOrDefault(person =>
            string.Equals(person.Text("email"), value, StringComparison.OrdinalIgnoreCase));
        return JsonValue.Create(match.ValueKind == JsonValueKind.Undefined
            ? throw CliException.Usage($"找不到 email 是 {value} 的人。負責人可以寫 @me 或同事的 email{(allowNone ? "，清掉寫 none" : string.Empty)}。")
            : match.Text("personId"));
    }
}
