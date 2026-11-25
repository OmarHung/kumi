namespace PmTool.Identity;

/// <summary>身分模組在交易提交之後呼叫；可以註冊零到多個（通知模組一個）。實作要是 singleton、不能丟例外。</summary>
public interface IAccountEventSink
{
    Task PublishAsync(AccountEvent accountEvent, CancellationToken cancellationToken);
}

public enum AccountEventKind
{
    AccessTokenCreated,
    CliLoginTokensIssued,
    OAuthAppAuthorized,
    CliLoginCodeReused,

    /// <summary>本機代理第一次從一台新的電腦登記（同一個人、沒見過的機器編號）。帶 <see cref="AccountEvent.MachineName"/> 與 <see cref="AccountEvent.SourceAddress"/>。</summary>
    LocalAgentMachineAdded,
}

/// <param name="EventId">等於同一件事的稽核紀錄編號；通知用它去重。</param>
/// <param name="PersonId">帳號的本人（收通知的人）。</param>
public sealed record AccountEvent(Guid EventId, Guid OrganizationId, Guid PersonId, AccountEventKind Kind, DateTimeOffset OccurredAt)
{
    /// <summary>手動建立：1 把；瀏覽器登入：1～2 把。</summary>
    public IReadOnlyList<AccountEventToken> Tokens { get; init; } = [];

    /// <summary>OAuth 授權的範圍。</summary>
    public IReadOnlyList<AccessTokenScope> Scopes { get; init; } = [];

    /// <summary>伺服器看到的位址（瀏覽器登入是兌換的那台，也就是 kumi 的電腦）。</summary>
    public string? SourceAddress { get; init; }

    public string? MachineName { get; init; }

    public string? ClientName { get; init; }

    public string? ClientHost { get; init; }

    /// <summary>只有 <see cref="AccountEventKind.CliLoginCodeReused"/> 用。</summary>
    public int RevokedTokens { get; init; }
}

public sealed record AccountEventToken(string Name, IReadOnlyList<AccessTokenScope> Scopes, DateTimeOffset? ExpiresAt);
