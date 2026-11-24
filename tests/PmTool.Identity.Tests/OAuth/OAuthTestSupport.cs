using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Identity.OAuth;

namespace PmTool.Identity.Tests.OAuth;

/// <summary>OAuth 測試共用的步驟：假的 HTTP 回應、PKCE、註冊用戶端、走完一次授權。</summary>
internal static class OAuthTestSupport
{
    public const string Loopback = "http://127.0.0.1:5000/callback";

    public static readonly OAuthServer Server = new("https://pm.example.com", "https://pm.example.com/mcp");

    /// <summary>開一個模組：時鐘由測試控制，抓 Client ID Metadata Document 走假的 handler（不連網路）。</summary>
    public static Task<IdentityTestHost> StartAsync(
        ManualTimeProvider time, HttpMessageHandler? handler = null, TimeSpan? fetchTimeout = null, Action<IServiceCollection>? configure = null) =>
        IdentityTestHost.StartAsync(services =>
        {
            services.AddSingleton<TimeProvider>(time);
            configure?.Invoke(services);
            if (handler is not null || fetchTimeout is not null)
            {
                services.AddSingleton(new OAuthFetchSettings { Handler = handler, Timeout = fetchTimeout ?? OAuthRules.MetadataFetchTimeout });
            }
        });

    public static (string Verifier, string Challenge) NewPkce()
    {
        var verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        return (verifier, Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    public static OAuthAuthorizeParameters Parameters(
        string clientId, string challenge, string redirectUri = Loopback, string? scope = null, string? state = "state-1") =>
        new("code", clientId, redirectUri, scope, state, challenge, "S256", Server.Resource);

    public static async Task<OAuthRegisteredClient> RegisterAsync(
        this IdentityTestHost host, string name = "範例代理", params string[] redirectUris)
    {
        var uris = redirectUris.Length == 0 ? new[] { Loopback } : redirectUris;
        var result = await host.OAuth.RegisterClientAsync(new OAuthClientRegistration(name, uris, "none", null, null), CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, result.Failure?.Description);
        return result.Value;
    }

    /// <summary>直接把一個動態註冊的用戶端寫進資料表，繞過註冊時的檢查（模擬不乾淨的資料）。回傳它的 client_id。</summary>
    public static async Task<string> InsertRegisteredClientAsync(this IdentityTestHost host, params string[] redirectUris)
    {
        var clientId = OAuthRules.RegisteredClientPrefix + Guid.NewGuid().ToString("N");
        await using var db = await host.CreateDbContextAsync();
        db.OAuthClients.Add(new PmTool.Identity.Persistence.OAuthClientRow
        {
            Id = Guid.CreateVersion7(),
            Kind = PmTool.Identity.Persistence.OAuthClientRow.RegisteredKind,
            ClientId = clientId,
            Name = "直接寫入的用戶端",
            RedirectUris = redirectUris,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return clientId;
    }

    /// <summary>網址的查詢參數。</summary>
    public static System.Collections.Specialized.NameValueCollection QueryOf(string url) => HttpUtility.ParseQueryString(new Uri(url).Query);

    /// <summary>授權一次，回傳授權碼與 PKCE 的 verifier。</summary>
    public static async Task<(string Code, string Verifier)> ApproveAsync(
        this IdentityTestHost host,
        AuthenticatedUser user,
        string clientId,
        string redirectUri = Loopback,
        IReadOnlyList<AccessTokenScope>? scopes = null)
    {
        var (verifier, challenge) = NewPkce();
        var result = await host.OAuth.ApproveAsync(
            Server, user.ToActor(), Parameters(clientId, challenge, redirectUri), scopes ?? AccessTokenScopes.Grantable, CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, result.Failure?.Description);
        return (QueryOf(result.Value)["code"]!, verifier);
    }

    public static Task<OAuthResult<OAuthTokens>> ExchangeAsync(
        this IdentityTestHost host, string clientId, string code, string verifier, string redirectUri = Loopback) =>
        host.OAuth.ExchangeCodeAsync(Server, new OAuthCodeExchange(code, redirectUri, clientId, verifier, Server.Resource), CancellationToken.None);

    public static Task<OAuthResult<OAuthTokens>> RefreshAsync(
        this IdentityTestHost host, string clientId, string refreshToken, string? scope = null) =>
        host.OAuth.RefreshAsync(Server, new OAuthRefreshRequest(refreshToken, clientId, Server.Resource, scope), CancellationToken.None);

    /// <summary>註冊用戶端、授權、換權杖，一次做完。</summary>
    public static async Task<(OAuthRegisteredClient Client, OAuthTokens Tokens)> ConnectAsync(
        this IdentityTestHost host, AuthenticatedUser user, string name = "範例代理")
    {
        var client = await host.RegisterAsync(name);
        var (code, verifier) = await host.ApproveAsync(user, client.ClientId);
        var tokens = await host.ExchangeAsync(client.ClientId, code, verifier);
        Assert.That(tokens.Succeeded, Is.True, tokens.Failure?.Description);
        return (client, tokens.Value);
    }
}

/// <summary>假的 HTTP 伺服器：每個請求交給測試給的函式，並記下被呼叫幾次。</summary>
internal sealed class FakeMetadataHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public int Calls { get; private set; }

    public static FakeMetadataHandler Json(string body, string? cacheControl = null) =>
        new((_, _) => Task.FromResult(JsonResponse(body, cacheControl)));

    public static HttpResponseMessage JsonResponse(string body, string? cacheControl = null)
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (cacheControl is not null)
        {
            response.Headers.TryAddWithoutValidation("Cache-Control", cacheControl);
        }

        return response;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        return respond(request, cancellationToken);
    }
}

/// <summary>記下所有記錄的假記錄器，測試用來確認「對外一樣、記錄分開」。</summary>
internal sealed class CapturingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
{
    private readonly List<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(
        Microsoft.Extensions.Logging.LogLevel logLevel,
        Microsoft.Extensions.Logging.EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_entries)
        {
            _entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
