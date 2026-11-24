namespace PmTool.Cli.Agent;

/// <summary>一則文字太長時切成好幾則（伺服器一則最多收 <c>MaxTextDelta</c> 個字）。本機代理與主機 worker 的回報共用。</summary>
internal static class EventSplitting
{
    public static IEnumerable<AgentEvent> SplitText(AgentEvent agentEvent, int maxTextDelta)
    {
        if (agentEvent is not TextEvent text || text.Delta.Length <= maxTextDelta)
        {
            return [agentEvent];
        }

        return Enumerable.Range(0, (text.Delta.Length + maxTextDelta - 1) / maxTextDelta)
            .Select(index => (AgentEvent)new TextEvent(SafeSlice(text.Delta, index * maxTextDelta, maxTextDelta)));
    }

    /// <summary>不能把一個代理對（emoji）切成兩半。</summary>
    private static string SafeSlice(string text, int start, int length)
    {
        var end = Math.Min(text.Length, start + length);
        var begin = start > 0 && char.IsLowSurrogate(text[start]) ? start - 1 : start;
        if (end < text.Length && char.IsLowSurrogate(text[end]))
        {
            end--;
        }

        return text[begin..end];
    }
}
