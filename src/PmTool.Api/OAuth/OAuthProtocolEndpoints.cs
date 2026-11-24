using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using PmTool.Api.Documents;
using PmTool.Api.RateLimiting;
using PmTool.Identity;

namespace PmTool.Api.OAuth;

/// <summary>
/// 標準路徑上的協定端點：不在 /v1 底下、不用登入、不用防偽標頭（<c>CsrfProtection</c> 只看 /v1）。
/// 錯誤是 RFC 6749 5.2 的 <c>{error, error_description}</c>，不是 API 的信封：MCP 用戶端只認這個格式。
/// 授權頁（<c>GET /oauth/authorize</c>）不是 API，是網頁的路由，這裡不接。
/// </summary>
internal static class OAuthProtocolEndpoints
{
    public const string MetadataPath = "/.well-known/oauth-authorization-server";

    private static readonly TimeSpan BodyTimeout = TimeSpan.FromSeconds(ApiLimits.BodyReadTimeoutSeconds);

    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        app.MapGet(MetadataPath, Metadata).WithName("OAuthAuthorizationServerMetadata");
        app.MapPost("/oauth/token", TokenAsync)
            .RequireRateLimiting(ApiRateLimiting.OAuthTokenPolicy)
            .WithName("OAuthToken");
        app.MapPost("/oauth/register", RegisterAsync)
            .RequireRateLimiting(ApiRateLimiting.OAuthRegisterPolicy)
            .WithName("OAuthRegister");
        app.MapPost("/oauth/revoke", RevokeAsync)
            .RequireRateLimiting(ApiRateLimiting.OAuthTokenPolicy)
            .WithName("OAuthRevoke");
        return app;
    }

    private static IResult Metadata(HttpContext context, [FromServices] OAuthServerResolver servers)
    {
        // 設了 PublicHost 才給用戶端與中間的快取留五分鐘（見 MetadataCacheControl）。
        context.Response.Headers.CacheControl = servers.MetadataCacheControl;
        return Results.Json(AuthorizationServerMetadata.For(servers.Resolve(context.Request)));
    }

    private static async Task<IResult> TokenAsync(
        HttpContext context,
        [FromServices] IOAuthService oauth,
        [FromServices] OAuthServerResolver servers,
        CancellationToken cancellationToken)
    {
        NoStore(context);
        var form = await ReadFormAsync(context, cancellationToken);
        if (form.Failure is { } failure)
        {
            return ErrorResult(failure);
        }

        var server = servers.Resolve(context.Request);
        var values = form.Value;
        var result = values.Get("grant_type") switch
        {
            "authorization_code" => await oauth.ExchangeCodeAsync(
                server,
                new OAuthCodeExchange(values.Get("code"), values.Get("redirect_uri"), values.Get("client_id"), values.Get("code_verifier"), values.Get("resource")),
                cancellationToken),
            "refresh_token" => await oauth.RefreshAsync(
                server,
                new OAuthRefreshRequest(values.Get("refresh_token"), values.Get("client_id"), values.Get("resource"), values.Get("scope")),
                cancellationToken),
            null => new OAuthFailure(OAuthError.InvalidRequest, "缺少 grant_type。"),
            _ => new OAuthFailure(OAuthError.UnsupportedGrantType, "只支援 authorization_code 與 refresh_token。"),
        };

        return result.Failure is { } denied ? ErrorResult(denied) : Results.Json(TokenResponse.From(result.Value));
    }

    private static async Task<IResult> RevokeAsync(
        HttpContext context, [FromServices] IOAuthService oauth, CancellationToken cancellationToken)
    {
        NoStore(context);
        var form = await ReadFormAsync(context, cancellationToken);

        // RFC 7009：不透露權杖存不存在，所以連格式不對的請求也回 200，只有讀不了內文時才回錯誤。
        if (form.Failure is { } failure)
        {
            return ErrorResult(failure);
        }

        if (form.Value.Get("token") is { Length: > 0 } token && token.Length <= ApiLimits.MaxOAuthBodyBytes)
        {
            await oauth.RevokeTokenAsync(token, cancellationToken);
        }

        return Results.Ok();
    }

    private static async Task<IResult> RegisterAsync(
        HttpContext context, [FromServices] IOAuthService oauth, CancellationToken cancellationToken)
    {
        NoStore(context);
        var body = await Utf8BodyReader.ReadAsync(context.Request, ApiLimits.MaxOAuthBodyBytes, BodyTimeout, cancellationToken);
        if (body.Failure is not null)
        {
            return ErrorResult(new OAuthFailure(OAuthError.InvalidClientMetadata, "內文讀不了，或超過上限。"));
        }

        if (!TryParseRegistration(body.Text, out var registration))
        {
            return ErrorResult(new OAuthFailure(OAuthError.InvalidClientMetadata, "內文要是 JSON 物件，欄位的型別要正確。"));
        }

        var result = await oauth.RegisterClientAsync(registration, cancellationToken);
        return result.Failure is { } failure
            ? ErrorResult(failure)
            : Results.Json(RegistrationResponse.From(result.Value), statusCode: StatusCodes.Status201Created);
    }

    /// <summary>RFC 7591：不認得的欄位略過（用戶端常多送 application_type、scope 之類的），所以不用嚴格的請求內文繫結。</summary>
    private static bool TryParseRegistration(string text, out OAuthClientRegistration registration)
    {
        registration = null!;
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryString(root, "client_name", out var name)
                || !TryString(root, "token_endpoint_auth_method", out var method)
                || !TryStrings(root, "redirect_uris", out var uris)
                || !TryStrings(root, "grant_types", out var grants)
                || !TryStrings(root, "response_types", out var responses))
            {
                return false;
            }

            registration = new OAuthClientRegistration(name, uris, method, grants, responses);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>沒寫或 null 是 null；是字串就取值；其他型別不接受。</summary>
    private static bool TryString(JsonElement root, string name, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        value = element.ValueKind == JsonValueKind.String ? element.GetString() : null;
        return element.ValueKind == JsonValueKind.String;
    }

    private static bool TryStrings(JsonElement root, string name, out IReadOnlyList<string>? values)
    {
        values = null;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Array || element.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
        {
            return false;
        }

        values = [.. element.EnumerateArray().Select(item => item.GetString()!)];
        return true;
    }

    private static async Task<FormResult> ReadFormAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (!context.Request.HasFormContentType
            || !context.Request.ContentType!.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            return new FormResult(null, new OAuthFailure(OAuthError.InvalidRequest, "內文要用 application/x-www-form-urlencoded。"));
        }

        var body = await Utf8BodyReader.ReadAsync(context.Request, ApiLimits.MaxOAuthBodyBytes, BodyTimeout, cancellationToken);
        if (body.Failure is not null)
        {
            return new FormResult(null, new OAuthFailure(OAuthError.InvalidRequest, "內文讀不了，或超過上限。"));
        }

        var parsed = QueryHelpers.ParseQuery(body.Text);

        // RFC 6749 3.2：同一個參數不能出現兩次。
        return parsed.Values.Any(value => value.Count > 1)
            ? new FormResult(null, new OAuthFailure(OAuthError.InvalidRequest, "同一個參數不能出現兩次。"))
            : new FormResult(new FormValues(parsed), null);
    }

    /// <summary>token 端點的回應不能被快取（RFC 6749 5.1）。</summary>
    private static void NoStore(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
    }

    /// <summary>RFC 6749 5.2：400，用戶端不認得是 401；伺服器自己的問題（抓不到用戶端資料）是 503。</summary>
    private static IResult ErrorResult(OAuthFailure failure)
    {
        var status = failure.Error switch
        {
            OAuthError.InvalidClient => StatusCodes.Status401Unauthorized,
            OAuthError.TemporarilyUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest,
        };
        return Results.Json(new OAuthErrorResponse(failure.Error, failure.Description), statusCode: status);
    }

    private sealed record FormResult(FormValues? Values, OAuthFailure? Failure)
    {
        public FormValues Value => Values!;
    }

    private sealed class FormValues(Dictionary<string, StringValues> values)
    {
        public string? Get(string name) => values.TryGetValue(name, out var value) && value.Count == 1 ? value[0] : null;
    }
}
