using PmTool.Cli.Api;
using PmTool.Cli.Commands;
using PmTool.Cli.Login;
using PmTool.Cli.Settings;

namespace PmTool.Cli.Setup;

/// <param name="CliReady">命令列的權杖可以用（本來就有、或這次登入拿到）。</param>
/// <param name="AgentReady">本機代理的權杖可以用。</param>
/// <param name="AgentSkipped">要了本機代理的權杖，但組織沒有開放成員使用自己的 AI 帳號。</param>
/// <param name="Failure">登入沒有成功的原因（給總結用）；沒有失敗是 null。</param>
internal sealed record SetupLoginState(bool CliReady, bool AgentReady, bool AgentSkipped, string? Account, string? Failure);

/// <summary>
/// 精靈的登入那一步：設定檔裡已經有而且還能用的權杖不再登入；缺的才一次開瀏覽器，同一頁核准命令列與本機代理兩把。
/// 登入失敗不中止精靈：Claude Code／Codex 的 MCP 授權是它們自己的流程，不依賴這裡的權杖。
/// </summary>
internal sealed class SetupLogin(CommandCall call, string server, TextWriter output)
{
    public async Task<SetupLoginState> EnsureAsync(bool needAgent)
    {
        var context = call.Context;
        var store = new ConfigStore(context.ConfigPath);
        var original = store.Load();
        var sameServer = original.Server is not null && Credentials.SameServer(original.Server, server);
        var stored = await StoredTokens.AdoptOldAgentTokenAsync(call, original, server);
        var cliAccount = sameServer ? await ValidAccountAsync(stored.Token, wantsAgentScope: false) : null;
        var agentAccount = needAgent && sameServer ? await ValidAccountAsync(stored.AgentToken, wantsAgentScope: true) : null;

        List<LoginToken> missing = [];
        if (cliAccount is null)
        {
            missing.Add(LoginToken.Cli);
        }

        if (needAgent && agentAccount is null)
        {
            missing.Add(LoginToken.Agent);
        }

        if (missing.Count == 0)
        {
            await output.WriteLineAsync($"  ✓ 已經登入：{cliAccount}");
            return new SetupLoginState(true, needAgent, false, cliAccount, null);
        }

        try
        {
            var request = new BrowserLoginRequest(
                server, missing, missing.Contains(LoginToken.Cli) ? LoginCommand.DefaultScopes : [],
                StoredTokens.ReplaceCandidates(stored, server, missing), call.Arguments.Has("no-browser"));
            var result = await new BrowserLogin(context).RunAsync(request, output, call.CancellationToken);
            store.Save(StoredTokens.Merge(stored, server, result));
            await output.WriteLineAsync($"  ✓ 已登入：{result.Describe}");
            if (result.AgentSkipped)
            {
                await output.WriteLineAsync("  – 組織沒有開放成員使用自己的 AI 帳號，這次沒有建立本機代理的權杖。");
            }

            return new SetupLoginState(
                cliAccount is not null || result.CliSecret is not null,
                agentAccount is not null || result.AgentSecret is not null,
                result.AgentSkipped, result.Describe, null);
        }
        catch (CliException exception)
        {
            await output.WriteLineAsync($"  ✗ 登入沒有完成：{exception.Message}");
            return new SetupLoginState(cliAccount is not null, agentAccount is not null, false, cliAccount, exception.Message);
        }
    }

    /// <summary>用這把權杖問伺服器登入的是誰；不通、已撤銷、範圍不對（命令列的不該有 agent:run、本機代理的要恰好是固定的那一組）都當作沒有。</summary>
    private async Task<string?> ValidAccountAsync(string? token, bool wantsAgentScope)
    {
        if (token is null)
        {
            return null;
        }

        try
        {
            var session = await new ApiClient(call.Context.Http, server, token).GetAsync("session", null, call.CancellationToken);
            if (session.Get("user") is null)
            {
                return null;
            }

            var scopes = session.Get("scopes");
            var hasAgentScope = scopes is { } granted && LoginCommand.HasScope(granted, LoginCommand.AccessTokenAgentScope);
            var accepted = wantsAgentScope ? LoginCommand.IsExactAgentScopes(scopes) : !hasAgentScope;
            return accepted ? LoginCommand.Describe(session) : null;
        }
        catch (CliException)
        {
            return null;
        }
    }
}
