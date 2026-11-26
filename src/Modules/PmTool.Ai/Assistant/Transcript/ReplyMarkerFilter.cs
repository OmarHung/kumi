using System.Text;

namespace PmTool.Ai.Assistant.Transcript;

/// <summary>
/// 只留下單獨一行的 <c>[REPLY]</c> 之後的文字。Claude Code 上的模型在「跳過思考」的輪次會把推理、控制標記的碎片寫進回覆
/// （開頭多一句英文、工具呼叫前多一段 <c>Thinking...</c>），提示擋不住、也沒有設定能關；實測有效的是限定格式：
/// 系統提示要求給使用者看的文字放在標記之後，混進來的東西全部在標記之前。
/// 一「段」是兩個工具呼叫之間連續的文字，每段各自判斷。一段裡第一個算數的標記才算：(a) 後面只有空格與 Tab 然後換行；
/// (b) 在這一段最開頭（前面只有空白），後面直接接內容也算；(c) 獨佔這一段的最後一行。
/// 不取「第一個標記」，因為漏出來的推理有時自己提到標記（「…with [REPLY] header.[REPLY]」）。
/// 一則回覆用一個，不是執行緒安全的。
/// </summary>
internal sealed class ReplyMarkerFilter
{
    public const string Marker = "[REPLY]";

    /// <summary>
    /// 一段沒有標記的文字累積超過這麼多字就不等了：標記都在回覆的最前面，等太久串流會卡住，
    /// 也不能讓一個沒照格式寫的長回答整段被留著。實測混進來的東西最長約 150 個字，留十倍以上的餘裕；
    /// 模型失控、一路吐到輸出上限的那種（看過一次 35 萬字）擋不住，會照原樣放行，由長度上限與逾時收尾。
    /// </summary>
    public const int GiveUpLength = 2000;

    private readonly StringBuilder _held = new();
    private Mode _mode = Mode.Searching;
    private bool _pendingCarriageReturn;

    /// <summary>這則回覆到目前為止有沒有任何一段出現過算數的標記。</summary>
    public bool SawMarker { get; private set; }

    /// <summary>目前這一段收到文字，回傳現在可以顯示的（找到標記之前是空的）。</summary>
    public string Push(string delta)
    {
        if (delta.Length == 0)
        {
            return string.Empty;
        }

        switch (_mode)
        {
            case Mode.Passing:
                return delta;
            case Mode.SkippingGap:
                return SkipGap(delta);
        }

        _held.Append(delta);
        var text = _held.ToString();
        var end = FindMarkerEnd(text, isFinal: false);
        if (end >= 0)
        {
            SawMarker = true;
            _held.Clear();
            _mode = Mode.SkippingGap;
            return SkipGap(text[end..]);
        }

        if (text.Length <= GiveUpLength)
        {
            return string.Empty;
        }

        _held.Clear();
        _mode = Mode.Passing;
        return text;
    }

    /// <summary>
    /// 這一段結束。最後一段沒有標記時照原樣交出去；後面接著工具而沒有標記的，留著不顯示（<see cref="SegmentEnd.Withheld"/>），
    /// 等整則回覆結束才依 <see cref="SawMarker"/> 決定丟掉或放回原位。下一段重新開始判斷。
    /// </summary>
    public SegmentEnd EndSegment(bool isLast)
    {
        var result = new SegmentEnd(string.Empty, null);
        if (_mode == Mode.Searching && _held.Length > 0)
        {
            var text = _held.ToString();
            if (FindMarkerEnd(text, isFinal: true) >= 0)
            {
                SawMarker = true;
            }
            else
            {
                result = isLast ? new SegmentEnd(text, null) : new SegmentEnd(string.Empty, text);
            }
        }

        _held.Clear();
        _mode = Mode.Searching;
        _pendingCarriageReturn = false;
        return result;
    }

    /// <summary>標記之後的空格、Tab 與一個換行（<c>\r\n</c> 也算）不顯示；之後的換行與內容照原樣。</summary>
    private string SkipGap(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (_pendingCarriageReturn)
            {
                _pendingCarriageReturn = false;
                _mode = Mode.Passing;
                return c == '\n' ? text[(i + 1)..] : "\r" + text[i..];
            }

            if (c is ' ' or '\t')
            {
                continue;
            }

            if (c == '\r')
            {
                _pendingCarriageReturn = true;
                continue;
            }

            _mode = Mode.Passing;
            return c == '\n' ? text[(i + 1)..] : text[i..];
        }

        return string.Empty;
    }

    private static int FindMarkerEnd(string text, bool isFinal)
    {
        var from = 0;
        while (true)
        {
            var index = text.IndexOf(Marker, from, StringComparison.Ordinal);
            if (index < 0)
            {
                return -1;
            }

            var end = index + Marker.Length;
            if (Counts(text, index, end, isFinal))
            {
                return end;
            }

            from = end;
        }
    }

    private static bool Counts(string text, int start, int end, bool isFinal)
    {
        if (text.AsSpan(0, start).Trim().Length == 0)
        {
            return true;
        }

        var p = end;
        while (p < text.Length && text[p] is ' ' or '\t')
        {
            p++;
        }

        if (p == text.Length)
        {
            return isFinal && StartsItsLine(text, start);
        }

        if (text[p] == '\n')
        {
            return true;
        }

        if (text[p] != '\r')
        {
            return false;
        }

        return p + 1 < text.Length ? text[p + 1] == '\n' : isFinal && StartsItsLine(text, start);
    }

    /// <summary>
    /// 段落結尾的標記要獨佔那一行才算：沒照格式寫的回答最後一句剛好提到標記（「…單獨一行的 [REPLY]」）時，
    /// 不能把整段回答當成標記之前的東西丟掉。
    /// </summary>
    private static bool StartsItsLine(string text, int start)
    {
        var p = start - 1;
        while (p >= 0 && text[p] is ' ' or '\t')
        {
            p--;
        }

        return p < 0 || text[p] == '\n';
    }

    /// <summary>
    /// 重送歷史時把標記補回去：存的是不帶標記的文字，模型看到自己的回覆沒有標記，下一則就可能學著不寫。
    /// 已經以標記開頭的不重複加（本機代理的歷史先經過 transcript 的還原）；空的文字不加。
    /// </summary>
    public static string WithMarker(string text) =>
        text.Length == 0 || text.StartsWith(Marker + "\n", StringComparison.Ordinal) ? text : Marker + "\n" + text;

    private enum Mode
    {
        Searching,
        SkippingGap,
        Passing,
    }

    /// <summary>一段結束的結果：還要顯示的文字，與「沒有標記、後面接著工具而留著」的文字（沒有時是 null）。</summary>
    public readonly record struct SegmentEnd(string Output, string? Withheld);
}
