using System.Globalization;
using System.Text.Json;
using PmTool.Api.Json;

namespace PmTool.Api.Mcp;

/// <summary>
/// 讀代理給的參數。一律不信任：缺的、型別不對、超出範圍都丟 <see cref="McpArgumentException"/>，
/// 由呼叫工具的地方轉成給代理看的錯誤。
/// </summary>
internal sealed class McpArguments(IDictionary<string, JsonElement>? values)
{
    private readonly IDictionary<string, JsonElement> _values = values ?? new Dictionary<string, JsonElement>();

    public IEnumerable<string> Names => _values.Keys;

    /// <summary>有給這個參數（包含明確給了 null：更新時 null 是「清掉」，和沒給不同）。</summary>
    public bool Has(string name) => _values.ContainsKey(name);

    public bool IsNull(string name) => _values.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.Null;

    /// <summary>這個參數給的是 JSON 字串（不是陣列、物件）。</summary>
    public bool IsString(string name) => _values.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String;

    public string RequiredString(string name) =>
        OptionalString(name) is { Length: > 0 } text ? text : throw new McpArgumentException($"缺少參數 {name}。");

    /// <summary>沒給或給 null 是 null。</summary>
    public string? OptionalString(string name)
    {
        if (!_values.TryGetValue(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : throw new McpArgumentException($"參數 {name} 要是字串。");
    }

    /// <summary>
    /// 查詢條件與識別用的字串（專案代號、項目編號、類型、狀態、游標、搜尋文字、負責人）：不能含 NUL 與其他控制字元。
    /// PostgreSQL 不收 NUL，送進模組就是 500；這些值也不會有換行。說明、留言、標題這類內文不用這個，內文可以換行。
    /// </summary>
    public string RequiredPlain(string name) =>
        OptionalPlain(name) is { Length: > 0 } text ? text : throw new McpArgumentException($"缺少參數 {name}。");

    /// <summary>沒給或給 null 是 null；其他同 <see cref="RequiredPlain"/>。</summary>
    public string? OptionalPlain(string name)
    {
        var text = OptionalString(name);
        return text is not null && text.Any(char.IsControl)
            ? throw new McpArgumentException($"參數 {name} 不能含有 NUL 或其他控制字元。")
            : text;
    }

    /// <summary>沒給或給 null 是 null；不是 true／false 就擋下（不收 "true" 這種字串，模型寫錯時才知道）。</summary>
    public bool? OptionalBool(string name)
    {
        if (!_values.TryGetValue(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw new McpArgumentException($"參數 {name} 要是 true 或 false。");
    }

    public int? OptionalInt(string name, int min, int max)
    {
        if (!_values.TryGetValue(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
        {
            throw new McpArgumentException($"參數 {name} 要是整數。");
        }

        return number >= min && number <= max
            ? number
            : throw new McpArgumentException($"參數 {name} 要在 {min} 到 {max} 之間。");
    }

    public long RequiredLong(string name)
    {
        if (!_values.TryGetValue(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number))
        {
            throw new McpArgumentException($"參數 {name} 要是整數。");
        }

        return number;
    }

    public DateOnly? OptionalDate(string name) =>
        OptionalString(name) is { } text
            ? DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : throw new McpArgumentException($"參數 {name} 的日期要寫成 YYYY-MM-DD。")
            : null;

    public TEnum? OptionalEnum<TEnum>(string name)
        where TEnum : struct, Enum
    {
        var text = OptionalString(name);
        if (text is null)
        {
            return null;
        }

        return SnakeCaseEnums.TryParse<TEnum>(text, out var value)
            ? value
            : throw new McpArgumentException($"參數 {name} 要是 {string.Join("、", SnakeCaseEnums.AllNames<TEnum>())} 其中之一。");
    }

    /// <summary>一個 JSON 物件。模型有時會把物件包成字串送來，字串裡是 JSON 物件的話照收。回傳物件的原文。</summary>
    public string RequiredObjectText(string name)
    {
        if (!_values.TryGetValue(name, out var value))
        {
            throw new McpArgumentException($"缺少參數 {name}，它要是一個 JSON 物件。");
        }

        return ObjectOf(value) is { } element
            ? element.GetRawText()
            : throw new McpArgumentException($"參數 {name} 要是一個 JSON 物件。");
    }

    /// <summary>自訂欄位這類「欄位的 key → 值」的物件；沒給是空的。值是 JSON 的 null 表示清掉（只有更新有意義）。</summary>
    public IReadOnlyDictionary<string, JsonElement> OptionalObject(string name)
    {
        if (!_values.TryGetValue(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return new Dictionary<string, JsonElement>();
        }

        var element = ObjectOf(value) ?? throw new McpArgumentException($"參數 {name} 要是一個 JSON 物件。");
        return element.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone());
    }

    /// <summary>一個 JSON 陣列（模型有時包成字串，照收）。沒給或給 null 是 null；不是陣列丟 <see cref="McpArgumentException"/>。元素已複製。</summary>
    public IReadOnlyList<JsonElement>? OptionalArray(string name)
    {
        if (!_values.TryGetValue(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var element = value;
        if (value.ValueKind == JsonValueKind.String)
        {
            try
            {
                using var document = JsonDocument.Parse(value.GetString() ?? string.Empty);
                element = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                throw new McpArgumentException($"參數 {name} 要是一個 JSON 陣列。");
            }
        }

        return element.ValueKind == JsonValueKind.Array
            ? [.. element.EnumerateArray().Select(item => item.Clone())]
            : throw new McpArgumentException($"參數 {name} 要是一個 JSON 陣列。");
    }

    private static JsonElement? ObjectOf(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            return value;
        }

        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } text)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
