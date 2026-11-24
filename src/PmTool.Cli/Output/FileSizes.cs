using System.Globalization;

namespace PmTool.Cli.Output;

/// <summary>檔案大小給人看：用 1024 為進位，一位小數，和網頁一樣的寫法。</summary>
internal static class FileSizes
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB"];

    public static string Format(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{Math.Max(bytes, 0)} B";
        }

        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value.ToString("0.0", CultureInfo.InvariantCulture)} {Units[unit]}";
    }
}
