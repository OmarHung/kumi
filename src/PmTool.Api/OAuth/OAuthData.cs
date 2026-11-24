using System.Text.Json.Serialization;
using PmTool.Identity;

namespace PmTool.Api.OAuth;

// 協定端點（中繼資料、token、註冊）的形狀是 RFC 定義的 snake_case，用 JsonPropertyName 寫死，不跟 API 其他地方的 camelCase。

/// <summary>RFC 8414 的授權伺服器中繼資料。</summary>
internal sealed record AuthorizationServerMetadata(
    [property: JsonPropertyName("issuer")] string Issuer,
    [property: JsonPropertyName("authorization_endpoint")] string AuthorizationEndpoint,
    [property: JsonPropertyName("token_endpoint")] string TokenEndpoint,
    [property: JsonPropertyName("registration_endpoint")] string RegistrationEndpoint,
    [property: JsonPropertyName("revocation_endpoint")] string RevocationEndpoint,
    [property: JsonPropertyName("response_types_supported")] IReadOnlyList<string> ResponseTypesSupported,
    [property: JsonPropertyName("grant_types_supported")] IReadOnlyList<string> GrantTypesSupported,
    [property: JsonPropertyName("code_challenge_methods_supported")] IReadOnlyList<string> CodeChallengeMethodsSupported,
    [property: JsonPropertyName("token_endpoint_auth_methods_supported")] IReadOnlyList<string> TokenEndpointAuthMethodsSupported,
    [property: JsonPropertyName("revocation_endpoint_auth_methods_supported")] IReadOnlyList<string> RevocationEndpointAuthMethodsSupported,
    [property: JsonPropertyName("scopes_supported")] IReadOnlyList<string> ScopesSupported,
    [property: JsonPropertyName("client_id_metadata_document_supported")] bool ClientIdMetadataDocumentSupported,
    [property: JsonPropertyName("authorization_response_iss_parameter_supported")] bool AuthorizationResponseIssParameterSupported)
{
    public static AuthorizationServerMetadata For(OAuthServer server) => new(
        server.Issuer,
        server.Issuer + "/oauth/authorize",
        server.Issuer + "/oauth/token",
        server.Issuer + "/oauth/register",
        server.Issuer + "/oauth/revoke",
        ["code"],
        ["authorization_code", "refresh_token"],
        ["S256"],
        ["none"],
        ["none"],
        [.. AccessTokenScopes.Grantable.Select(AccessTokenScopes.NameOf)],
        true,
        true);
}

/// <summary>RFC 6749 5.1。</summary>
internal sealed record TokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("scope")] string Scope)
{
    public static TokenResponse From(OAuthTokens tokens) =>
        new(tokens.AccessToken, "Bearer", tokens.ExpiresIn, tokens.RefreshToken, tokens.Scope);
}

/// <summary>RFC 6749 5.2。</summary>
internal sealed record OAuthErrorResponse(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("error_description")] string ErrorDescription);

/// <summary>RFC 7591 3.2.1。</summary>
internal sealed record RegistrationResponse(
    [property: JsonPropertyName("client_id")] string ClientId,
    [property: JsonPropertyName("client_id_issued_at")] long ClientIdIssuedAt,
    [property: JsonPropertyName("client_name")] string ClientName,
    [property: JsonPropertyName("redirect_uris")] IReadOnlyList<string> RedirectUris,
    [property: JsonPropertyName("token_endpoint_auth_method")] string TokenEndpointAuthMethod,
    [property: JsonPropertyName("grant_types")] IReadOnlyList<string> GrantTypes,
    [property: JsonPropertyName("response_types")] IReadOnlyList<string> ResponseTypes)
{
    public static RegistrationResponse From(OAuthRegisteredClient client) => new(
        client.ClientId,
        client.IssuedAt.ToUnixTimeSeconds(),
        client.ClientName,
        client.RedirectUris,
        "none",
        ["authorization_code", "refresh_token"],
        ["code"]);
}

// 同意頁的端點是 API 的一部分，照 API 的 camelCase。

/// <summary>授權請求的參數，網頁從網址列讀來原樣送上來。</summary>
internal sealed record AuthorizeRequestBody(
    string? ResponseType = null,
    string? ClientId = null,
    string? RedirectUri = null,
    string? Scope = null,
    string? State = null,
    string? CodeChallenge = null,
    string? CodeChallengeMethod = null,
    string? Resource = null)
{
    public OAuthAuthorizeParameters ToParameters() =>
        new(ResponseType, ClientId, RedirectUri, Scope, State, CodeChallenge, CodeChallengeMethod, Resource);
}

/// <summary>「允許」：授權請求的參數加上使用者勾的範圍。</summary>
internal sealed record ApproveRequestBody(
    string? ResponseType = null,
    string? ClientId = null,
    string? RedirectUri = null,
    string? Scope = null,
    string? State = null,
    string? CodeChallenge = null,
    string? CodeChallengeMethod = null,
    string? Resource = null,
    IReadOnlyList<string>? Scopes = null)
{
    public OAuthAuthorizeParameters ToParameters() =>
        new(ResponseType, ClientId, RedirectUri, Scope, State, CodeChallenge, CodeChallengeMethod, Resource);
}

internal sealed record AuthorizationAccountData(string DisplayName, string Email, string OrganizationName);

internal sealed record AuthorizationCheckData(
    string ClientId,
    string ClientName,
    string? ClientHost,
    string RedirectHost,
    bool RedirectIsLoopback,
    IReadOnlyList<string> RequestedScopes,
    AuthorizationAccountData Account);

internal sealed record RedirectData(string RedirectTo);

/// <summary>放在 <c>error.data</c>：OAuth 的錯誤代碼，與（用戶端驗過時）帶著錯誤導回去的網址。</summary>
internal sealed record OAuthRequestErrorData(string Error, string? RedirectTo);

internal sealed record OAuthGrantData(
    Guid GrantId,
    string ClientName,
    string? ClientHost,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt)
{
    public static OAuthGrantData From(OAuthGrantSummary grant) => new(
        grant.GrantId, grant.ClientName, grant.ClientHost, [.. grant.Scopes.Select(AccessTokenScopes.NameOf)], grant.CreatedAt, grant.LastUsedAt);
}
