using PmTool.Identity;

namespace PmTool.Api.Security;

/// <summary>
/// 端點宣告：用存取權杖呼叫時，權杖要有這些範圍之一。沒有宣告的端點一律不給權杖用（帳號管理、人員管理、
/// 建立專案這些只能在網頁上做）：新加的端點忘了宣告時，預設是安全的那一邊。
/// 帶 <see cref="AccessTokenScope.AgentRun"/> 的權杖（本機代理的，會交給成員電腦上的引擎）只能用在宣告了它的端點
/// 或 <see cref="AnyToken"/> 的端點，宣告的是別的範圍時照樣拒絕（<see cref="TokenScopeCheck"/>）。
/// </summary>
internal sealed record TokenScopeRequirement(IReadOnlyList<AccessTokenScope> AnyOf)
{
    /// <summary>任何一把有效的權杖都可以（例如 GET /v1/session，讓 CLI 確認自己是誰）。</summary>
    public static readonly TokenScopeRequirement AnyToken = new([]);

    public bool AllowsAnyToken => AnyOf.Count == 0;
}

internal static class TokenScopeRequirementExtensions
{
    /// <summary>用權杖呼叫時，權杖要有其中一個範圍。用 Cookie 登入的請求不受影響。</summary>
    public static TBuilder RequireTokenScope<TBuilder>(this TBuilder builder, params AccessTokenScope[] anyOf)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentOutOfRangeException.ThrowIfZero(anyOf.Length);
        return builder.WithMetadata(new TokenScopeRequirement(anyOf));
    }

    /// <summary>任何一把有效的權杖都可以呼叫。</summary>
    public static TBuilder AllowAnyAccessToken<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder => builder.WithMetadata(TokenScopeRequirement.AnyToken);
}
