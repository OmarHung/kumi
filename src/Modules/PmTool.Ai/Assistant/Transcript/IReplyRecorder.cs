namespace PmTool.Ai.Assistant.Transcript;

/// <summary>
/// 一則回覆收尾時要的東西：給人看的段落、之後再送給模型的 transcript、用量。
/// 用 API 金鑰的模型（<see cref="ReplyAccumulator"/>）與本機的引擎（<c>LocalReplyAccumulator</c>）各有一個實作，存檔的程式共用。
/// </summary>
internal interface IReplyRecorder
{
    long? InputTokens { get; }

    long? OutputTokens { get; }

    /// <summary>收尾：還沒結束的工具呼叫補上失敗的結果，回傳給人看的段落與 transcript。</summary>
    (IReadOnlyList<AssistantRunEvent> Events, IReadOnlyList<AssistantPart> Parts, IReadOnlyList<TranscriptMessage> Transcript) Finish(
        string unresolvedError);
}
