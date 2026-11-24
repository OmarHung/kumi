using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using PmTool.Blueprint.Model;
using PmTool.Work.Items;

namespace PmTool.Work.ChangeSets;

/// <summary>
/// 欄位換型別時一個值的轉換（允許哪些轉換由藍圖的計畫決定）。先把值改成新型別的形狀，再交給
/// <see cref="FieldValueRules"/> 照新型別檢查與正規化：數字的範圍與小數位數、文字的長度與換行，規則和直接填值時一樣。
/// </summary>
internal static partial class ValueConversions
{
    /// <returns>轉好的值；轉不過去是 null。</returns>
    public static JsonElement? Convert(FieldDefinition from, FieldDefinition to, JsonElement value)
    {
        if (Reshape(from.Type, to.Type, value) is not { } reshaped)
        {
            return null;
        }

        var result = FieldValueRules.Check(to, reshaped);
        return result.Problem is null ? result.Value : null;
    }

    // 單選→多選包成陣列，選項對不對由多選的檢查負責。文字→長文字、日期→文字、連結→文字、長文字→文字的值本身不用改，
    // 能不能放進新型別由檢查決定。
    private static JsonElement? Reshape(FieldType from, FieldType to, JsonElement value) => (from, to) switch
    {
        (FieldType.SingleSelect, FieldType.MultiSelect) => JsonSerializer.SerializeToElement(new[] { value }),
        (FieldType.MultiSelect, FieldType.SingleSelect) =>
            value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 1 ? value[0] : null,
        (FieldType.Number, FieldType.Text) => NumberAsText(value),
        (FieldType.Text, FieldType.Number) => TextAsNumber(value),
        (FieldType.Text, FieldType.Date) => Trimmed(value),
        _ => value,
    };

    /// <summary>存著的數字已經正規化過，寫成一般的寫法（2.5、100），不會出現指數或尾端的 0。</summary>
    private static JsonElement? NumberAsText(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && NumberRules.TryNormalize(value, out var number)
            ? JsonSerializer.SerializeToElement(number.ToString(CultureInfo.InvariantCulture))
            : null;

    /// <summary>
    /// 只認一般的十進位寫法：可以有正負號、可以省略整數或小數部分（.5、5.），前後的空白不算。
    /// 千分位、全形數字、指數都不認，轉不過去比猜錯好。認得的寫法換成 JSON 的數字，再照數字欄位的規則檢查範圍。
    /// </summary>
    private static JsonElement? TextAsNumber(JsonElement value)
    {
        var match = Trimmed(value) is { } text ? DecimalText().Match(text.GetString()!) : Match.Empty;
        var whole = match.Groups["whole"].Value;
        var fraction = match.Groups["fraction"].Value;
        if (!match.Success || whole.Length + fraction.Length == 0)
        {
            return null;
        }

        var digits = whole.TrimStart('0') is { Length: > 0 } significant ? significant : "0";
        var literal = match.Groups["sign"].Value.Replace("+", string.Empty, StringComparison.Ordinal)
            + digits + (fraction.Length > 0 ? "." + fraction : string.Empty);
        using var document = JsonDocument.Parse(literal);
        return document.RootElement.Clone();
    }

    private static JsonElement? Trimmed(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? JsonSerializer.SerializeToElement(value.GetString()!.Trim()) : null;

    [GeneratedRegex(@"\A(?<sign>[+-]?)(?<whole>[0-9]*)(?:\.(?<fraction>[0-9]*))?\z", RegexOptions.CultureInvariant)]
    private static partial Regex DecimalText();
}
