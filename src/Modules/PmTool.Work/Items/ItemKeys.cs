using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using PmTool.Work.Access;

namespace PmTool.Work.Items;

/// <summary>項目編號：專案代號、連字號、流水號，例如 WEB-12。</summary>
internal static class ItemKeys
{
    public static string Format(string projectKey, long number) =>
        string.Create(CultureInfo.InvariantCulture, $"{projectKey}-{number}");

    /// <summary>不合格式的編號直接當成找不到，不必去查資料庫。代號不分大小寫，和查專案時一樣。</summary>
    public static bool TryParse(string? itemKey, [NotNullWhen(true)] out string? projectKey, out long number)
    {
        projectKey = null;
        number = 0;
        var dash = itemKey?.LastIndexOf('-') ?? -1;
        if (dash <= 0 || !ProjectKeys.TryNormalize(itemKey![..dash], out var key))
        {
            return false;
        }

        // 數字解析會忽略尾端的 NUL，"WEB-1\0" 會被當成 WEB-1；先確認後面全是數字。
        var digits = itemKey.AsSpan(dash + 1);
        if (!IsAllAsciiDigits(digits) || !long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            return false;
        }

        projectKey = key;
        number = parsed;
        return true;
    }

    public static bool IsAllAsciiDigits(ReadOnlySpan<char> text) => !text.IsEmpty && !text.ContainsAnyExceptInRange('0', '9');
}
