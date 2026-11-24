using PmTool.Blueprint.Model;
using static PmTool.Blueprint.IssueText;

namespace PmTool.Blueprint.Validation.Rules;

/// <summary>規則 2 與 3：自訂欄位本身。兩條規則各自依欄位順序走一遍。</summary>
internal static class FieldRules
{
    /// <summary>
    /// 規則 2：自訂欄位不能和核心欄位同名。新寫入的文件還不能叫週期、里程碑的名字（<see cref="BlueprintVocabulary.ReservedFieldKeys"/>）；
    /// 讀回已存的文件只擋核心欄位，早就存著、剛好用了那兩個名字的藍圖才讀得回來。
    /// </summary>
    public static IEnumerable<ValidationIssue> CheckCoreNames(ProjectBlueprint blueprint, ValidationMode mode)
    {
        var reserved = mode == ValidationMode.Authoring ? BlueprintVocabulary.ReservedFieldKeys : BlueprintVocabulary.CoreFields;
        for (var i = 0; i < blueprint.Fields.Count; i++)
        {
            var key = blueprint.Fields[i].Key;
            if (reserved.Contains(key))
            {
                yield return new ValidationIssue(
                    IssueCodes.FieldCoreNameConflict,
                    $"/fields/{i}/key",
                    $"自訂欄位{Quote(key)}和內建欄位同名。內建欄位不必也不能在 fields 裡宣告。");
            }
        }
    }

    /// <summary>規則 3：選項與幣別。同一個欄位先回報選項的問題，再回報幣別的問題。</summary>
    public static IEnumerable<ValidationIssue> CheckShapes(ProjectBlueprint blueprint)
    {
        for (var i = 0; i < blueprint.Fields.Count; i++)
        {
            var field = blueprint.Fields[i];
            var path = $"/fields/{i}";
            if (OptionsIssue(field, path) is { } optionsIssue)
            {
                yield return optionsIssue;
            }

            if (CurrencyIssue(field, path) is { } currencyIssue)
            {
                yield return currencyIssue;
            }
        }
    }

    // options 是 null 表示沒寫，指到欄位本身；寫了空清單指到 options。非選項型欄位寫了空清單也算寫了。
    private static ValidationIssue? OptionsIssue(FieldDefinition field, string path) => field switch
    {
        { IsSelect: true, Options: null } => new ValidationIssue(
            IssueCodes.FieldOptionsRequired, path, $"選項型欄位{Quote(field.Key)}必須有 options，而且至少一個選項。"),
        { IsSelect: true, Options.Count: 0 } => new ValidationIssue(
            IssueCodes.FieldOptionsRequired, $"{path}/options", $"選項型欄位{Quote(field.Key)}的 options 至少要有一個選項。"),
        { IsSelect: false, Options: not null } => new ValidationIssue(
            IssueCodes.FieldOptionsNotAllowed,
            $"{path}/options",
            $"欄位{Quote(field.Key)}不是單選或多選，不能寫 options（空清單也不行）。"),
        _ => null,
    };

    // 不該有幣別的欄位只回報「不能有」，不再檢查代碼本身：拿掉它就好，代碼對不對已經無關。
    private static ValidationIssue? CurrencyIssue(FieldDefinition field, string path) =>
        (field.Type == FieldType.Currency, field.Currency) switch
        {
            (true, null) => new ValidationIssue(
                IssueCodes.FieldCurrencyRequired, path, $"金額欄位{Quote(field.Key)}必須用 currency 指定幣別，例如 TWD。"),
            (false, not null) => new ValidationIssue(
                IssueCodes.FieldCurrencyNotAllowed, $"{path}/currency", $"欄位{Quote(field.Key)}不是金額欄位，不能指定幣別。"),
            (true, { } code) when !StrictText.IsCurrencyCode(code) => new ValidationIssue(
                IssueCodes.FieldCurrencyCode,
                $"{path}/currency",
                $"欄位{Quote(field.Key)}的幣別{Quote(code)}不是三個大寫英文字母的 ISO 4217 代碼。"),
            _ => null,
        };
}
