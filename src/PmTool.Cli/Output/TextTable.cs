using System.Text;

namespace PmTool.Cli.Output;

/// <summary>用空白對齊的表格。中文字佔兩格寬，不然欄位會歪掉。</summary>
internal static class TextTable
{
    private const int ColumnGap = 2;

    public static async Task WriteAsync(TextWriter output, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var widths = headers.Select((header, column) =>
            rows.Select(row => DisplayWidth(row[column])).Append(DisplayWidth(header)).Max()).ToArray();
        await output.WriteLineAsync(Line(headers, widths));
        foreach (var row in rows)
        {
            await output.WriteLineAsync(Line(row, widths));
        }
    }

    public static int DisplayWidth(string text) => text.EnumerateRunes().Sum(rune => IsWide(rune.Value) ? 2 : 1);

    private static string Line(IReadOnlyList<string> cells, int[] widths)
    {
        var line = new StringBuilder();
        for (var column = 0; column < cells.Count; column++)
        {
            var cell = Flatten(cells[column]);
            line.Append(cell);
            if (column < cells.Count - 1)
            {
                line.Append(' ', widths[column] - DisplayWidth(cell) + ColumnGap);
            }
        }

        return line.ToString();
    }

    /// <summary>表格一格只放一行：標題裡的換行換成空白。</summary>
    private static string Flatten(string cell) => cell.Replace("\r", string.Empty).Replace('\n', ' ');

    private static bool IsWide(int codePoint) => codePoint is
        (>= 0x1100 and <= 0x115F) or (>= 0x2E80 and <= 0xA4CF) or (>= 0xAC00 and <= 0xD7A3)
        or (>= 0xF900 and <= 0xFAFF) or (>= 0xFE30 and <= 0xFE6F) or (>= 0xFF00 and <= 0xFF60)
        or (>= 0xFFE0 and <= 0xFFE6) or (>= 0x1F300 and <= 0x1FAFF) or (>= 0x20000 and <= 0x3FFFD);
}
