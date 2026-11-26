using PmTool.Cli.Api;
using PmTool.Cli.Commands;
using PmTool.Cli.Settings;

namespace PmTool.Cli.Login;

/// <param name="Cli">命令列的權杖在伺服器上撤銷了。</param>
/// <param name="Agent">本機代理的權杖在伺服器上撤銷了。</param>
/// <param name="Failures">沒撤銷成功的說明（已經不重複、不含權杖）。</param>
internal sealed record RevocationResult(bool Cli, bool Agent, IReadOnlyList<string> Failures);

/// <summary>
/// 登出時撤銷伺服器上的權杖：用權杖自己呼叫 <c>DELETE /v1/account/tokens/current</c>（權杖撤銷自己）。
/// 失敗（連不上、伺服器不認得、權杖早就失效）只回報，不擋後面刪本機的檔案：使用者要登出，不能因為伺服器的問題卡住。
/// </summary>
internal static class TokenRevocation
{
    public static readonly RevocationResult None = new(false, false, []);

    public static async Task<RevocationResult> RevokeAsync(CommandCall call, StoredConfig stored)
    {
        if (stored.Server is null || (stored.Token is null && stored.AgentToken is null))
        {
            return None;
        }

        var failures = new List<string>();
        var cli = await RevokeOneAsync(call, stored.Server, stored.Token, "命令列的權杖", failures);
        var agent = await RevokeOneAsync(call, stored.Server, stored.AgentToken, "本機代理的權杖", failures);
        return new RevocationResult(cli, agent, failures);
    }

    private static async Task<bool> RevokeOneAsync(CommandCall call, string server, string? token, string name, List<string> failures)
    {
        if (token is null)
        {
            return false;
        }

        try
        {
            Credentials.EnsureTransportAllowed(call.Context, call.Arguments, server);
            await new ApiClient(call.Context.Http, server, token).DeleteAsync("account/tokens/current", call.CancellationToken);
            return true;
        }
        catch (CliException exception)
        {
            failures.Add($"{name}：{exception.Message}");
            return false;
        }
    }
}
