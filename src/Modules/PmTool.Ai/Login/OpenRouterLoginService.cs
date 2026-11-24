using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Ai.Providers;
using PmTool.Ai.Settings;

namespace PmTool.Ai.Login;

/// <summary>
/// 用 OpenRouter 登入（internal/HANDOFF-ai-floating.md 第 4.3 節）：OAuth 加 PKCE，不必事先註冊應用。
/// state 用 Data Protection 加密帶在網址上（人、組織、屬於誰、PKCE 的 verifier、到期時間），不另外存表；
/// 到期時間放在內容裡、用 <see cref="TimeProvider"/> 比，不用 ITimeLimitedDataProtector（它讀系統時鐘，測不到過期）。
/// 金鑰只在換到之後直接交給連線的新增，不出現在記錄、例外、回應裡。
/// </summary>
internal sealed class OpenRouterLoginService(
    IDataProtectionProvider protection,
    IDbContextFactory<AiDbContext> contexts,
    AiConnectionStore store,
    IOpenRouterAuthClient authClient,
    TimeProvider clock) : IOpenRouterLoginService
{
    internal const string StatePurpose = "PmTool.Ai.OpenRouterLogin.v1";

    internal const string AuthorizeUrl = "https://openrouter.ai/auth";

    internal const string KeyLabel = "Kumi";

    private const string OrganizationName = "OpenRouter";

    private const string PersonalName = "我的 OpenRouter";

    private const int VerifierBytes = 32;

    private const int NameAttempts = 5;

    private const int MaxCodeLength = 2048;

    private const int MaxStateLength = 8192;

    private const string KeyAlreadyIssued = "OpenRouter 那邊已經建好一把金鑰，請到 OpenRouter 的設定頁把它封存。";

    private readonly IDataProtector _protector = protection.CreateProtector(StatePurpose);

    public async Task<Result<string>> StartAsync(Actor actor, AiConnectionScope scope, string callbackUrl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(callbackUrl);
        if (!Uri.TryCreate(callbackUrl, UriKind.Absolute, out var callback) || callback.Scheme is not ("http" or "https") || callback.Fragment.Length > 0)
        {
            throw new ArgumentException("回呼網址要是 http 或 https 的完整網址。", nameof(callbackUrl));
        }

        if (await CheckScopeAsync(actor, scope, cancellationToken) is { } denied)
        {
            return denied;
        }

        var owner = OwnerOf(actor, scope);
        if (await store.IsFullAsync(owner, cancellationToken))
        {
            return AiConnectionStore.TooMany(owner);
        }

        var verifier = Base64Url(RandomNumberGenerator.GetBytes(VerifierBytes));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = _protector.Protect(JsonSerializer.Serialize(new LoginState(
            actor.OrganizationId, actor.PersonId, scope == AiConnectionScope.Personal ? "personal" : "organization", verifier,
            clock.GetUtcNow().Add(AiRules.OpenRouterLoginLifetime).ToUnixTimeSeconds())));
        return $"{AuthorizeUrl}?callback_url={Uri.EscapeDataString(callbackUrl)}&code_challenge={Uri.EscapeDataString(challenge)}"
            + $"&code_challenge_method=S256&state={Uri.EscapeDataString(state)}&key_label={Uri.EscapeDataString(KeyLabel)}";
    }

    public async Task<Result<OpenRouterLoginResult>> CompleteAsync(Actor actor, string code, string state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (string.IsNullOrWhiteSpace(code) || code.Length > MaxCodeLength || string.IsNullOrWhiteSpace(state) || state.Length > MaxStateLength)
        {
            return LoginFailed("缺少授權碼或登入的狀態，請重新登入。");
        }

        var opened = Open(actor, state);
        if (opened.Failure is { } bad)
        {
            return bad;
        }

        var scope = opened.Value.Scope;
        if (await CheckScopeAsync(actor, scope, cancellationToken) is { } denied)
        {
            return denied;
        }

        // 先看連線數，到上限就不用換金鑰了（換了反而在 OpenRouter 那邊留下一把用不到的）。
        var owner = OwnerOf(actor, scope);
        if (await store.IsFullAsync(owner, cancellationToken))
        {
            return AiConnectionStore.TooMany(owner);
        }

        string key;
        try
        {
            key = await authClient.ExchangeCodeAsync(code.Trim(), opened.Value.Verifier, cancellationToken);
        }
        catch (AiProviderException exception)
        {
            return LoginFailed(exception.Message);
        }

        var created = await CreateConnectionAsync(actor, owner, key, cancellationToken);
        return created.Failure is { } failure
            ? failure with { Message = $"{failure.Message}{KeyAlreadyIssued}" }
            : new OpenRouterLoginResult(scope, created.Value);
    }

    private async Task<Result<AiConnection>> CreateConnectionAsync(
        Actor actor, ConnectionOwner owner, string key, CancellationToken cancellationToken)
    {
        var existing = (await store.NamesAsync(owner, cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var baseName = owner.IsPersonal ? PersonalName : OrganizationName;
        Result<AiConnection> result = default!;
        for (var attempt = 1; attempt <= NameAttempts; attempt++)
        {
            var name = NextFreeName(baseName, existing);
            result = await store.CreateAsync(
                actor, owner, new AiConnectionInput(name, AiProviderKind.Openrouter, null, key, [], []), cancellationToken);

            // 同時有另一個登入搶先用了這個名字：換下一個再試。
            if (result.Failure?.Details.Any(d => d.Path == "name") != true)
            {
                return result;
            }

            existing.Add(name);
        }

        return result;
    }

    private static string NextFreeName(string baseName, HashSet<string> existing)
    {
        if (!existing.Contains(baseName))
        {
            return baseName;
        }

        for (var n = 2; ; n++)
        {
            var candidate = $"{baseName} {n}";
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private Result<OpenedState> Open(Actor actor, string state)
    {
        LoginState? payload;
        try
        {
            payload = JsonSerializer.Deserialize<LoginState>(_protector.Unprotect(state));
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException)
        {
            return LoginFailed("登入的連結不對或已經失效，請重新登入。");
        }

        if (payload is null || payload.Scope is not ("organization" or "personal") || string.IsNullOrEmpty(payload.Verifier))
        {
            return LoginFailed("登入的連結不對或已經失效，請重新登入。");
        }

        if (clock.GetUtcNow().ToUnixTimeSeconds() > payload.Expires)
        {
            return LoginFailed("登入等太久了，請重新登入。");
        }

        // 不是同一個人、同一個組織開始的：連結被別人拿去用，或換了帳號。
        if (payload.PersonId != actor.PersonId || payload.OrganizationId != actor.OrganizationId)
        {
            return LoginFailed("這個登入不是用目前的帳號開始的，請重新登入。");
        }

        return new OpenedState(payload.Scope == "personal" ? AiConnectionScope.Personal : AiConnectionScope.Organization, payload.Verifier);
    }

    private async Task<Failure?> CheckScopeAsync(Actor actor, AiConnectionScope scope, CancellationToken cancellationToken)
    {
        if (scope == AiConnectionScope.Organization)
        {
            return actor.IsOrganizationAdmin
                ? null
                : new Failure(FailureKind.Forbidden, AiErrorCodes.Forbidden, "只有組織的擁有者或管理員能接組織的 AI 帳號。");
        }

        return await AiPersonalPolicy.IsAllowedAsync(contexts, actor.OrganizationId, cancellationToken)
            ? null
            : new Failure(FailureKind.Forbidden, AiErrorCodes.PersonalDisabled, "組織目前不允許使用自己的 AI 帳號。");
    }

    private static ConnectionOwner OwnerOf(Actor actor, AiConnectionScope scope) =>
        scope == AiConnectionScope.Personal ? ConnectionOwner.Personal(actor) : ConnectionOwner.Organization(actor.OrganizationId);

    private static Failure LoginFailed(string reason) =>
        new(FailureKind.Invalid, AiErrorCodes.LoginFailed, $"OpenRouter 的登入沒有完成：{reason}");

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record OpenedState(AiConnectionScope Scope, string Verifier);

    private sealed record LoginState(
        [property: System.Text.Json.Serialization.JsonPropertyName("o")] Guid OrganizationId,
        [property: System.Text.Json.Serialization.JsonPropertyName("p")] Guid PersonId,
        [property: System.Text.Json.Serialization.JsonPropertyName("s")] string Scope,
        [property: System.Text.Json.Serialization.JsonPropertyName("v")] string Verifier,
        [property: System.Text.Json.Serialization.JsonPropertyName("e")] long Expires);
}
