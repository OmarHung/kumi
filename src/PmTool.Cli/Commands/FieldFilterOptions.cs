using System.Text.RegularExpressions;

namespace PmTool.Cli.Commands;

/// <summary>
/// 把 --field 欄位=值[,值…] 轉成 API 的 field.&lt;欄位&gt; 參數。值不轉換（選項 key、人員編號、me、true／false、_none 都照傳），
/// 格式不對的在送出前就擋下，不用多跑一趟伺服器才知道打錯。
/// </summary>
internal static partial class FieldFilterOptions
{
    public static IReadOnlyList<KeyValuePair<string, string>> ToQuery(IEnumerable<string> options)
    {
        var query = new List<KeyValuePair<string, string>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var option in options)
        {
            var (field, values) = Parse(option);
            // 同一個欄位只能寫一次：兩次的話分不出使用者要「或」還是打錯，值要用逗號放在同一個 --field
            if (!seen.Add(field))
            {
                throw CliException.Usage($"--field 的欄位 {field} 給了兩次。同一個欄位的多個值請用逗號分開，例如 --field {field}=甲,乙。");
            }

            query.AddRange(values.Select(value => new KeyValuePair<string, string>($"field.{field}", value)));
        }

        return query;
    }

    private static (string Field, string[] Values) Parse(string option)
    {
        var equals = option.IndexOf('=');
        if (equals < 0)
        {
            throw CliException.Usage($"--field 要寫成 欄位=值，收到「{option}」。");
        }

        var field = option[..equals];
        if (!FieldKey().IsMatch(field))
        {
            throw CliException.Usage($"--field 的欄位名稱「{field}」不像藍圖的欄位 key（小寫英文字母開頭，接小寫英文字母、數字、底線，2 到 40 個字）。");
        }

        var values = option[(equals + 1)..].Split(',');
        if (values.Any(string.IsNullOrWhiteSpace))
        {
            throw CliException.Usage($"--field {field} 的值不能是空的。要找沒有填的，值寫 _none。");
        }

        return (field, values);
    }

    // \A \z 而不是 ^ $：.NET 的 $ 會讓結尾多一個換行也通過。
    [GeneratedRegex(@"\A[a-z][a-z0-9_]{1,39}\z", RegexOptions.CultureInvariant)]
    private static partial Regex FieldKey();
}
