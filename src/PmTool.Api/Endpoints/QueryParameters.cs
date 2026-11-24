using System.Globalization;
using Microsoft.Extensions.Primitives;
using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 清單端點共用的查詢參數規則。同一種參數在每個清單的收法一樣：空值當作沒給（網頁常把空的欄位照樣送出來）、
/// 只能給一次的參數給了兩次是錯、數字的範圍與游標的內容由模組判斷，規則只有一份。
/// </summary>
internal static class QueryParameters
{
    public const string Limit = "limit";

    public const string Cursor = "cursor";

    public static Parsed<string?> Single(IQueryCollection query, string name)
    {
        var values = NonEmpty(query[name]);
        return values.Count > 1
            ? Parsed<string?>.Invalid(null, name, $"{name} 只能給一次。")
            : new Parsed<string?>(values.Count == 1 ? values[0] : null);
    }

    /// <summary>這裡只把字串變成整數；範圍由模組檢查，超出時模組回的也是 query.invalid。</summary>
    public static Parsed<int> LimitOf(IQueryCollection query, int fallback) =>
        Single(query, Limit).Then(value =>
            value is null ? new Parsed<int>(fallback)
            : int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) ? new Parsed<int>(limit)
            : Parsed<int>.Invalid(fallback, Limit, "limit 要是整數。"));

    /// <summary>
    /// 搜尋端點（<c>/v1/search/*</c>）共用的 q 與 limit。文字的長度與字元由模組檢查；這裡只確認有給、只給一次，
    /// 缺的 q 和空的 q 一樣是錯。有問題時 Failure 是一次回齊的 422，不該再呼叫模組。
    /// </summary>
    public static (string? Text, int Limit, Failure? Failure) SearchOf(IQueryCollection query, int defaultLimit)
    {
        var text = Single(query, "q");
        var limit = LimitOf(query, defaultLimit);
        var missing = text is { Value: null, Problem: null }
            ? new FailureDetail(WorkErrorCodes.QueryInvalid, "q", "要給搜尋的文字 q。")
            : null;
        return (text.Value, limit.Value, FailureOf(text.Problem, missing, limit.Problem));
    }

    public static IReadOnlyList<string> NonEmpty(StringValues values) =>
        [.. values.Where(value => !string.IsNullOrEmpty(value)).Select(value => value!)];

    /// <summary>把各個參數的問題合成一個失敗，一次回報全部；都沒有問題時是 null。</summary>
    public static Failure? FailureOf(params FailureDetail?[] problems)
    {
        FailureDetail[] found = [.. problems.OfType<FailureDetail>()];
        return found.Length == 0
            ? null
            : new Failure(FailureKind.Invalid, WorkErrorCodes.QueryInvalid, "查詢參數不正確。") { Details = found };
    }
}

/// <summary>一個參數的解析結果。失敗時也帶一個預設值，其他參數才能繼續解析，一次回報所有問題。</summary>
internal sealed record Parsed<T>(T Value, FailureDetail? Problem = null)
{
    public static Parsed<T> Invalid(T fallback, string parameter, string message) =>
        new(fallback, new FailureDetail(WorkErrorCodes.QueryInvalid, parameter, message));

    // 前一步已經失敗時，它的值是 null，下一步會給出預設值；問題沿用前一步的。
    public Parsed<TNext> Then<TNext>(Func<T, Parsed<TNext>> next) =>
        Problem is null ? next(Value) : next(Value) with { Problem = Problem };
}
