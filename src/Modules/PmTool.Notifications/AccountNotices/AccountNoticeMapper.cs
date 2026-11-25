using PmTool.Identity;

namespace PmTool.Notifications.AccountNotices;

/// <summary>帳號事件轉成通知的內容：事件種類換成通知自己的列舉，範圍換成對外的名稱。</summary>
internal static class AccountNoticeMapper
{
    public static AccountNotice From(AccountEvent accountEvent) => new(
        EventOf(accountEvent.Kind),
        [.. accountEvent.Tokens.Select(token => new AccountNoticeToken(token.Name, ScopeNames(token.Scopes), token.ExpiresAt))],
        ScopeNames(accountEvent.Scopes),
        accountEvent.SourceAddress,
        accountEvent.MachineName,
        accountEvent.ClientName,
        accountEvent.ClientHost,
        accountEvent.RevokedTokens);

    private static IReadOnlyList<string> ScopeNames(IReadOnlyList<AccessTokenScope> scopes) =>
        [.. scopes.Select(AccessTokenScopes.NameOf)];

    private static AccountNoticeEvent EventOf(AccountEventKind kind) => kind switch
    {
        AccountEventKind.AccessTokenCreated => AccountNoticeEvent.AccessTokenCreated,
        AccountEventKind.CliLoginTokensIssued => AccountNoticeEvent.CliLoginTokensIssued,
        AccountEventKind.OAuthAppAuthorized => AccountNoticeEvent.OauthAppAuthorized,
        AccountEventKind.CliLoginCodeReused => AccountNoticeEvent.CliLoginCodeReused,
        AccountEventKind.LocalAgentMachineAdded => AccountNoticeEvent.LocalAgentMachineAdded,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "不認得的帳號事件種類。"),
    };
}
