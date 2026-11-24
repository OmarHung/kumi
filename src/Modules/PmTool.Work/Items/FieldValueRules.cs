using System.Globalization;
using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Blueprint.Model;
using static PmTool.Work.Common.TextRules;

namespace PmTool.Work.Items;

/// <param name="Value">要寫進去的值；null 表示拿掉這個欄位（沒有值的欄位不出現）。</param>
/// <param name="Person">成員欄位指到的人，之後一起確認是不是專案成員。</param>
internal sealed record FieldResult(string Key, JsonElement? Value, FailureDetail? Problem, PersonReference? Person = null);

/// <summary>
/// 一個自訂欄位的值是否符合欄位型別（形狀見 <see cref="WorkItem"/> 的說明），並正規化。
/// 空字串、空陣列、沒勾的勾選都當成沒有值，這樣「要先填」的檢查只要看鍵在不在。
/// </summary>
internal static class FieldValueRules
{
    private const string DateFormat = "yyyy-MM-dd";

    public static FieldResult Check(FieldDefinition field, JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return Clear(field);
        }

        return field.Type switch
        {
            FieldType.Text => Text(field, value, WorkRules.MaxTextFieldLength, allowLineBreaks: false),
            FieldType.LongText => Text(field, value, WorkRules.MaxLongTextFieldLength, allowLineBreaks: true),
            FieldType.Number or FieldType.Currency => Number(field, value),
            FieldType.Date => Date(field, value),
            FieldType.SingleSelect => SingleSelect(field, value),
            FieldType.MultiSelect => MultiSelect(field, value),
            FieldType.Person => Person(field, value),
            FieldType.Checkbox => Checkbox(field, value),
            FieldType.Url => Url(field, value),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field.Type, "不認得的欄位型別。"),
        };
    }

    // 換行先統一成 \n 再算長度、存起來：換行的寫法不同時，內容相同也不能當成有改。
    private static FieldResult Text(FieldDefinition field, JsonElement value, int maxLength, bool allowLineBreaks)
    {
        if (!TryGetString(value, out var original))
        {
            return Invalid(field, $"{Quote(field.Name)}要填文字。");
        }

        var text = NormalizeLineBreaks(original)!;
        if (string.IsNullOrWhiteSpace(text))
        {
            return Clear(field);
        }

        if (text.Length > maxLength)
        {
            return Invalid(field, $"{Quote(field.Name)}最多 {maxLength} 個字，現在有 {text.Length} 個字。");
        }

        return HasForbiddenCharacters(text, allowLineBreaks)
            ? Invalid(field, $"{Quote(field.Name)}不能有{(allowLineBreaks ? string.Empty : "換行、")}控制字元或無效的字元。")
            : new FieldResult(field.Key, JsonSerializer.SerializeToElement(text), null);
    }

    // 存正規化後的值，不存原文：原文的 1e2 經過 jsonb 會變成 100，新增當下回的和之後讀回來的就對不上。
    private static FieldResult Number(FieldDefinition field, JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && NumberRules.TryNormalize(value, out var number)
            ? new FieldResult(field.Key, JsonSerializer.SerializeToElement(number), null)
            : Invalid(
                field,
                $"{Quote(field.Name)}要填數字，絕對值不能超過 {NumberRules.MaxText}，小數最多 {WorkRules.MaxNumberDecimalPlaces} 位。");

    private static FieldResult Date(FieldDefinition field, JsonElement value) =>
        TryGetString(value, out var text)
        && text.Length == DateFormat.Length
        && DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? Keep(field, value)
            : Invalid(field, $"{Quote(field.Name)}要填 YYYY-MM-DD 格式的日期，例如 2026-11-30。");

    private static FieldResult SingleSelect(FieldDefinition field, JsonElement value)
    {
        if (!TryGetString(value, out var option))
        {
            return Invalid(field, $"{Quote(field.Name)}只能選一個選項。");
        }

        return IsOption(field, option) ? Keep(field, value) : Invalid(field, $"{Quote(field.Name)}沒有{Quote(option)}這個選項。");
    }

    private static FieldResult MultiSelect(FieldDefinition field, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            return Invalid(field, $"{Quote(field.Name)}要是選項的清單。");
        }

        var options = value.EnumerateArray().Select(o => TryGetString(o, out var text) ? text : null).ToArray();
        if (options.Length == 0)
        {
            return Clear(field);
        }

        var invalid = Array.FindIndex(options, o => o is null || !IsOption(field, o));
        if (invalid >= 0)
        {
            return Invalid(field, options[invalid] is { } unknown
                ? $"{Quote(field.Name)}沒有{Quote(unknown)}這個選項。"
                : $"{Quote(field.Name)}的每一個選項都要是選項的 key。");
        }

        return options.Distinct(StringComparer.Ordinal).Count() == options.Length
            ? Keep(field, value)
            : Invalid(field, $"{Quote(field.Name)}的選項重複了。");
    }

    private static FieldResult Person(FieldDefinition field, JsonElement value)
    {
        if (!TryGetString(value, out var text) || !Guid.TryParse(text, out var personId))
        {
            return Invalid(field, $"{Quote(field.Name)}要選一位專案成員。");
        }

        // 人員編號的寫法不只一種，統一成小寫加連字號，查詢與比對才一致。
        var normalized = JsonSerializer.SerializeToElement(personId.ToString("D"));
        return new FieldResult(field.Key, normalized, null, new PersonReference(field.Key, field.Name, personId));
    }

    private static FieldResult Checkbox(FieldDefinition field, JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => Keep(field, value),
        JsonValueKind.False => Clear(field),
        _ => Invalid(field, $"{Quote(field.Name)}只能勾選或不勾選。"),
    };

    private static FieldResult Url(FieldDefinition field, JsonElement value)
    {
        if (!TryGetString(value, out var text))
        {
            return Invalid(field, $"{Quote(field.Name)}要填網址。");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return Clear(field);
        }

        return text.Length <= WorkRules.MaxTextFieldLength && IsAbsoluteHttpUrl(text)
            ? Keep(field, value)
            : Invalid(field, $"{Quote(field.Name)}要填 http:// 或 https:// 開頭的完整網址。");
    }

    // 和藍圖模組檢查網址的方式相同：Uri 會默默去掉空白、在 Unix 上把 "/path" 當成 file 網址，所以要先擋。
    private static bool IsAbsoluteHttpUrl(string text) =>
        !text.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))
        && Uri.TryCreate(text, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && uri.Host.Length > 0;

    private static bool IsOption(FieldDefinition field, string option) => field.Options?.Any(o => o.Key == option) == true;

    /// <summary>字串裡有落單的代理字元時，取值會丟例外；這種值當成不是字串。</summary>
    private static bool TryGetString(JsonElement value, out string text)
    {
        text = string.Empty;
        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        try
        {
            text = value.GetString()!;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static FieldResult Keep(FieldDefinition field, JsonElement value) => new(field.Key, value.Clone(), null);

    private static FieldResult Clear(FieldDefinition field) => new(field.Key, null, null);

    private static FieldResult Invalid(FieldDefinition field, string message) =>
        new(field.Key, null, new FailureDetail(WorkErrorCodes.ItemInvalidValue, field.Key, message));
}
