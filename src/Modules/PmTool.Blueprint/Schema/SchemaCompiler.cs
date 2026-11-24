using System.Collections.Frozen;
using System.Text.Json;

namespace PmTool.Blueprint.Schema;

/// <summary>
/// 把 Schema 文件編成 <see cref="SchemaNode"/>，同時檢查是不是只用了支援的子集。
/// 子集以外的東西一律在載入時拒絕：這樣同一份 Schema 才能直接拿去約束語言模型的輸出，
/// 也不會出現「標準驗證器會擋、我們卻放行」的規則。
/// </summary>
/// <remarks>
/// 這個類別只活在一次 Load 裡面；欄位是編譯過程的暫存，載入完就丟掉。
/// </remarks>
internal sealed class SchemaCompiler
{
    private const string DefinitionPrefix = "#/$defs/";

    private static readonly FrozenDictionary<string, SchemaType> TypeNames = new Dictionary<string, SchemaType>
    {
        ["object"] = SchemaType.Object,
        ["array"] = SchemaType.Array,
        ["string"] = SchemaType.String,
        ["integer"] = SchemaType.Integer,
        ["number"] = SchemaType.Number,
        ["boolean"] = SchemaType.Boolean,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private readonly FrozenDictionary<string, SchemaDocument> _documents;
    private readonly Dictionary<string, SchemaNode> _compiled = new(StringComparer.Ordinal);

    // 正在編譯中的引用目標，依進入的順序。再次遇到其中一個就是繞成一圈。
    private readonly List<string> _inProgress = [];

    private SchemaCompiler(FrozenDictionary<string, SchemaDocument> documents) => _documents = documents;

    /// <summary>回傳每份文件根節點編好的結果，以 $id 為 key。呼叫前 $id 必須已經確定不重複。</summary>
    public static FrozenDictionary<string, SchemaNode> CompileAll(IReadOnlyList<SchemaDocument> documents)
    {
        // 所有文件都建好索引之後才解析 $ref，引用後面才出現的文件也找得到。
        var compiler = new SchemaCompiler(documents.ToFrozenDictionary(document => document.Id, StringComparer.Ordinal));

        // 沒被引用的定義也要編譯一次，整份 Schema 才算檢查過。
        foreach (var document in documents)
        {
            foreach (var name in document.DefinitionNames())
            {
                compiler.CompileTarget(document, name);
            }
        }

        return documents.ToFrozenDictionary(
            document => document.Id,
            document => compiler.CompileTarget(document, definitionName: null),
            StringComparer.Ordinal);
    }

    /// <summary>編譯可以被 $ref 引用的目標：文件的根節點，或根節點 $defs 底下的一個定義。</summary>
    private SchemaNode CompileTarget(SchemaDocument document, string? definitionName)
    {
        var location = definitionName is null
            ? SchemaLocation.RootOf(document.Id)
            : SchemaLocation.RootOf(document.Id).Child(SchemaKeywords.Defs).Child(definitionName);
        var key = location.ToString();
        if (_compiled.TryGetValue(key, out var compiled))
        {
            return compiled;
        }

        // 不允許繞圈（包括經過 properties 的遞迴）：驗證的深度因此由 Schema 決定，和 instance 多深無關。
        var cycleStart = _inProgress.IndexOf(key);
        if (cycleStart >= 0)
        {
            var cycle = string.Join(" → ", [.. _inProgress.Skip(cycleStart), key]);
            throw new NotSupportedException($"$ref 繞成一圈：{cycle}。這個子集不支援遞迴的 Schema。");
        }

        _inProgress.Add(key);
        var element = definitionName is null
            ? document.Root
            : document.Root.GetProperty(SchemaKeywords.Defs).GetProperty(definitionName);
        var node = CompileSchema(element, location, isDocumentRoot: definitionName is null);
        _inProgress.RemoveAt(_inProgress.Count - 1);
        _compiled.Add(key, node);
        return node;
    }

    private SchemaNode CompileSchema(JsonElement element, SchemaLocation location, bool isDocumentRoot)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Unsupported(location, "Schema 必須是 JSON 物件（不支援 true、false 這種布林形式）。");
        }

        var keywords = ReadKeywords(element, location, isDocumentRoot);
        if (keywords.TryGetValue(SchemaKeywords.Ref, out var reference))
        {
            // 2020-12 與更早的草案對 $ref 旁邊的關鍵字解讀不同，只允許單獨使用就沒有歧義。
            if (keywords.Count > 1)
            {
                throw Unsupported(location, "$ref 不能和其他檢查用的關鍵字寫在一起（只能加 title、description 這類註記）。");
            }

            return ResolveRef(reference, location);
        }

        var type = ReadType(keywords, location);
        CheckKeywordsFitType(keywords, type, location);
        var properties = ReadProperties(keywords, location);
        return new SchemaNode(
            type,
            properties.ToFrozenDictionary(StringComparer.Ordinal),
            [.. properties.Select(property => property.Key)],
            ReadRequired(keywords, properties, location),
            ReadItems(keywords, location),
            ReadEnum(keywords, location),
            ReadConst(keywords, location));
    }

    private static Dictionary<string, JsonElement> ReadKeywords(
        JsonElement element, SchemaLocation location, bool isDocumentRoot)
    {
        var keywords = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            var name = property.Name;
            if (SchemaKeywords.Checked.Contains(name))
            {
                keywords.Add(name, property.Value);
            }
            else if (SchemaKeywords.RootOnly.Contains(name))
            {
                if (!isDocumentRoot)
                {
                    throw Unsupported(location, $"「{name}」只能寫在文件的最上層。");
                }

                // $id 與 $defs 的內容由 SchemaDocument 檢查，這裡只剩 $schema。
                if (name == SchemaKeywords.SchemaDialect)
                {
                    RequireString(property, location);
                }
            }
            else if (SchemaKeywords.TextAnnotations.Contains(name))
            {
                RequireString(property, location);
            }
            else
            {
                throw Unsupported(location, $"不支援的關鍵字「{name}」。");
            }
        }

        return keywords;
    }

    private static void RequireString(JsonProperty property, SchemaLocation location)
    {
        if (property.Value.ValueKind != JsonValueKind.String)
        {
            throw Unsupported(location, $"「{property.Name}」的值必須是字串。");
        }
    }

    private static SchemaType? ReadType(Dictionary<string, JsonElement> keywords, SchemaLocation location)
    {
        if (!keywords.TryGetValue(SchemaKeywords.Type, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String && TypeNames.TryGetValue(value.GetString()!, out var type)
            ? type
            : throw Unsupported(location, "type 只能是 object、array、string、integer、number、boolean 其中一個字串。");
    }

    private static void CheckKeywordsFitType(
        Dictionary<string, JsonElement> keywords, SchemaType? type, SchemaLocation location)
    {
        var misplaced = SchemaKeywords.ObjectOnly.FirstOrDefault(keyword => type != SchemaType.Object && keywords.ContainsKey(keyword));
        if (misplaced is not null)
        {
            throw Unsupported(location, $"{misplaced} 只能用在 type 是 object 的地方。");
        }

        if (type != SchemaType.Array && keywords.ContainsKey(SchemaKeywords.Items))
        {
            throw Unsupported(location, "items 只能用在 type 是 array 的地方。");
        }

        if (type != SchemaType.Object)
        {
            return;
        }

        // 拿去約束語言模型輸出時，物件不能多出任何沒定義的屬性；寫明 false 讓兩邊的規則一致。
        if (!keywords.TryGetValue(SchemaKeywords.AdditionalProperties, out var additional))
        {
            throw Unsupported(location, "type 是 object 的地方必須寫 additionalProperties: false。");
        }

        if (additional.ValueKind != JsonValueKind.False)
        {
            throw Unsupported(location, "additionalProperties 只能是 false。");
        }
    }

    private List<KeyValuePair<string, SchemaNode>> ReadProperties(
        Dictionary<string, JsonElement> keywords, SchemaLocation location)
    {
        if (!keywords.TryGetValue(SchemaKeywords.Properties, out var value))
        {
            return [];
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Unsupported(location, "properties 必須是 JSON 物件。");
        }

        var propertiesLocation = location.Child(SchemaKeywords.Properties);
        return
        [
            .. value.EnumerateObject().Select(property => KeyValuePair.Create(
                property.Name,
                CompileSchema(property.Value, propertiesLocation.Child(property.Name), isDocumentRoot: false))),
        ];
    }

    private static string[] ReadRequired(
        Dictionary<string, JsonElement> keywords,
        List<KeyValuePair<string, SchemaNode>> properties,
        SchemaLocation location)
    {
        if (!keywords.TryGetValue(SchemaKeywords.Required, out var value))
        {
            return [];
        }

        if (value.ValueKind != JsonValueKind.Array || value.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
        {
            throw Unsupported(location, "required 必須是字串的陣列。");
        }

        string[] names = [.. value.EnumerateArray().Select(item => item.GetString()!)];
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length)
        {
            throw Unsupported(location, "required 裡有重複的名稱。");
        }

        // additionalProperties 一定是 false，沒定義在 properties 的屬性永遠不能出現，這種 required 不可能滿足。
        var undefined = names.FirstOrDefault(name => !properties.Exists(property => property.Key == name));
        return undefined is null
            ? names
            : throw Unsupported(location, $"required 的「{undefined}」沒有定義在 properties 裡，這個條件永遠無法滿足。");
    }

    private SchemaNode? ReadItems(Dictionary<string, JsonElement> keywords, SchemaLocation location) =>
        keywords.TryGetValue(SchemaKeywords.Items, out var value)
            ? CompileSchema(value, location.Child(SchemaKeywords.Items), isDocumentRoot: false)
            : null;

    private static ScalarValue[]? ReadEnum(Dictionary<string, JsonElement> keywords, SchemaLocation location)
    {
        if (!keywords.TryGetValue(SchemaKeywords.Enum, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
        {
            throw Unsupported(location, "enum 必須是至少有一個值的陣列。");
        }

        return [.. value.EnumerateArray().Select(item => ReadScalar(item, SchemaKeywords.Enum, location))];
    }

    private static ScalarValue? ReadConst(Dictionary<string, JsonElement> keywords, SchemaLocation location) =>
        keywords.TryGetValue(SchemaKeywords.Const, out var value)
            ? ReadScalar(value, SchemaKeywords.Const, location)
            : null;

    private static ScalarValue ReadScalar(JsonElement value, string keyword, SchemaLocation location) =>
        ScalarValue.TryRead(value, out var scalar)
            ? scalar
            : throw Unsupported(location, $"{keyword} 的值只能是字串、有限的數字或布林值。");

    private SchemaNode ResolveRef(JsonElement value, SchemaLocation location)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw Unsupported(location, "$ref 必須是字串。");
        }

        var reference = value.GetString()!;
        if (reference.StartsWith(DefinitionPrefix, StringComparison.Ordinal))
        {
            return ResolveDefinition(_documents[location.DocumentId], reference, location);
        }

        if (reference.Length == 0 || reference.Contains('#', StringComparison.Ordinal))
        {
            throw UnsupportedRefForm(reference, location);
        }

        if (reference == location.DocumentId)
        {
            throw Unsupported(location, $"$ref「{reference}」指向自己所在的文件；同一份文件裡請用 #/$defs/<名稱>。");
        }

        return _documents.TryGetValue(reference, out var target)
            ? CompileTarget(target, definitionName: null)
            : throw Unsupported(location, $"$ref「{reference}」找不到：沒有載入這個 $id 的文件。");
    }

    private SchemaNode ResolveDefinition(SchemaDocument document, string reference, SchemaLocation location)
    {
        var name = reference[DefinitionPrefix.Length..];

        // 名稱裡的 /、~、% 要照 JSON Pointer 與 URI 的規則解碼；這個子集不處理，直接拒絕，免得和標準的解讀不同。
        if (name.Length == 0 || name.AsSpan().IndexOfAny("/~%") >= 0)
        {
            throw UnsupportedRefForm(reference, location);
        }

        return document.HasDefinition(name)
            ? CompileTarget(document, name)
            : throw Unsupported(location, $"$ref「{reference}」找不到：這份文件的 $defs 裡沒有「{name}」。");
    }

    private static NotSupportedException UnsupportedRefForm(string reference, SchemaLocation location) =>
        Unsupported(location, $"$ref「{reference}」的寫法不支援；只能是 #/$defs/<名稱>，或另一份已載入文件的 $id。");

    private static NotSupportedException Unsupported(SchemaLocation location, string reason) =>
        new($"Schema 位置 {location}：{reason}");
}
