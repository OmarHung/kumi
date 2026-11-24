using PmTool.Api.Documents;
using PmTool.Api.RateLimiting;
using PmTool.Api.Responses;
using PmTool.Api.Security;
using PmTool.Blueprint;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 兩個驗證端點刻意不需要登入：它們是純函式，不讀寫任何資料，又有內文大小上限與限流。
/// 內文自己讀，不用框架的模型繫結，才能依 Content-Type 分辨 JSON 與 YAML，並控制最多讀多少。
/// 同時處理的數量與讀內文的時間也都有上限，見 <see cref="ValidationSlots"/>。
/// </summary>
internal static class ValidationEndpoints
{
    public const string BlueprintRoute = "/v1/blueprints/validate";
    public const string ChangeSetRoute = "/v1/changesets/validate";

    // 位置滿了多半只是一下子，請用戶端一秒後再試。
    private const string BusyRetryAfterSeconds = "1";

    public static IServiceCollection AddValidationEndpoints(this IServiceCollection services)
    {
        services.AddSingleton(new ValidationLoadLimits());
        services.AddSingleton<ValidationSlots>();
        return services;
    }

    public static IEndpointRouteBuilder MapValidationEndpoints(this IEndpointRouteBuilder app)
    {
        // 不需要防偽標頭：它們不讀寫任何資料，也不看工作階段，被別的網站觸發也冒用不了什麼。
        app.MapPost(BlueprintRoute, ValidateBlueprintAsync)
            .WithName("ValidateBlueprint")
            .ExemptFromCsrfCheck()
            .RequireRateLimiting(ApiRateLimiting.ValidationPolicy)
            .Produces<ApiEnvelope<ValidationResultData>>();
        app.MapPost(ChangeSetRoute, ValidateChangeSetAsync)
            .WithName("ValidateChangeSet")
            .ExemptFromCsrfCheck()
            .RequireRateLimiting(ApiRateLimiting.ValidationPolicy)
            .Produces<ApiEnvelope<ValidationResultData>>();
        return app;
    }

    private static Task<IResult> ValidateBlueprintAsync(
        HttpRequest request, IBlueprintValidator validator, ValidationSlots slots, CancellationToken cancellationToken) =>
        ValidateAsync(request, slots, (text, format) => validator.ValidateBlueprint(text, format).Report, cancellationToken);

    private static Task<IResult> ValidateChangeSetAsync(
        HttpRequest request, IBlueprintValidator validator, ValidationSlots slots, CancellationToken cancellationToken) =>
        ValidateAsync(request, slots, (text, format) => validator.ValidateChangeSet(text, format).Report, cancellationToken);

    private static async Task<IResult> ValidateAsync(
        HttpRequest request,
        ValidationSlots slots,
        Func<string, DocumentFormat, ValidationReport> validate,
        CancellationToken cancellationToken)
    {
        // 只看標頭就能拒絕的先拒絕，不占位置。
        if (!DocumentMediaTypes.TryResolve(request.ContentType, out var format, out var mediaTypeFailure))
        {
            return mediaTypeFailure.ToResult();
        }

        if (!slots.TryEnter())
        {
            request.HttpContext.Response.Headers.RetryAfter = BusyRetryAfterSeconds;
            return ApiFailure.Busy.ToResult();
        }

        try
        {
            var body = await Utf8BodyReader.ReadDocumentAsync(request, slots.BodyReadTimeout, cancellationToken);
            return body.Failure is { } bodyFailure
                ? bodyFailure.ToResult()
                : Results.Ok(ApiEnvelope.Ok(ValidationResultData.From(validate(body.Text, format))));
        }
        finally
        {
            slots.Exit();
        }
    }
}
