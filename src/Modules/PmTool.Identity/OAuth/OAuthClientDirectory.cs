using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PmTool.Http;
using PmTool.Identity.Persistence;

namespace PmTool.Identity.OAuth;

/// <summary>查到的用戶端：授權請求要用的資料。</summary>
/// <param name="Host">Client ID Metadata Document 的網域；動態註冊的是 null。</param>
internal sealed record ResolvedClient(string ClientId, string Name, string? Host, IReadOnlyList<string> RedirectUris);

/// <summary>
/// 用戶端是誰：動態註冊的（<c>pmc_…</c>，查資料表）或 Client ID Metadata Document（<c>https://…</c>，去抓，有快取）。
/// 不預先登記用戶端（<c>internal/HANDOFF-mcp.md</c> 第 2 節）。
/// </summary>
internal sealed class OAuthClientDirectory(
    IDbContextFactory<IdentityDbContext> contexts,
    ClientMetadataFetcher fetcher,
    TimeProvider time)
{
    /// <summary>動態註冊的用戶端這麼久沒用過、也沒有有效的授權，註冊新的時順手刪掉。</summary>
    private static readonly TimeSpan UnusedRegistrationRetention = TimeSpan.FromDays(90);

    private static readonly OAuthFailure UnknownClient = new(OAuthError.InvalidClient, "不認得這個應用（client_id 不正確）。");

    public async Task<OAuthResult<ResolvedClient>> ResolveAsync(string? clientId, CancellationToken cancellationToken)
    {
        if (!OAuthText.IsUsable(clientId, OAuthRules.MaxRedirectUriLength))
        {
            return UnknownClient;
        }

        if (clientId.StartsWith(OAuthRules.RegisteredClientPrefix, StringComparison.Ordinal))
        {
            return await ResolveRegisteredAsync(clientId, cancellationToken);
        }

        return clientId.StartsWith("https://", StringComparison.Ordinal)
            ? await ResolveMetadataAsync(clientId, cancellationToken)
            : UnknownClient;
    }

    public async Task<OAuthResult<OAuthRegisteredClient>> RegisterAsync(OAuthClientRegistration registration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);
        var checkedInput = CheckRegistration(registration);
        if (checkedInput.Failure is { } failure)
        {
            return failure;
        }

        var (name, redirectUris) = checkedInput.Value;
        var now = time.GetUtcNow();
        var row = new OAuthClientRow
        {
            Id = Guid.CreateVersion7(),
            Kind = OAuthClientRow.RegisteredKind,
            ClientId = OAuthRules.RegisteredClientPrefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)),
            Name = name,
            RedirectUris = [.. redirectUris],
            CreatedAt = now,
        };

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await DeleteUnusedAsync(db, now, cancellationToken);
        db.OAuthClients.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return new OAuthRegisteredClient(row.ClientId, row.Name, row.RedirectUris, now);
    }

    /// <summary>有人用這個用戶端發出授權碼了：記下時刻，「90 天沒用」才算得出來。</summary>
    public async Task TouchAsync(string clientId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var now = time.GetUtcNow();
        await db.OAuthClients
            .Where(c => c.ClientId == clientId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.LastUsedAt, (DateTimeOffset?)now), cancellationToken);
    }

    private static OAuthResult<(string Name, IReadOnlyList<string> RedirectUris)> CheckRegistration(OAuthClientRegistration registration)
    {
        // 要求 client_secret_post／basic 的也收，註冊成公開用戶端（回應裡寫 none、沒有 client_secret）：RFC 7591 允許伺服器把
        // 不支援的值換成自己的，MCP 官方的 C# SDK 註冊時一律要 client_secret_post。用金鑰或憑證認證的（private_key_jwt 這類）換不了。
        if (registration.TokenEndpointAuthMethod is not (null or "none" or "client_secret_post" or "client_secret_basic"))
        {
            return Fail(OAuthError.InvalidClientMetadata, "只支援公開用戶端（token_endpoint_auth_method 要是 none）。");
        }

        if (!IsSubset(registration.GrantTypes, "authorization_code", "refresh_token"))
        {
            return Fail(OAuthError.InvalidClientMetadata, "只支援 authorization_code 與 refresh_token 這兩種 grant_types。");
        }

        if (!IsSubset(registration.ResponseTypes, "code"))
        {
            return Fail(OAuthError.InvalidClientMetadata, "只支援 response_types: code。");
        }

        var name = OAuthText.CleanName(registration.ClientName, OAuthRules.MaxClientNameLength);
        if (name is null)
        {
            return Fail(OAuthError.InvalidClientMetadata, $"請填 client_name（不能是空的、不能超過 {OAuthRules.MaxClientNameLength} 個字）。");
        }

        var uris = (registration.RedirectUris ?? []).Distinct(StringComparer.Ordinal).ToList();
        if (uris.Count is < 1 or > OAuthRules.MaxRedirectUris || uris.Any(uri => !OAuthRedirects.TryParse(uri, out _)))
        {
            return Fail(
                OAuthError.InvalidRedirectUri,
                $"redirect_uris 要有 1 到 {OAuthRules.MaxRedirectUris} 個，而且都要是 https，或這台電腦上的 http（127.0.0.1、[::1]、localhost）。");
        }

        // 動態註冊的用戶端沒有人驗證過，外部的導回網址只給 Client ID Metadata Document（它的網域會顯示在同意頁）。
        if (uris.Any(uri => !OAuthRedirects.IsLoopbackRedirect(uri)))
        {
            return Fail(OAuthError.InvalidRedirectUri, OAuthRedirects.UnverifiedClientMessage);
        }

        return (name, uris);
    }

    private static OAuthFailure Fail(string error, string description) => new(error, description);

    private static bool IsSubset(IReadOnlyList<string>? given, params string[] allowed) =>
        given is null || given.All(value => allowed.Contains(value, StringComparer.Ordinal));

    private async Task<OAuthResult<ResolvedClient>> ResolveRegisteredAsync(string clientId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.OAuthClients.AsNoTracking()
            .SingleOrDefaultAsync(c => c.ClientId == clientId && c.Kind == OAuthClientRow.RegisteredKind, cancellationToken);
        return row is null ? UnknownClient : new ResolvedClient(row.ClientId, row.Name, null, row.RedirectUris);
    }

    private async Task<OAuthResult<ResolvedClient>> ResolveMetadataAsync(string clientId, CancellationToken cancellationToken)
    {
        if (!TryParseMetadataUrl(clientId, out var url, out var invalid))
        {
            return new OAuthFailure(OAuthError.InvalidClient, invalid);
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var now = time.GetUtcNow();
        var cached = await db.OAuthClients.AsNoTracking()
            .SingleOrDefaultAsync(c => c.ClientId == clientId && c.Kind == OAuthClientRow.MetadataKind, cancellationToken);
        if (cached is { CacheExpiresAt: { } expires, FetchedAt: { } fetched } && fetched <= now && now < expires)
        {
            return FromRow(cached, url);
        }

        var outcome = await fetcher.FetchAsync(url, clientId, cancellationToken);
        if (outcome.Document is { } document)
        {
            await StoreAsync(db, clientId, document, outcome.MaxAge, now, cancellationToken);
            return new ResolvedClient(clientId, document.Name, url.IdnHost, document.RedirectUris);
        }

        if (outcome.Rejected is { } reason)
        {
            return new OAuthFailure(OAuthError.InvalidClient, reason);
        }

        // 抓不到時，在快取的硬性期限（從抓到的時刻算起 MetadataCacheMax）內先用舊的；超過就不信了。
        return cached is { FetchedAt: { } at } && at <= now && now < at + OAuthRules.MetadataCacheMax
            ? FromRow(cached, url)
            : new OAuthFailure(OAuthError.TemporarilyUnavailable, "暫時抓不到用戶端的資料，請稍後再試。");
    }

    /// <summary><c>client_id</c> 是 https 網址、有路徑、沒有帳密與片段、路徑裡沒有 <c>.</c> <c>..</c>，也不指向內網的 IP 字面值。</summary>
    private static bool TryParseMetadataUrl(string clientId, out Uri url, out string error)
    {
        url = null!;
        error = "client_id 要是 https 網址，有路徑，沒有帳號密碼與片段。";
        if (clientId.Contains('#', StringComparison.Ordinal) || !Uri.TryCreate(clientId, UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeHttps || parsed.UserInfo.Length > 0 || parsed.Host.Length == 0
            || parsed.AbsolutePath.Length <= 1 || HasDotSegment(clientId))
        {
            return false;
        }

        if (PublicAddressGuard.IsBlockedLiteral(parsed))
        {
            error = "client_id 指向內網或本機，不能使用。";
            return false;
        }

        url = parsed;
        return true;
    }

    private static bool HasDotSegment(string clientId)
    {
        var afterAuthority = clientId.IndexOf('/', "https://".Length);
        var path = clientId[afterAuthority..].Split('?', 2)[0];
        return path.Split('/').Any(segment => segment is "." or "..");
    }

    private static OAuthResult<ResolvedClient> FromRow(OAuthClientRow row, Uri url) =>
        new ResolvedClient(row.ClientId, row.Name, url.IdnHost, row.RedirectUris);

    private static async Task StoreAsync(
        IdentityDbContext db, string clientId, ClientMetadataDocument document, TimeSpan? maxAge, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var ttl = Clamp(maxAge ?? OAuthRules.MetadataCacheMin);
        var id = Guid.CreateVersion7();
        var name = document.Name;
        var uris = document.RedirectUris.ToArray();
        var expires = now + ttl;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity.oauth_clients (id, kind, client_id, name, redirect_uris, created_at, fetched_at, cache_expires_at)
            VALUES ({id}, 'metadata', {clientId}, {name}, {uris}, {now}, {now}, {expires})
            ON CONFLICT (client_id) DO UPDATE SET
                name = EXCLUDED.name, redirect_uris = EXCLUDED.redirect_uris,
                fetched_at = EXCLUDED.fetched_at, cache_expires_at = EXCLUDED.cache_expires_at
            """, cancellationToken);
        await DeleteStaleMetadataAsync(db, now, cancellationToken);
    }

    private static TimeSpan Clamp(TimeSpan value) =>
        value < OAuthRules.MetadataCacheMin ? OAuthRules.MetadataCacheMin
        : value > OAuthRules.MetadataCacheMax ? OAuthRules.MetadataCacheMax
        : value;

    private static async Task DeleteUnusedAsync(IdentityDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now - UnusedRegistrationRetention;
        await db.OAuthClients
            .Where(c => c.Kind == OAuthClientRow.RegisteredKind && (c.LastUsedAt ?? c.CreatedAt) < cutoff
                && !db.OAuthGrants.Any(g => g.ClientId == c.ClientId && g.RevokedAt == null))
            .ExecuteDeleteAsync(cancellationToken);
        await DeleteStaleMetadataAsync(db, now, cancellationToken);
    }

    /// <summary>
    /// Client ID Metadata Document 的快取列：過期超過 <see cref="OAuthRules.MetadataCacheMax"/>（連抓不到時的後備期限都過了）、
    /// 也沒有有效的授權在用，就刪。任何人都能拿網址讓伺服器去抓，不清的話資料表只會長大；
    /// 抓成功時與動態註冊時都清，共用這一段。
    /// </summary>
    private static Task DeleteStaleMetadataAsync(IdentityDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now - OAuthRules.MetadataCacheMax;
        return db.OAuthClients
            .Where(c => c.Kind == OAuthClientRow.MetadataKind && c.CacheExpiresAt < cutoff
                && !db.OAuthGrants.Any(g => g.ClientId == c.ClientId && g.RevokedAt == null))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
