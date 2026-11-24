using System.Text;

namespace PmTool.Cli.Agent.Autostart;

/// <summary>每一行前面加時間：背景程序的記錄檔沒有時間，出事時沒辦法對上「什麼時候」。</summary>
internal sealed class TimestampedWriter(TextWriter inner, Func<DateTimeOffset> now) : TextWriter
{
    public override Encoding Encoding => inner.Encoding;

    public override void Write(char value) => inner.Write(value);

    public override void Write(string? value) => inner.Write(value);

    public override void WriteLine(string? value) => inner.WriteLine($"{now():yyyy-MM-dd HH:mm:ss}  {value}");

    public override void Flush() => inner.Flush();
}
