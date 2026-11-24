using System.Text;

namespace PmTool.Cli.Output;

/// <summary>
/// 包住輸出，把控制字元濾掉再寫出去。伺服器回的文字（標題、留言、錯誤訊息）都是使用者打的，
/// 裡面如果夾了 ESC 序列，原樣印到終端機就能改畫面、改視窗標題，甚至把東西寫進剪貼簿。
/// 只留下換行、歸位與 Tab；其他 C0（含 ESC）、DEL 與 C1 都拿掉。
/// </summary>
internal sealed class SafeTextWriter(TextWriter inner) : TextWriter
{
    public override Encoding Encoding => inner.Encoding;

    public override void Write(char value)
    {
        if (IsAllowed(value))
        {
            inner.Write(value);
        }
    }

    public override void Write(string? value) => inner.Write(Clean(value));

    public override void Write(char[] buffer, int index, int count) => inner.Write(Clean(new string(buffer, index, count)));

    public override void Write(ReadOnlySpan<char> buffer) => inner.Write(Clean(new string(buffer)));

    public override Task WriteAsync(char value) => IsAllowed(value) ? inner.WriteAsync(value) : Task.CompletedTask;

    public override Task WriteAsync(string? value) => inner.WriteAsync(Clean(value));

    public override Task WriteAsync(char[] buffer, int index, int count) => inner.WriteAsync(Clean(new string(buffer, index, count)));

    public override Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default) =>
        inner.WriteAsync(Clean(new string(buffer.Span)).AsMemory(), cancellationToken);

    public override Task WriteLineAsync() => inner.WriteLineAsync();

    public override Task WriteLineAsync(string? value) => inner.WriteLineAsync(Clean(value));

    public override Task WriteLineAsync(char value) => IsAllowed(value) ? inner.WriteLineAsync(value) : inner.WriteLineAsync();

    public override Task WriteLineAsync(char[] buffer, int index, int count) =>
        inner.WriteLineAsync(Clean(new string(buffer, index, count)));

    public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default) =>
        inner.WriteLineAsync(Clean(new string(buffer.Span)).AsMemory(), cancellationToken);

    public override void Flush() => inner.Flush();

    public override Task FlushAsync() => inner.FlushAsync();

    /// <summary>拿掉不能印的控制字元。沒有就原樣回傳，不配置新字串。</summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.All(IsAllowed))
        {
            return text ?? string.Empty;
        }

        return string.Concat(text.Where(IsAllowed));
    }

    private static bool IsAllowed(char value) =>
        value is '\n' or '\r' or '\t' || (value >= ' ' && value != '\u007f' && !(value >= '\u0080' && value <= '\u009f'));
}
