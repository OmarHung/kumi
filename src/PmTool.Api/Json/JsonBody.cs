using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using PmTool.Api.Documents;
using PmTool.Api.Responses;

namespace PmTool.Api.Json;

/// <summary>
/// JSON 請求內文。不用框架的內文繫結：那一條路沒有大小上限（TestServer 與 chunked 上傳都擋不住）、
/// 讀內文沒有期限，失敗時回的也不是我們的信封。這裡讀法和驗證端點一樣，失敗時帶著要回給用戶端的錯誤。
/// </summary>
internal sealed class JsonBody<T>
    where T : class
{
    private const string BadValueMessage = "這個屬性的值不正確、型別不對，或不是認得的屬性。";

    private readonly T? _value;
    private readonly ApiFailure? _failure;

    private JsonBody(T? value, ApiFailure? failure)
    {
        _value = value;
        _failure = failure;
    }

    /// <summary>端點的參數是這個型別時，框架會呼叫它。一律回傳物件，不回 null，失敗由端點決定怎麼回。</summary>
    public static async ValueTask<JsonBody<T>?> BindAsync(HttpContext context)
    {
        if (!DocumentMediaTypes.IsJson(context.Request.ContentType, out var mediaTypeFailure))
        {
            return new JsonBody<T>(null, mediaTypeFailure);
        }

        var body = await Utf8BodyReader.ReadAsync(
            context.Request,
            JsonBodyLimit.For(context),
            TimeSpan.FromSeconds(ApiLimits.BodyReadTimeoutSeconds),
            context.RequestAborted);
        return body.Failure is { } bodyFailure ? new JsonBody<T>(null, bodyFailure) : Parse(body.Text);
    }

    public bool TryGetValue([NotNullWhen(true)] out T? value, [NotNullWhen(false)] out ApiFailure? failure)
    {
        value = _value;
        failure = _failure;
        return value is not null;
    }

    private static JsonBody<T> Parse(string text)
    {
        try
        {
            var value = JsonSerializer.Deserialize<T>(text, ApiJson.RequestOptions);
            return new JsonBody<T>(value, value is null ? ApiFailure.BadRequest : null);
        }
        catch (JsonException exception)
        {
            // 沒帶時區的時間：訊息是我們寫的，直接回給用戶端。
            var message = HasZoneless(exception) ? ZonedTimestamp.Message : BadValueMessage;

            // 例外的訊息是英文，還帶著 .NET 的型別名稱，不回給用戶端；只回出問題的位置。
            return new JsonBody<T>(null, ApiFailure.BadRequest with
            {
                Details = [new ApiErrorDetail(ApiErrorCodes.BadRequest, PathOf(exception), message)],
            });
        }
    }

    private static bool HasZoneless(Exception? exception)
    {
        for (; exception is not null; exception = exception.InnerException)
        {
            if (exception is ZonelessTimestampException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>JSON 路徑「$.fields.impact」寫成「fields.impact」，根是空字串，和其他細項的寫法一致。</summary>
    private static string PathOf(JsonException exception) =>
        (exception.Path ?? string.Empty).TrimStart('$').TrimStart('.');
}
