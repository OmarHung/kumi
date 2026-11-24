using PmTool.Blueprint.Model;

namespace PmTool.Blueprint.Validation.Rules;

/// <summary>
/// 找出類型上層關係裡「把圈閉合」的那幾條邊。
/// 依文件順序從每個還沒走過的類型出發做深度優先走訪；某條邊指回目前路徑上的類型，就是它把圈閉合。
/// 只回報這些邊，而不是圈上的每個類型：拿掉它們圖就沒有圈了，使用者照著改就好。
/// </summary>
internal static class ParentCycles
{
    private const byte Unvisited = 0;
    private const byte OnPath = 1;
    private const byte Finished = 2;

    /// <returns>(類型在 itemTypes 的位置, 上層在它 parents 的位置)。</returns>
    public static IReadOnlySet<(int Type, int Parent)> FindClosingEdges(
        IReadOnlyList<ItemType> itemTypes, IReadOnlyDictionary<string, int> firstPositions)
    {
        var marks = new byte[itemTypes.Count];
        var closing = new HashSet<(int Type, int Parent)>();
        for (var root = 0; root < itemTypes.Count; root++)
        {
            // 重複 key 的類型不是圖上的點：別人用 key 指到它時，指到的是第一個。
            if (marks[root] == Unvisited && firstPositions[itemTypes[root].Key] == root)
            {
                Walk(root, itemTypes, firstPositions, marks, closing);
            }
        }

        return closing;
    }

    /// <summary>用自己的堆疊模擬遞迴，兩萬層的上層關係也不會把呼叫堆疊用完。</summary>
    private static void Walk(
        int root,
        IReadOnlyList<ItemType> itemTypes,
        IReadOnlyDictionary<string, int> firstPositions,
        byte[] marks,
        HashSet<(int Type, int Parent)> closing)
    {
        var path = new Stack<(int Type, int NextParent)>();
        marks[root] = OnPath;
        path.Push((root, 0));
        while (path.TryPop(out var frame))
        {
            var parents = itemTypes[frame.Type].Parents;
            if (frame.NextParent >= parents.Count)
            {
                marks[frame.Type] = Finished;
                continue;
            }

            path.Push((frame.Type, frame.NextParent + 1));
            if (!firstPositions.TryGetValue(parents[frame.NextParent], out var target))
            {
                continue;
            }

            if (marks[target] == OnPath)
            {
                closing.Add((frame.Type, frame.NextParent));
            }
            else if (marks[target] == Unvisited)
            {
                marks[target] = OnPath;
                path.Push((target, 0));
            }
        }
    }
}
