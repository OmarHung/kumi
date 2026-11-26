using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace PmTool.Ai.Assistant.Transcript;

/// <summary>
/// 把模型回覆的串流（含工具迴圈的來回）一段一段收起來：同時組出給人看的段落、之後再送給模型的 transcript、用量，
/// 並說出這一段該送出哪些事件。一則回覆用一個，不是執行緒安全的（只在背景的回覆迴圈裡用）。
/// </summary>
internal sealed class ReplyAccumulator : IReplyRecorder
{
    public const string InterruptedError = "回覆中斷了。";

    public const string CallLimitError = "已經達到一則回覆最多呼叫工具的次數，沒有執行。";

    private readonly List<AssistantPart> _parts = [];
    private readonly List<TranscriptMessage> _messages = [];
    private readonly List<TranscriptContent> _assistant = [];
    private readonly List<TranscriptContent> _tool = [];
    private readonly StringBuilder _text = new();
    private readonly Dictionary<string, string> _ownIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _partIndexes = new(StringComparer.Ordinal);
    private readonly List<string> _callOrder = [];
    private readonly HashSet<string> _resolved = new(StringComparer.Ordinal);
    private readonly ToolMarkupFilter _markup = new();
    private readonly ReplyMarkerFilter _marker = new();

    // 沒有標記、後面接著工具的段落在 _parts 裡的位置與它在 transcript 裡的那一筆：整則回覆有標記就兩邊都丟掉，
    // 一個標記都沒有就留在原位（畫面上有的文字，下一則送回給模型的歷史也要有）。
    private readonly HashSet<int> _withheldIndexes = [];
    private readonly List<TranscriptText> _withheldContents = [];

    public ChatFinishReason? FinishReason { get; private set; }

    public long? InputTokens { get; private set; }

    public long? OutputTokens { get; private set; }

    /// <summary>已經送出（或要送出）的文字，含還在緩衝的。</summary>
    public bool HasText => _parts.OfType<AssistantTextPart>().Any() || _text.Length > 0;

    public bool HasUnresolvedCalls => _callOrder.Count > _resolved.Count;

    public IReadOnlyList<AssistantRunEvent> Apply(ChatResponseUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (update.FinishReason is { } finish)
        {
            FinishReason = finish;
        }

        var events = new List<AssistantRunEvent>();
        foreach (var content in update.Contents)
        {
            switch (content)
            {
                case TextContent text when text.Text.Length > 0:
                    AddModelText(text.Text, events);
                    break;
                case FunctionCallContent call:
                    AddCall(call, events);
                    break;
                case FunctionResultContent result:
                    AddResult(result, events);
                    break;
                case UsageContent usage:
                    AddUsage(usage.Details);
                    break;
            }
        }

        return events;
    }

    /// <summary>
    /// 還沒有結果的工具呼叫補上失敗的結果（不然下一則送出去時供應商會因為有 call 沒有 result 而拒絕）。
    /// 回傳補上的事件。
    /// </summary>
    public IReadOnlyList<AssistantRunEvent> ResolveOpenCalls(string error)
    {
        var events = new List<AssistantRunEvent>();
        DrainMarkup(events, isLast: true);
        FlushText();
        foreach (var providerId in _callOrder.Where(id => !_resolved.Contains(id)).ToList())
        {
            Resolve(providerId, ToolOutcome.FromError(error), events);
        }

        return events;
    }

    /// <summary>在回覆的最後加一段說明（不是模型寫的，例如達到工具呼叫的上限）。</summary>
    public IReadOnlyList<AssistantRunEvent> AddNote(string note)
    {
        var events = new List<AssistantRunEvent>();
        DrainMarkup(events, isLast: true);
        AppendText(note, events);
        return events;
    }

    /// <summary>收尾：還沒補的工具呼叫補上失敗的結果，回傳給人看的段落與 transcript。</summary>
    public (IReadOnlyList<AssistantRunEvent> Events, IReadOnlyList<AssistantPart> Parts, IReadOnlyList<TranscriptMessage> Transcript) Finish(
        string unresolvedError)
    {
        var events = ResolveOpenCalls(unresolvedError);
        FlushText();
        FlushAssistant();
        FlushTool();
        return (events, PartsToShow(), TranscriptToKeep());
    }

    private void AddModelText(string delta, List<AssistantRunEvent> events)
    {
        // 模型偶爾吐出 NUL：資料庫的 jsonb 不收，存的時候整則都會失敗。
        var visible = _markup.Push(delta.Replace("\0", string.Empty, StringComparison.Ordinal));
        AppendText(_marker.Push(visible), events);
    }

    /// <summary>
    /// 過濾器留著的文字交出來並結束這一段：工具開始、有結果、加說明、收尾之前都要，段落的順序才和模型說話的順序一樣。
    /// 沒有標記、後面接著工具的文字不發事件（補發會出現在畫面的最後面），只在 _parts 佔著它的位置。
    /// </summary>
    private void DrainMarkup(List<AssistantRunEvent> events, bool isLast = false)
    {
        AppendText(_marker.Push(_markup.Flush()), events);
        var end = _marker.EndSegment(isLast);
        AppendText(end.Output, events);
        if (end.Withheld is { } withheld)
        {
            Withhold(withheld);
        }
    }

    private void Withhold(string text)
    {
        FlushText();
        FlushTool();
        _withheldIndexes.Add(_parts.Count);
        _parts.Add(new AssistantTextPart(text));
        var content = new TranscriptText(text);
        _withheldContents.Add(content);
        _assistant.Add(content);
    }

    private IReadOnlyList<AssistantPart> PartsToShow() => _marker.SawMarker
        ? [.. _parts.Where((_, index) => !_withheldIndexes.Contains(index))]
        : [.. _parts];

    /// <summary>
    /// 有標記的回覆把留著的那幾筆從 transcript 拿掉（照參考比對：同樣的字可能也是正常的回答），拿完變空的訊息不留。
    /// </summary>
    private IReadOnlyList<TranscriptMessage> TranscriptToKeep()
    {
        if (!_marker.SawMarker || _withheldContents.Count == 0)
        {
            return [.. _messages];
        }

        return
        [
            .. _messages
                .Select(message => message with { Contents = [.. message.Contents.Where(content => !IsWithheld(content))] })
                .Where(message => message.Contents.Count > 0),
        ];
    }

    private bool IsWithheld(TranscriptContent content) =>
        _withheldContents.Exists(withheld => ReferenceEquals(withheld, content));

    private void AppendText(string text, List<AssistantRunEvent> events)
    {
        if (text.Length == 0)
        {
            return;
        }

        FlushTool();
        _text.Append(text);
        events.Add(new AssistantTextDelta(text));
    }

    private void AddCall(FunctionCallContent call, List<AssistantRunEvent> events)
    {
        // 串流可能把同一個呼叫報兩次（參數補齊時）：只認第一次。
        if (_ownIds.ContainsKey(call.CallId))
        {
            return;
        }

        DrainMarkup(events);
        FlushText();
        FlushTool();
        var ownId = $"c{_ownIds.Count + 1}";
        _ownIds[call.CallId] = ownId;
        _callOrder.Add(call.CallId);
        _assistant.Add(new TranscriptCall(ownId, call.Name, ArgumentsOf(call)));
        var part = new AssistantToolPart(ownId, call.Name, AssistantToolStatus.Running, null, null);
        _partIndexes[call.CallId] = _parts.Count;
        _parts.Add(part);
        events.Add(new AssistantToolUpdate(part));
    }

    private void AddResult(FunctionResultContent result, List<AssistantRunEvent> events)
    {
        if (!_ownIds.ContainsKey(result.CallId) || _resolved.Contains(result.CallId))
        {
            return;
        }

        Resolve(result.CallId, ToolOutcome.From(result), events);
    }

    private void Resolve(string providerId, ToolOutcome outcome, List<AssistantRunEvent> events)
    {
        DrainMarkup(events);
        FlushText();
        FlushAssistant();
        var ownId = _ownIds[providerId];
        _tool.Add(new TranscriptResult(ownId, outcome.RawResult));
        _resolved.Add(providerId);
        var index = _partIndexes[providerId];
        var running = (AssistantToolPart)_parts[index];
        var finished = running with
        {
            Status = outcome.IsOk ? AssistantToolStatus.Done : AssistantToolStatus.Failed,
            ChangeSetNumber = outcome.IsOk && running.Name == AssistantToolNames.ProposeChangeSet ? outcome.ChangeSetNumber : null,
            ItemProposalId = outcome.IsOk && running.Name == AssistantToolNames.ProposeItemChanges ? outcome.ItemProposalId : null,
            ProjectProposalId = outcome.IsOk && running.Name == AssistantToolNames.ProposeProject ? outcome.ProjectProposalId : null,
            NewItemProposalId = outcome.IsOk && running.Name == AssistantToolNames.ProposeNewItems ? outcome.NewItemProposalId : null,
            Error = outcome.IsOk ? null : outcome.Error,
        };
        _parts[index] = finished;
        events.Add(new AssistantToolUpdate(finished));
    }

    private void AddUsage(UsageDetails? details)
    {
        if (details is null)
        {
            return;
        }

        // 每次呼叫模型回報一次，把整則回覆（含工具來回）的加起來。
        InputTokens = (InputTokens ?? 0) + (details.InputTokenCount ?? 0);
        OutputTokens = (OutputTokens ?? 0) + (details.OutputTokenCount ?? 0);
    }

    private void FlushText()
    {
        if (_text.Length == 0)
        {
            return;
        }

        var text = _text.ToString();
        _text.Clear();
        _assistant.Add(new TranscriptText(text));
        _parts.Add(new AssistantTextPart(text));
    }

    private void FlushAssistant()
    {
        if (_assistant.Count == 0)
        {
            return;
        }

        _messages.Add(new TranscriptMessage(TranscriptMessage.AssistantRole, [.. _assistant]));
        _assistant.Clear();
    }

    private void FlushTool()
    {
        if (_tool.Count == 0)
        {
            return;
        }

        _messages.Add(new TranscriptMessage(TranscriptMessage.ToolRole, [.. _tool]));
        _tool.Clear();
    }

    private static JsonElement ArgumentsOf(FunctionCallContent call)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, value) in call.Arguments ?? new Dictionary<string, object?>())
        {
            arguments[name] = value;
        }

        return AssistantJson.WithoutNul(JsonSerializer.SerializeToElement(arguments, AssistantJson.Options));
    }
}
