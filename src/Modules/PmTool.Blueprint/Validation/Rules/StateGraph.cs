using PmTool.Blueprint.Model;

namespace PmTool.Blueprint.Validation.Rules;

/// <summary>
/// 一個流程的轉換圖。只收兩端都是這個流程的狀態、而且不是自己轉到自己的轉換：
/// 不合規的轉換已經另外回報，不能讓它們替狀態製造出「走得到」或「有出路」的假象。
/// </summary>
internal sealed class StateGraph
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _exits;

    private StateGraph(IReadOnlyDictionary<string, IReadOnlyList<string>> exits) => _exits = exits;

    public static StateGraph Build(IReadOnlyList<WorkflowTransition> transitions, IReadOnlySet<string> states)
    {
        var exits = transitions
            .Where(t => t.From != t.To && states.Contains(t.From) && states.Contains(t.To))
            .GroupBy(t => t.From, t => t.To, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.ToArray(), StringComparer.Ordinal);
        return new StateGraph(exits);
    }

    public bool HasExit(string state) => _exits.ContainsKey(state);

    /// <summary>廣度優先走一次。用佇列不用遞迴，幾萬個狀態串成一條也不會把堆疊用完；走過的不再走，所以繞圈與重複的邊都會停。</summary>
    public IReadOnlySet<string> ReachableFrom(string start)
    {
        var reached = new HashSet<string>(StringComparer.Ordinal) { start };
        var pending = new Queue<string>();
        pending.Enqueue(start);
        while (pending.TryDequeue(out var state))
        {
            if (!_exits.TryGetValue(state, out var targets))
            {
                continue;
            }

            foreach (var target in targets)
            {
                if (reached.Add(target))
                {
                    pending.Enqueue(target);
                }
            }
        }

        return reached;
    }
}
