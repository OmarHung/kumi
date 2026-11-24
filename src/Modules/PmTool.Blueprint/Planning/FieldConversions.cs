using System.Collections.Frozen;
using PmTool.Blueprint.Model;

namespace PmTool.Blueprint.Planning;

internal enum ConversionSafety
{
    /// <summary>每一個既有的值都轉得過去。</summary>
    Lossless,

    /// <summary>有的值可能轉不過去，變更集要寫明那時候怎麼辦。</summary>
    NeedsFailurePolicy,

    NotSupported,
}

/// <summary>
/// 欄位改型別時允許哪些轉換。先只開確定說得清楚的幾種，其他一律不允許：
/// 開放容易，收回卻會讓已經套用過的藍圖失效。
/// </summary>
internal static class FieldConversions
{
    private static readonly FrozenSet<(FieldType From, FieldType To)> Lossless = new[]
    {
        (FieldType.SingleSelect, FieldType.MultiSelect),
        (FieldType.Text, FieldType.LongText),
        (FieldType.Number, FieldType.Text),
        (FieldType.Date, FieldType.Text),
        (FieldType.Url, FieldType.Text),
    }.ToFrozenSet();

    // 文字不一定解析得成數字或日期；多選可能選了不只一個；長文字可能有換行。
    private static readonly FrozenSet<(FieldType From, FieldType To)> NeedsFailurePolicy = new[]
    {
        (FieldType.Text, FieldType.Number),
        (FieldType.Text, FieldType.Date),
        (FieldType.MultiSelect, FieldType.SingleSelect),
        (FieldType.LongText, FieldType.Text),
    }.ToFrozenSet();

    public static ConversionSafety Classify(FieldType from, FieldType to) =>
        Lossless.Contains((from, to)) ? ConversionSafety.Lossless
        : NeedsFailurePolicy.Contains((from, to)) ? ConversionSafety.NeedsFailurePolicy
        : ConversionSafety.NotSupported;
}
