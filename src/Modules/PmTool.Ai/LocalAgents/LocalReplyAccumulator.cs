using System.Text;
using PmTool.Ai.Assistant;
using PmTool.Ai.Assistant.Transcript;

namespace PmTool.Ai.LocalAgents;

/// <summary>
/// 把本機代理回報的進度一則一則收起來：組出給人看的段落（文字與工具依序）、之後再送給模型的 transcript、用量，
/// 並說出該送出哪些事件。transcript 只存助理寫的文字（工具只在段落裡）：本機的引擎自己跑工具，
/// 呼叫與結果的格式和 API 模型的不一樣，換成 API 模型時當作一般的助理訊息就好。
/// 不是執行緒安全的（只在背景的回覆迴圈裡用）。
/// </summary>
internal sealed class LocalReplyAccumulator : IReplyRecorder
{
    /// <summary>一則回覆的文字最多這麼長。本機代理是外部送來的，不能讓一則回覆把資料庫撐大。</summary>
    public const int MaxReplyCharacters = 100_000;

    private readonly List<AssistantPart> _parts = [];
    private readonly StringBuilder _segment = new();
    private readonly StringBuilder _all = new();
    private readonly List<string> _segments = [];
    private readonly Dictionary<string, int> _toolIndexes = new(StringComparer.Ordinal);
    private readonly ToolMarkupFilter _markup = new();

    public long? InputTokens { get; private set; }

    public long? OutputTokens { get; private set; }

    /// <summary>文字超過 <see cref="MaxReplyCharacters"/> 了：後面的不收。</summary>
    public bool Truncated { get; private set; }

    /// <summary>不同的工具呼叫超過 <see cref="LocalAgentLimits.MaxToolCallsPerReply"/> 個了：後面的不收。</summary>
    public bool ToolLimitExceeded { get; private set; }

    /// <summary>本機代理送來的內容超過上限時給人看的原因；沒超過是 null。</summary>
    public string? Problem => Truncated
        ? AssistantReplyRunner.TruncatedMessage
        : ToolLimitExceeded ? ToolLimitMessage : null;

    public const string ToolLimitMessage = "本機的引擎呼叫工具的次數超過上限，已經停下。請把要求拆小一點再試。";

    /// <summary>工作的編號；回覆存完之後由執行器刪掉那一列。</summary>
    public Guid? JobId { get; set; }

    public void SetUsage(AssistantUsage? usage)
    {
        InputTokens = usage?.InputTokens;
        OutputTokens = usage?.OutputTokens;
    }

    public IReadOnlyList<AssistantRunEvent> Apply(LocalAgentEvent progress) => progress switch
    {
        LocalAgentText text => AddText(text.Delta),
        LocalAgentTool tool => AddTool(tool.Tool),
        _ => [],
    };

    public (IReadOnlyList<AssistantRunEvent> Events, IReadOnlyList<AssistantPart> Parts, IReadOnlyList<TranscriptMessage> Transcript) Finish(
        string unresolvedError)
    {
        var events = AppendText(_markup.Flush());
        FlushSegment();
        for (var i = 0; i < _parts.Count; i++)
        {
            if (_parts[i] is AssistantToolPart { Status: AssistantToolStatus.Running } running)
            {
                var failed = running with { Status = AssistantToolStatus.Failed, Error = unresolvedError };
                _parts[i] = failed;
                events.Add(new AssistantToolUpdate(failed));
            }
        }

        return (events, [.. _parts], TranscriptOf());
    }

    // 工具前後的文字是不同的段落，串起來要有換行。只有工具、沒有文字時存一句說明，不留空的：
    // 空的 transcript 會讓下一輪變成連續兩則使用者訊息。
    private IReadOnlyList<TranscriptMessage> TranscriptOf()
    {
        if (_segments.Count > 0)
        {
            return [new TranscriptMessage(TranscriptMessage.AssistantRole, [new TranscriptText(string.Join("\n", _segments))])];
        }

        var tools = _parts.OfType<AssistantToolPart>().Select(t => t.Name).Distinct().ToList();
        return tools.Count == 0
            ? []
            : [new TranscriptMessage(TranscriptMessage.AssistantRole, [new TranscriptText($"（用了 {string.Join("、", tools)}，沒有文字回覆）")])];
    }

    private List<AssistantRunEvent> AddText(string delta) => AppendText(_markup.Push(delta));

    private List<AssistantRunEvent> AppendText(string delta)
    {
        var room = MaxReplyCharacters - _all.Length;
        if (delta.Length > room)
        {
            Truncated = true;
            delta = AiText.Cut(delta, Math.Max(room, 0));
        }

        if (delta.Length == 0)
        {
            return [];
        }

        _segment.Append(delta);
        _all.Append(delta);
        return [new AssistantTextDelta(delta)];
    }

    private List<AssistantRunEvent> AddTool(AssistantToolPart tool)
    {
        var events = new List<AssistantRunEvent>();

        // 工具開始時文字先收成一段，之後的文字是新的一段：段落的順序和模型說話的順序一樣。
        // 已經在跑的工具更新狀態不是段落的邊界，過濾器留著的半截標記要等後面的字接上。
        if (!_toolIndexes.TryGetValue(tool.CallId, out var index))
        {
            events.AddRange(AppendText(_markup.Flush()));
            if (_toolIndexes.Count >= LocalAgentLimits.MaxToolCallsPerReply)
            {
                ToolLimitExceeded = true;
                return events;
            }

            FlushSegment();
            index = _parts.Count;
            _toolIndexes[tool.CallId] = index;

            // 這則回覆裡自己的編號（c1、c2…，依工具出現的順序）：引擎給的編號格式不一，也可能很長。
            _parts.Add(new AssistantToolPart($"c{_toolIndexes.Count}", tool.Name, AssistantToolStatus.Running, null, null));
        }

        var current = (AssistantToolPart)_parts[index];
        var updated = current with
        {
            Name = tool.Name,
            Status = tool.Status,
            ChangeSetNumber = tool.Status == AssistantToolStatus.Done && tool.Name == AssistantToolNames.ProposeChangeSet
                ? tool.ChangeSetNumber
                : null,
            Error = tool.Status == AssistantToolStatus.Failed ? tool.Error : null,
            ItemProposalId = tool.Status == AssistantToolStatus.Done && tool.Name == AssistantToolNames.ProposeItemChanges
                ? tool.ItemProposalId
                : current.ItemProposalId,
        };
        _parts[index] = updated;
        events.Add(new AssistantToolUpdate(updated));
        return events;
    }

    private void FlushSegment()
    {
        if (_segment.Length == 0)
        {
            return;
        }

        var text = _segment.ToString();
        _parts.Add(new AssistantTextPart(text));
        _segments.Add(text);
        _segment.Clear();
    }
}
