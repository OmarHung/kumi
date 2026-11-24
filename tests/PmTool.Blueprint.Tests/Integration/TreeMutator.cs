using System.Text.Json.Nodes;

namespace PmTool.Blueprint.Tests.Integration;

/// <summary>模糊測試用：把一棵文件的樹隨機改壞一處。亂數由呼叫端給，種子固定就能重現。</summary>
internal static class TreeMutator
{
    private static readonly string[] WildTexts =
    [
        string.Empty, "x", "state", "delivery", "due_date", "Bad Key", "blueprint/v0", "waiting", "TWD", "12", "true", "null", "~",
        "冒號: 井號 # 減號 - 引號 \" 反斜線 \\", "tab\tand\nnewline", "https://example.com/hook", "ftp://example.com",
    ];

    private static readonly string[] WildKeys = ["extra", "key", "name", "type", "workflow", "fields", "to", "baseVersion"];

    /// <summary>隨機挑樹上的一個位置，換成亂七八糟的值、刪掉、換成別處的子樹、複製陣列元素，或把屬性改名。</summary>
    public static void Mutate(JsonObject root, Random random)
    {
        var slots = Slots(root);
        var slot = slots[random.Next(slots.Count)];
        switch (random.Next(5))
        {
            case 0:
                slot.Set(WildValue(random));
                break;
            case 1:
                slot.Remove();
                break;
            case 2:
                slot.Set(slots[random.Next(slots.Count)].Get()?.DeepClone());
                break;
            case 3 when slot.Get() is JsonArray { Count: > 0 } array:
                array.Add(array[random.Next(array.Count)]?.DeepClone());
                break;
            case 4 when slot.Parent is JsonObject parent:
                var value = slot.Get()?.DeepClone();
                slot.Remove();
                parent[WildKeys[random.Next(WildKeys.Length)]] = value;
                break;
            default:
                slot.Set(WildValue(random));
                break;
        }
    }

    private static JsonNode? WildValue(Random random) => random.Next(12) switch
    {
        0 => null,
        1 => JsonValue.Create(random.Next(2) == 0),
        2 => JsonValue.Create(random.Next(-3, 50)),
        3 => JsonValue.Create(12.5),
        4 => JsonValue.Create(1e30),
        5 => JsonValue.Create(long.MaxValue),
        6 => new JsonArray(),
        7 => new JsonObject(),
        8 => new JsonArray(WildValue(random), WildValue(random)),
        9 => new JsonObject { ["key"] = WildTexts[random.Next(WildTexts.Length)] },
        _ => JsonValue.Create(WildTexts[random.Next(WildTexts.Length)]),
    };

    /// <summary>樹上每一個可以改的位置：物件的屬性與陣列的元素。根本身不算，文件一定要有個根。</summary>
    private static List<Slot> Slots(JsonObject root)
    {
        var slots = new List<Slot>();
        var pending = new Stack<JsonNode>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            if (node is JsonObject obj)
            {
                slots.AddRange(obj.Select(property => new Slot(obj, property.Key, -1)));
            }
            else if (node is JsonArray array)
            {
                slots.AddRange(Enumerable.Range(0, array.Count).Select(index => new Slot(array, null, index)));
            }

            foreach (var child in Children(node))
            {
                pending.Push(child);
            }
        }

        return slots;
    }

    private static IEnumerable<JsonNode> Children(JsonNode node) => node switch
    {
        JsonObject obj => obj.Select(property => property.Value).OfType<JsonNode>(),
        JsonArray array => array.OfType<JsonNode>(),
        _ => [],
    };

    private sealed record Slot(JsonNode Parent, string? Name, int Index)
    {
        public JsonNode? Get() => Parent is JsonObject obj ? obj[Name!] : ((JsonArray)Parent)[Index];

        public void Set(JsonNode? value)
        {
            if (Parent is JsonObject obj)
            {
                obj[Name!] = value;
            }
            else
            {
                ((JsonArray)Parent)[Index] = value;
            }
        }

        public void Remove()
        {
            if (Parent is JsonObject obj)
            {
                obj.Remove(Name!);
            }
            else
            {
                ((JsonArray)Parent).RemoveAt(Index);
            }
        }
    }
}
