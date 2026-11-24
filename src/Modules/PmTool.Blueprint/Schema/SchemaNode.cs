using System.Collections.Frozen;

namespace PmTool.Blueprint.Schema;

/// <summary>
/// 編譯好的 Schema 節點。單例的驗證器會在多個請求之間同時使用它，所以載入時一次建好、之後不再改變，
/// 驗證時也不再碰 JsonNode 形式的 Schema（JsonNode 的延遲初始化不是執行緒安全的）。
/// $ref 在載入時就換成被引用的節點，所以這裡沒有 $ref。
/// </summary>
/// <param name="Type">沒寫 type 時是 null，表示不限型別。</param>
/// <param name="Properties">type 是 object 時，有定義的屬性；其餘一律視為多出來的屬性。</param>
/// <param name="PropertyNames">同一批屬性依 Schema 裡的順序，給訊息列出允許的屬性用。</param>
/// <param name="Required">依 Schema 裡的順序。</param>
/// <param name="Items">type 是 array 時每個元素的 Schema；沒寫時元素不限。</param>
/// <param name="EnumValues">沒寫 enum 時是 null。</param>
/// <param name="ConstValue">沒寫 const 時是 null。</param>
internal sealed record SchemaNode(
    SchemaType? Type,
    FrozenDictionary<string, SchemaNode> Properties,
    IReadOnlyList<string> PropertyNames,
    IReadOnlyList<string> Required,
    SchemaNode? Items,
    IReadOnlyList<ScalarValue>? EnumValues,
    ScalarValue? ConstValue);
