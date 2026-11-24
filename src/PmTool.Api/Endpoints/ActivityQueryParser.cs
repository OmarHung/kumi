using PmTool.Abstractions;
using PmTool.Api.Json;
using PmTool.Work;

namespace PmTool.Api.Endpoints;

/// <summary>活動紀錄的查詢參數轉成 <see cref="ActivityQuery"/>，收法和項目清單的 limit、cursor 一樣。不認得的參數不理會。</summary>
internal static class ActivityQueryParser
{
    private const string Kind = "kind";

    public static Result<ActivityQuery> Parse(IQueryCollection query)
    {
        var limit = QueryParameters.LimitOf(query, ActivityQuery.DefaultLimit);
        var cursor = QueryParameters.Single(query, QueryParameters.Cursor);
        var kind = QueryParameters.Single(query, Kind).Then(ParseKind);
        return QueryParameters.FailureOf(limit.Problem, cursor.Problem, kind.Problem) is { } failure
            ? failure
            : new ActivityQuery { Limit = limit.Value, Cursor = cursor.Value, Kind = kind.Value };
    }

    private static Parsed<ActivityKind?> ParseKind(string? value) =>
        value is null ? new Parsed<ActivityKind?>(null)
        : SnakeCaseEnums.TryParse<ActivityKind>(value, out var kind) ? new Parsed<ActivityKind?>(kind)
        : Parsed<ActivityKind?>.Invalid(null, Kind, $"不認得的活動種類：{value}。可以用的是：{string.Join("、", SnakeCaseEnums.AllNames<ActivityKind>().Order())}。");
}
