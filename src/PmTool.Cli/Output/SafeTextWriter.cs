using System.Globalization;
using System.Text;

namespace PmTool.Cli.Output;

/// <summary>
/// 包住輸出，把控制字元濾掉再寫出去。伺服器回的文字（標題、留言、錯誤訊息）都是使用者打的，
/// 裡面如果夾了 ESC 序列，原樣印到終端機就能改畫面、改視窗標題，甚至把東西寫進剪貼簿。
/// 只留下換行與 Tab；其他 C0（含 ESC）、DEL 與 C1 都拿掉。另外拿掉 Unicode 的 Format 類字元（方向覆寫、零寬字元：
/// 惡意的標題用它們讓畫面上的字和實際內容不同），單獨的歸位字元換成空白（不然後面的字會蓋掉前面的）。
/// </summary>
internal sealed class SafeTextWriter(TextWriter inner) : TextWriter
{
    // 單一字元寫入時，補充平面的字元會拆成兩個代理字元分兩次到：記住前半，等後半到了才能判斷整個字元能不能印。
    private char _pendingHigh;

    public override Encoding Encoding => inner.Encoding;

    public override void Write(char value) => inner.Write(One(value));

    public override void Write(string? value) => inner.Write(Take(value));

    public override void Write(char[] buffer, int index, int count) => inner.Write(Take(new string(buffer, index, count)));

    public override void Write(ReadOnlySpan<char> buffer) => inner.Write(Take(new string(buffer)));

    public override Task WriteAsync(char value) => inner.WriteAsync(One(value));

    public override Task WriteAsync(string? value) => inner.WriteAsync(Take(value));

    public override Task WriteAsync(char[] buffer, int index, int count) => inner.WriteAsync(Take(new string(buffer, index, count)));

    public override Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default) =>
        inner.WriteAsync(Take(new string(buffer.Span)).AsMemory(), cancellationToken);

    public override Task WriteLineAsync() => inner.WriteLineAsync();

    public override Task WriteLineAsync(string? value) => inner.WriteLineAsync(Take(value));

    public override Task WriteLineAsync(char value) => inner.WriteLineAsync(Take(value.ToString()));

    public override Task WriteLineAsync(char[] buffer, int index, int count) =>
        inner.WriteLineAsync(Take(new string(buffer, index, count)));

    public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default) =>
        inner.WriteLineAsync(Take(new string(buffer.Span)).AsMemory(), cancellationToken);

    public override void Flush() => inner.Flush();

    public override Task FlushAsync() => inner.FlushAsync();

    /// <summary>
    /// 拿掉不能印的字元；單獨的 \r（後面不是 \n）換成空白。沒有要動的就原樣回傳，不配置新字串。
    /// 以 rune 判斷：標籤字元（U+E0000 起）在補充平面，只看 char 會漏。
    /// </summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (IsClean(text))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length;)
        {
            var status = Rune.DecodeFromUtf16(text.AsSpan(index), out var rune, out var consumed);
            index += consumed;
            if (status != System.Buffers.OperationStatus.Done)
            {
                // 成對不起來的代理字元：不是任何字，丟掉。
                continue;
            }

            if (rune.Value == '\r')
            {
                // 只有 \r\n 的 \r 算換行；單獨的 \r 會讓游標回到行首，後面的字蓋掉前面印過的。
                builder.Append(index < text.Length && text[index] == '\n' ? '\r' : ' ');
            }
            else if (IsAllowed(rune))
            {
                builder.Append(rune.ToString());
            }
        }

        return builder.ToString();
    }

    private static bool IsClean(string text)
    {
        for (var index = 0; index < text.Length;)
        {
            var status = Rune.DecodeFromUtf16(text.AsSpan(index), out var rune, out var consumed);
            if (status != System.Buffers.OperationStatus.Done || !IsAllowed(rune) && rune.Value != '\r'
                || rune.Value == '\r' && !(index + 1 < text.Length && text[index + 1] == '\n'))
            {
                return false;
            }

            index += consumed;
        }

        return true;
    }

    private string Take(string? text)
    {
        _pendingHigh = '\0';
        return Clean(text);
    }

    /// <summary>
    /// 單一字元寫出時看不到後面接什麼：\r 一律當成單獨的。前半的代理字元先扣著，後半到了湊成完整的字元再判斷
    /// （標籤字元這類補充平面的 Format 字元也要濾掉）；對不起來的單獨一半丟掉。
    /// </summary>
    private string One(char value)
    {
        if (char.IsHighSurrogate(value))
        {
            _pendingHigh = value;
            return string.Empty;
        }

        var high = _pendingHigh;
        _pendingHigh = '\0';
        if (char.IsLowSurrogate(value))
        {
            return high == '\0' ? string.Empty : Clean(new string([high, value]));
        }

        return Clean(value.ToString());
    }

    private static bool IsAllowed(Rune rune) =>
        rune.Value is '\n' or '\t'
        || (!Rune.IsControl(rune) && Rune.GetUnicodeCategory(rune) != UnicodeCategory.Format);
}
