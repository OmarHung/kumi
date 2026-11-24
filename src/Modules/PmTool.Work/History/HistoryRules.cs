using System.Text.RegularExpressions;
using PmTool.Abstractions;
using PmTool.Work.Common;
using PmTool.Work.Items;

namespace PmTool.Work.History;

/// <summary>歷史匯入共用的檢查：來源、外部編號、原本的時間。請求本身的問題是 history.invalid，逐筆的問題是 item.import_invalid。</summary>
internal static partial class HistoryRules
{
    /// <summary>原本的時間可以比現在晚多少：兩邊的時鐘有一點誤差，不應該因此被擋下。</summary>
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    private static readonly DateTimeOffset Earliest = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public const int MaxFollowers = 50;

    /// <summary>請求本身的來源名字；null 表示合格。正規式的 $ 會讓結尾多一個換行的名字過關，所以另外擋。</summary>
    public static Failure? CheckSource(string? source) =>
        source is not null && SourceName().IsMatch(source) && !source.EndsWith('\n')
            ? null
            : WorkFailures.Invalid(
                WorkErrorCodes.HistoryInvalid, "source", "來源的名字要是小寫英文開頭，小寫英文、數字、_ 或 -，2 到 30 個字（例如 monday）。");

    /// <summary>外部編號已經匯入到呼叫者看不到的專案：不能透露那個專案或項目的編號。</summary>
    public static string ImportedElsewhere(string externalId) => $"外部編號{TextRules.Quote(externalId)}已經匯入到別的專案。";

    /// <summary>外部編號的問題；null 表示合格。</summary>
    public static string? ExternalIdProblem(string? externalId)
    {
        if (string.IsNullOrEmpty(externalId))
        {
            return "外部編號不能是空的。";
        }

        if (externalId.Length > HistorySource.MaxExternalIdLength)
        {
            return $"外部編號最多 {HistorySource.MaxExternalIdLength} 個字。";
        }

        return externalId.Any(char.IsWhiteSpace) || TextRules.HasForbiddenCharacters(externalId, allowLineBreaks: false)
            ? "外部編號不能有空白或控制字元。"
            : null;
    }

    /// <summary>原本的時間的問題；null 表示合格。</summary>
    public static string? TimeProblem(DateTimeOffset at, TimeProvider time)
    {
        if (at < Earliest)
        {
            return "時間不能早於 2000-01-01。";
        }

        return at > time.GetUtcNow() + ClockSkew ? "時間不能比現在晚。" : null;
    }

    /// <summary>資料庫的時間只到微秒、時區統一成 UTC：存進去與讀回來才一樣。</summary>
    public static DateTimeOffset Normalize(DateTimeOffset at)
    {
        var ticks = at.UtcTicks;
        return new DateTimeOffset(ticks - (ticks % TimeSpan.TicksPerMicrosecond), TimeSpan.Zero);
    }

    public static string KindText(HistoryRefKind kind) => kind switch
    {
        HistoryRefKind.Item => "item",
        HistoryRefKind.Comment => "comment",
        HistoryRefKind.Attachment => "attachment",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "不認得的外部編號種類。"),
    };

    public static HistoryRefKind ParseKind(string text) => text switch
    {
        "item" => HistoryRefKind.Item,
        "comment" => HistoryRefKind.Comment,
        "attachment" => HistoryRefKind.Attachment,
        _ => throw new InvalidOperationException($"資料表裡有不認得的外部編號種類 {text}。"),
    };

    /// <summary>逐筆的問題。<paramref name="collection"/> 是 rows 或 comments，欄位是 null 時指整筆。</summary>
    public static FailureDetail Problem(string collection, int index, string? field, string message) =>
        new(WorkErrorCodes.ImportInvalid, field is null ? $"{collection}/{index}" : $"{collection}/{index}/{field}", message);

    /// <summary>請求裡的請求本身（來源、筆數）有問題。</summary>
    public static Failure CountProblem(string path, int max, int actual) =>
        actual == 0
            ? WorkFailures.Invalid(WorkErrorCodes.HistoryInvalid, path, "沒有要匯入的東西。")
            : WorkFailures.Invalid(WorkErrorCodes.HistoryInvalid, path, $"一次最多 {max} 筆，這次有 {actual} 筆。請分成幾份。");

    [GeneratedRegex(HistorySource.Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex SourceName();
}
