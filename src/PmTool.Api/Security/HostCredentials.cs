using System.Globalization;
using PmTool.Ai;
using PmTool.Api.OAuth;
using PmTool.Api.RateLimiting;
using PmTool.Api.Responses;

namespace PmTool.Api.Security;

/// <summary>標在 worker 的端點上：這個端點只認機器憑證（<c>kha_…</c>），不認任何人的登入。</summary>
/// <param name="RequiresCredential">false 是配對（用配對碼換憑證，還沒有憑證）。</param>
internal sealed record HostAgentEndpoint(bool RequiresCredential);

/// <summary>
/// 主機代理的兩種機器憑證，都不是任何人的身分：
/// <c>kha_…</c>（worker 的機器憑證，只能打 <c>/v1/host-agent/*</c>）與 <c>kjt_…</c>（每件工作的工具授權，只能打 <c>/mcp</c>）。
/// <see cref="AccessTokenHandler"/> 看到這兩種開頭一律當匿名（<c>NoResult</c>，不記失敗），所以帶著它們打其他端點就是 401；
/// 這裡自己驗證，結果放進 <c>HttpContext.Items</c>，不建 <c>ClaimsPrincipal</c>。
/// 用中介軟體而不是端點的過濾器：限流要依代理、依工作分開算，而限流排在端點的過濾器之前；
/// 排在確認登入之後、限流之前，和權杖的驗證同一個位置。
/// </summary>
internal static class HostCredentials
{
    public const string AgentPrefix = "kha_";

    public const string ToolPrefix = "kjt_";

    private const string AgentItem = "pmtool.host-agent";

    private const string GrantItem = "pmtool.host-tool-grant";

    public static bool IsMachineCredential(string secret) =>
        secret.StartsWith(AgentPrefix, StringComparison.Ordinal) || secret.StartsWith(ToolPrefix, StringComparison.Ordinal);

    /// <summary>驗證過的機器；沒有（沒帶、無效）是 null。</summary>
    public static HostAgentIdentity? TryGetAgent(HttpContext context) =>
        context.Items.TryGetValue(AgentItem, out var value) ? value as HostAgentIdentity : null;

    /// <summary>驗證過的工作授權；沒有是 null。</summary>
    public static HostToolGrant? TryGetGrant(HttpContext context) =>
        context.Items.TryGetValue(GrantItem, out var value) ? value as HostToolGrant : null;

    /// <summary>機器憑證（含配對）失敗額度的鍵：自己的桶，不和人的權杖（同一個 IP）共用。</summary>
    public static string AgentFailureSource(HttpContext context) =>
        "kha:" + ApiRateLimiting.AddressKeyOf(context.Connection.RemoteIpAddress);

    public static IApplicationBuilder UseHostCredentials(this IApplicationBuilder app) => app.Use(AuthenticateAsync);

    /// <summary>端點的過濾器：萬一中介軟體沒排進去，也不會讓沒驗證過的請求通過。</summary>
    public static async ValueTask<object?> RequireAgentAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next) =>
        TryGetAgent(invocation.HttpContext) is null ? ApiFailure.AuthRequired.ToResult() : await next(invocation);

    private static async Task AuthenticateAsync(HttpContext context, RequestDelegate next)
    {
        var endpoint = context.GetEndpoint();
        var secret = AccessTokenAuthentication.TryReadSecret(context.Request);
        if (endpoint?.Metadata.GetMetadata<HostAgentEndpoint>() is { RequiresCredential: true })
        {
            if (!await TryAuthenticateAgentAsync(context, secret))
            {
                return;
            }
        }
        else if (secret?.StartsWith(ToolPrefix, StringComparison.Ordinal) == true
            && context.Request.Path.StartsWithSegments(OAuthServerResolver.McpPath, StringComparison.OrdinalIgnoreCase)
            && !await TryResolveGrantAsync(context, secret))
        {
            return;
        }

        await next(context);
    }

    // 失敗時已經寫好回應（401 或 429）。
    private static async Task<bool> TryAuthenticateAgentAsync(HttpContext context, string? secret)
    {
        var failures = context.RequestServices.GetRequiredService<BearerFailureLimiter>();
        // 自己的失敗額度桶（和 kjt: 一樣）：同一個 IP 上 worker 的憑證打錯，不能把那個 IP 上的人的權杖一起擋掉。
        var source = AgentFailureSource(context);
        if (secret is not null && await RejectIfBlockedAsync(context, failures, source))
        {
            return false;
        }

        HostAgentIdentity? identity = null;
        if (secret?.StartsWith(AgentPrefix, StringComparison.Ordinal) == true)
        {
            var workers = context.RequestServices.GetRequiredService<IHostAgentWorkerService>();
            identity = await workers.AuthenticateAsync(secret, context.RequestAborted);
        }

        if (identity is null)
        {
            // 帶了東西卻不對才記：沒帶標頭的請求不是在猜憑證。
            if (secret is not null)
            {
                failures.RecordFailure(source);
            }

            await Challenge(context);
            return false;
        }

        context.Items[AgentItem] = identity;
        return true;
    }

    // 無效的工作授權不在這裡擋：/mcp 要先看 Origin，之後由守門的過濾器回 401。這裡只負責記失敗與擋住猜測。
    private static async Task<bool> TryResolveGrantAsync(HttpContext context, string secret)
    {
        // 自己的失敗額度桶：worker 與它跑的引擎子程序同一個 IP，引擎用剛失效的授權多打幾次，不能把 worker 的 kha_ 一起擋掉。
        var failures = context.RequestServices.GetRequiredService<BearerFailureLimiter>();
        var source = "kjt:" + ApiRateLimiting.AddressKeyOf(context.Connection.RemoteIpAddress);
        if (await RejectIfBlockedAsync(context, failures, source))
        {
            return false;
        }

        var grants = context.RequestServices.GetRequiredService<IHostToolGrants>();
        if (await grants.ResolveAsync(secret, context.RequestAborted) is { } grant)
        {
            context.Items[GrantItem] = grant;
        }
        else
        {
            failures.RecordFailure(source);
        }

        return true;
    }

    private static async Task<bool> RejectIfBlockedAsync(HttpContext context, BearerFailureLimiter failures, string source)
    {
        if (!failures.IsBlocked(source, out var retryAfter))
        {
            return false;
        }

        context.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        await ApiFailure.RateLimited.WriteAsync(context.Response, context.RequestAborted);
        return true;
    }

    private static Task Challenge(HttpContext context)
    {
        context.Response.Headers.WWWAuthenticate = "Bearer";
        return ApiFailure.AuthRequired.WriteAsync(context.Response, context.RequestAborted);
    }
}
