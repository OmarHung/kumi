using System.Globalization;
using PmTool.Abstractions;
using PmTool.Work.Persistence;
using static PmTool.Work.Common.TextRules;

namespace PmTool.Work.Items;

/// <summary>一個欄位檢查完的結果：可以寫進去的值，或擋下它的問題。</summary>
internal readonly record struct Checked<T>(Optional<T> Value, FailureDetail? Problem)
{
    public static Checked<T> Ok(Optional<T> value) => new(value, null);

    public static Checked<T> Fail(string code, string path, string message) => new(default, new FailureDetail(code, path, message));
}

/// <summary>核心欄位的檢查。沒給的欄位（HasValue 是 false）不檢查，原樣跳過。</summary>
internal static class CoreFieldRules
{
    public static Checked<string> Title(Optional<string> title)
    {
        if (!title.HasValue)
        {
            return Checked<string>.Ok(default);
        }

        var trimmed = title.Value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return Checked<string>.Fail(WorkErrorCodes.ItemTitleRequired, CoreFields.Title, "請填標題。");
        }

        if (trimmed.Length > WorkRules.MaxTitleLength)
        {
            return Checked<string>.Fail(
                WorkErrorCodes.ItemTitleTooLong, CoreFields.Title, $"標題最多 {WorkRules.MaxTitleLength} 個字，現在有 {trimmed.Length} 個字。");
        }

        return HasForbiddenCharacters(trimmed, allowLineBreaks: false)
            ? Checked<string>.Fail(WorkErrorCodes.ItemInvalidValue, CoreFields.Title, "標題不能有換行、控制字元或無效的字元。")
            : Checked<string>.Ok(trimmed);
    }

    /// <summary>
    /// 說明和標題一樣去掉頭尾空白再存，前後多一個換行才不會被當成改過；空的當成沒有值。
    /// 換行先統一成 \n，換行的寫法不同也不算改過。
    /// 可以有換行與 Tab，其他控制字元擋下來：NUL 存不進資料庫，其他的只會造成顯示問題。
    /// </summary>
    public static Checked<string?> Description(Optional<string?> description)
    {
        if (!description.HasValue)
        {
            return Checked<string?>.Ok(default);
        }

        var trimmed = NormalizeLineBreaks(description.Value)?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return Checked<string?>.Ok(new Optional<string?>(null));
        }

        if (trimmed.Length > WorkRules.MaxDescriptionLength)
        {
            return Checked<string?>.Fail(
                WorkErrorCodes.ItemDescriptionTooLong,
                CoreFields.Description,
                $"說明最多 {WorkRules.MaxDescriptionLength} 個字，現在有 {trimmed.Length} 個字。");
        }

        return HasForbiddenCharacters(trimmed, allowLineBreaks: true)
            ? Checked<string?>.Fail(WorkErrorCodes.ItemInvalidValue, CoreFields.Description, "說明不能有控制字元或無效的字元；換行與 Tab 可以。")
            : Checked<string?>.Ok(new Optional<string?>(trimmed));
    }

    /// <summary>列舉可以被轉型成任意整數，從 API 進來的值不一定是定義過的。</summary>
    public static Checked<Priority> Priority(Optional<Priority> priority) =>
        !priority.HasValue || Enum.IsDefined(priority.Value)
            ? Checked<Priority>.Ok(priority)
            : Checked<Priority>.Fail(WorkErrorCodes.ItemInvalidValue, CoreFields.Priority, "優先順序只能是無、低、中、高、緊急其中之一。");

    /// <summary>範圍與數字欄位相同，另外不能是負數；存的是去掉尾端 0 的值，回給呼叫端的和讀回來的寫法才一樣。</summary>
    public static Checked<decimal?> Estimate(Optional<decimal?> estimate)
    {
        if (!estimate.HasValue || estimate.Value is not { } value)
        {
            return Checked<decimal?>.Ok(estimate);
        }

        if (value < 0)
        {
            return Checked<decimal?>.Fail(WorkErrorCodes.ItemInvalidValue, CoreFields.Estimate, "估計不能是負數。");
        }

        return NumberRules.TryNormalize(value, out var normalized)
            ? Checked<decimal?>.Ok(new Optional<decimal?>(normalized))
            : Checked<decimal?>.Fail(
                WorkErrorCodes.ItemInvalidValue,
                CoreFields.Estimate,
                $"估計不能超過 {NumberRules.MaxText}，小數最多 {WorkRules.MaxNumberDecimalPlaces} 位。");
    }

    /// <summary>
    /// 到期日不能早於開始日。修改時拿合併之後的值比（只改其中一個也要和另一個舊的值比）；
    /// 兩個都沒改就不比，免得舊資料擋住不相干的修改。只有一個有值時沒有先後可比。
    /// </summary>
    public static FailureDetail? Schedule(Optional<DateOnly?> start, Optional<DateOnly?> due, ItemEntity? current)
    {
        if (!start.HasValue && !due.HasValue)
        {
            return null;
        }

        var startDate = start.HasValue ? start.Value : current?.StartDate;
        var dueDate = due.HasValue ? due.Value : current?.DueDate;
        return startDate is { } from && dueDate is { } to && to < from
            ? new FailureDetail(
                WorkErrorCodes.ItemInvalidValue,
                CoreFields.DueDate,
                $"到期日不能早於開始日：開始日是 {Format(from)}，到期日是 {Format(to)}。")
            : null;
    }

    private static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
