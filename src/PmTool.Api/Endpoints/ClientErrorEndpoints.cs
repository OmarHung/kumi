using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Api.Json;
using PmTool.Api.RateLimiting;
using PmTool.Api.Responses;
using PmTool.Api.Security;

namespace PmTool.Api.Endpoints;

/// <summary>網頁回報的錯誤是怎麼發生的：window 的 error、沒接住的 Promise、路由的錯誤頁。</summary>
internal enum ClientErrorKind
{
    Error,
    UnhandledRejection,
    Render,
}

/// <summary>網頁回報的一筆錯誤。堆疊可以沒有（有些瀏覽器的例外沒有堆疊），網頁那時可以送 null 或不送。</summary>
internal sealed record ClientErrorReport(ClientErrorKind Kind, string Message, string Path, string? Stack = null);

/// <summary>
/// 網頁的例外記到伺服器，和伺服器自己的記錄在同一個地方（Warning 等級、固定的類別：網頁的錯誤不是伺服器的錯，不該讓維運把它當成伺服器出錯）。不存資料庫。
/// 只記人員編號，不記名稱與 Email：記錄會被複製、轉寄，裡面不該有個人資料。
/// </summary>
internal static partial class ClientErrorEndpoints
{
    /// <summary>記錄的類別。維運的腳本靠它把網頁回報的錯誤和伺服器自己的分開。</summary>
    public const string LogCategory = "PmTool.Api.ClientErrors";

    /// <summary>全站的上限：替換 <see cref="ClientErrorLimits"/> 就能換掉它的數字。</summary>
    public static IServiceCollection AddClientErrorReporting(this IServiceCollection services)
    {
        services.AddSingleton(new ClientErrorLimits());
        services.AddSingleton<ClientErrorBudget>();
        services.AddSingleton<ClientErrorDeduplicator>();
        return services;
    }

    public static RouteGroupBuilder MapClientErrorEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapPost("/client-errors", Report)
            .WithName("ReportClientError")
            .RequireRateLimiting(ApiRateLimiting.ClientErrorPolicy);
        return v1;
    }

    private static IResult Report(
        JsonBody<ClientErrorReport> body,
        ClaimsPrincipal principal,
        HttpContext context,
        [FromServices] ClientErrorBudget budget,
        [FromServices] ClientErrorDeduplicator deduplicator,
        [FromServices] ILoggerFactory loggers)
    {
        if (!body.TryGetValue(out var report, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        IReadOnlyList<ApiErrorDetail> problems = [.. Problems(report)];
        if (problems.Count > 0)
        {
            return (ApiFailure.BadRequest with { Details = problems }).ToResult();
        }

        // 重複的和成功的回應一樣，但不記、也不占全站的額度。
        var kind = SnakeCaseEnums.NameOf(report.Kind);
        var message = Clean(report.Message);
        var personId = SessionPrincipal.Read(principal).PersonId;
        if (!deduplicator.ShouldLog(personId, kind, message))
        {
            return EndpointResults.Ok<object?>(null);
        }

        // 驗證之後才拿全站的額度：格式不對的請求不必占掉別人的額度。
        if (!budget.TryTake(out var retryAfter))
        {
            context.Response.Headers.RetryAfter = retryAfter;
            return ApiFailure.RateLimited.ToResult();
        }

        LogClientError(
            loggers.CreateLogger(LogCategory),
            kind,
            message,
            Clean(WithoutQuery(report.Path)),
            personId,
            report.Stack is null ? null : Clean(report.Stack));
        return EndpointResults.Ok<object?>(null);
    }

    private static IEnumerable<ApiErrorDetail> Problems(ClientErrorReport report)
    {
        if (report.Message.Length is 0 or > ApiLimits.MaxClientErrorMessageLength)
        {
            yield return Problem("message", $"訊息要有 1 到 {ApiLimits.MaxClientErrorMessageLength} 個字。");
        }

        if (report.Stack?.Length > ApiLimits.MaxClientErrorStackLength)
        {
            yield return Problem("stack", $"堆疊不能超過 {ApiLimits.MaxClientErrorStackLength} 個字。");
        }

        if (!report.Path.StartsWith('/') || report.Path.Length > ApiLimits.MaxClientErrorPathLength)
        {
            yield return Problem("path", $"路徑要以 / 開頭，不能超過 {ApiLimits.MaxClientErrorPathLength} 個字。");
        }
    }

    private static ApiErrorDetail Problem(string path, string message) => new(ApiErrorCodes.BadRequest, path, message);

    /// <summary>網頁只送路徑；萬一帶了查詢字串或片段，也不記下來，裡面可能有權杖或搜尋的內容。</summary>
    private static string WithoutQuery(string path) =>
        path.IndexOfAny(['?', '#']) is var cut and >= 0 ? path[..cut] : path;

    /// <summary>
    /// 控制字元（包含換行）換成空白而不是擋下來：這是診斷資料，擋了就什麼都沒記到。換行會讓一筆記錄看起來像好幾筆。
    /// 格式字元（雙向控制、零寬字元）與行、段落分隔字元也換掉：errors.sh 解碼後印到終端機時，
    /// 它們能把訊息顯示成誤導的樣子。這幾類幾乎都在 BMP 裡，逐字看就夠；落單的代理字元在讀 JSON 時就被擋下了。
    /// </summary>
    private static string Clean(string text) =>
        string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = IsUnsafeToPrint(source[i]) ? ' ' : source[i];
            }
        });

    private static bool IsUnsafeToPrint(char character) =>
        char.IsControl(character)
        || CharUnicodeInfo.GetUnicodeCategory(character)
            is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;

    // 屬性名稱不用 Message：JSON 格式的記錄已經有一個 Message（整句訊息），同名會寫出重複的鍵。
    [LoggerMessage(Level = LogLevel.Warning, Message = "網頁回報的錯誤（{Kind}）：{ErrorMessage}；路徑 {Path}；人員 {PersonId}；堆疊 {Stack}")]
    private static partial void LogClientError(ILogger logger, string kind, string errorMessage, string path, Guid personId, string? stack);
}
