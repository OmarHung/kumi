using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using PmTool.Blueprint.Model;

namespace PmTool.Blueprint;

/// <summary>
/// 把模型寫回文件的形式（JSON）。存進資料庫與回給用戶端的藍圖都用這一份，
/// 所以命名規則要和讀進來時一樣：屬性是 camelCase，列舉是 snake_case，沒寫的屬性不出現。
/// </summary>
public static class BlueprintSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        // IsSelect、IsTerminal 這類算出來的屬性不是文件的一部分，寫出去會過不了結構檢查。
        IgnoreReadOnlyProperties = true,

        // 預設會把中文寫成跳脫序列；文件是給人看也給機器看的，照原樣寫。
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
    };

    public static string ToJson(ProjectBlueprint blueprint) => JsonSerializer.Serialize(blueprint, Options);

    public static string ToJson(ChangeSetRequest changeSet) => JsonSerializer.Serialize(changeSet, Options);

    /// <summary>同一份文件，給要把它放進另一個 JSON 回應裡的呼叫端用。</summary>
    public static JsonElement ToElement(ProjectBlueprint blueprint)
    {
        using var document = JsonDocument.Parse(ToJson(blueprint));
        return document.RootElement.Clone();
    }
}
