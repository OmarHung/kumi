using System.Text;

namespace PmTool.Cli.Agent.Autostart;

/// <summary>
/// 每一行前面加時間：背景程序的記錄檔沒有時間，出事時沒辦法對上「什麼時候」。
/// 寫進去的文字先把換行與其他控制字元換成空白：訊息裡有伺服器或引擎回的內容，帶著換行就能偽造出另一行「看起來是 kumi 寫的」記錄。
/// </summary>
internal sealed class TimestampedWriter(TextWriter inner, Func<DateTimeOffset> now) : TextWriter
{
    public override Encoding Encoding => inner.Encoding;

    // 單一字元只會從 WriteLine() 的換行來：換行本身放行（它不帶內容），其他控制字元換成空白。
    public override void Write(char value) => inner.Write(value is '\n' or '\r' || !char.IsControl(value) ? value : ' ');

    public override void Write(string? value) => inner.Write(SingleLine(value));

    public override void WriteLine(string? value) => inner.WriteLine($"{now():yyyy-MM-dd HH:mm:ss}  {SingleLine(value)}");

    // 非同步的寫法要和同步的一致：不覆寫的話 WriteLineAsync 會走基底類別，少了時間、也不濾控制字元。
    public override Task WriteAsync(char value) => inner.WriteAsync(value is '\n' or '\r' || !char.IsControl(value) ? value : ' ');

    public override Task WriteAsync(string? value) => inner.WriteAsync(SingleLine(value));

    public override Task WriteLineAsync() => inner.WriteLineAsync();

    public override Task WriteLineAsync(string? value) => inner.WriteLineAsync($"{now():yyyy-MM-dd HH:mm:ss}  {SingleLine(value)}");

    public override void Flush() => inner.Flush();

    public override Task FlushAsync() => inner.FlushAsync();

    /// <summary>控制字元（含換行、Tab、ESC、DEL、C1）與行分隔、段落分隔一律換成空白。</summary>
    internal static string SingleLine(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsControl(character) || (int)character is 0x2028 or 0x2029 ? ' ' : character);
        }

        return builder.ToString();
    }
}
