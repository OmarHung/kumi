using PmTool.Abstractions;
using PmTool.Api.Responses;

namespace PmTool.Api.Endpoints;

/// <summary>端點共用的回應寫法，成功與失敗都包進信封。</summary>
internal static class EndpointResults
{
    public static IResult Ok<T>(T data) => Results.Ok(ApiEnvelope.Ok(data));

    public static IResult Created<T>(string? location, T data) => Results.Created(location, ApiEnvelope.Ok(data));

    public static IResult Fail(Failure failure) => failure.ToApiFailure().ToResult();

    public static async Task<IResult> OkOrFailAsync<T>(Result<T> result, Func<T, Task<IResult>> onSuccess) =>
        result.Failure is { } failure ? Fail(failure) : await onSuccess(result.Value);
}
