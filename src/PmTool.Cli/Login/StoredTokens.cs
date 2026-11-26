using PmTool.Cli.Api;
using PmTool.Cli.Commands;
using PmTool.Cli.Settings;

namespace PmTool.Cli.Login;

/// <summary>設定檔裡的權杖怎麼換：瀏覽器登入（kumi auth login、kumi agent login、kumi setup）共用。</summary>
internal static class StoredTokens
{
    /// <summary>
    /// 這次登入要換掉的舊權杖：只有設定檔裡同一個伺服器的才算（別的伺服器的權杖不屬於這個帳號，送過去只是洩漏），
    /// 要命令列的就放 <see cref="StoredConfig.Token"/>，要本機代理的就放 <see cref="StoredConfig.AgentToken"/>。
    /// </summary>
    public static IReadOnlyList<string> ReplaceCandidates(StoredConfig stored, string server, IReadOnlyList<LoginToken> tokens)
    {
        if (stored.Server is null || !Credentials.SameServer(stored.Server, server))
        {
            return [];
        }

        var candidates = new List<string>();
        if (tokens.Contains(LoginToken.Cli) && stored.Token is { } cli)
        {
            candidates.Add(cli);
        }

        if (tokens.Contains(LoginToken.Agent) && stored.AgentToken is { } agent)
        {
            candidates.Add(agent);
        }

        return [.. candidates.Where(token => token.StartsWith(Credentials.TokenPrefix, StringComparison.Ordinal))];
    }

    /// <summary>
    /// 新的權杖蓋掉同一個伺服器的舊的；這次沒換到的那一把留著。換了伺服器就都不留：舊的權杖是別的伺服器的。
    /// </summary>
    public static StoredConfig Merge(StoredConfig stored, string server, BrowserLoginResult result)
    {
        var sameServer = stored.Server is not null && Credentials.SameServer(stored.Server, server);
        return new StoredConfig
        {
            Server = server,
            Token = result.CliSecret ?? (sameServer ? stored.Token : null),
            AgentToken = result.AgentSecret ?? (sameServer ? stored.AgentToken : null),
        };
    }

    /// <summary>
    /// 0.6.0 以前本機代理的權杖是用 kumi auth login 存的：它在 <see cref="StoredConfig.Token"/>、有 agent:run，可能正被 kumi agent start 用著。
    /// 還沒有另外存本機代理的權杖、而且同一個伺服器時，把它當成本機代理的（只在記憶體裡搬；登入成功才和新的權杖一起寫回），
    /// 不然它會被當成「要換掉的命令列權杖」送去撤銷。
    /// </summary>
    public static async Task<StoredConfig> AdoptOldAgentTokenAsync(CommandCall call, StoredConfig stored, string server)
    {
        var sameServer = stored.Server is not null && Credentials.SameServer(stored.Server, server);
        if (!sameServer || stored.AgentToken is not null || stored.Token is not { } token)
        {
            return stored;
        }

        try
        {
            var session = await new ApiClient(call.Context.Http, server, token).GetAsync("session", null, call.CancellationToken);
            // 範圍要恰好是本機代理固定的那一組才搬：混了 items:write 這類的不能存成本機代理的權杖（引擎拿得到它）。
            var isAgent = session.Get("user") is not null && LoginCommand.IsExactAgentScopes(session.Get("scopes"));
            return isAgent ? stored with { AgentToken = token, Token = null } : stored;
        }
        catch (CliException)
        {
            // 問不到（斷線、已撤銷）就維持原樣：那把權杖是不是本機代理的無從判斷，照舊交給伺服器處理。
            return stored;
        }
    }
}
