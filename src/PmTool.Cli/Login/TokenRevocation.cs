using PmTool.Cli.Api;
using PmTool.Cli.Commands;
using PmTool.Cli.Settings;

namespace PmTool.Cli.Login;

/// <param name="Cli">命令列的權杖在伺服器上撤銷了。</param>
/// <param name="Agent">本機代理的權杖在伺服器上撤銷了。</param>
/// <param name="Failures">沒撤銷成功的說明（已經不重複、不含權杖）。</param>
/// <param name="AlreadyInvalid">伺服器回 401 的那幾把（早就過期或被撤銷）：只有要求 <c>acceptInvalid</c> 時才算成撤銷了。</param>
internal sealed record RevocationResult(bool Cli, bool Agent, IReadOnlyList<string> Failures)
{
    public IReadOnlyList<string> AlreadyInvalid { get; init; } = [];
}

/// <summary>
/// 登出時撤銷伺服器上的權杖：用權杖自己呼叫 <c>DELETE /v1/account/tokens/current</c>（權杖撤銷自己）。
/// 失敗（連不上、伺服器不認得、權杖早就失效）只回報，不擋後面刪本機的檔案：使用者要登出，不能因為伺服器的問題卡住。
/// </summary>
internal static class TokenRevocation
{
    public static readonly RevocationResult None = new(false, false, []);

    /// <param name="acceptInvalid">
    /// 伺服器回 401（權杖早就失效）時當作撤銷了：kumi uninstall 用，失效的權杖不該擋著刪設定。
    /// 登出照舊算失敗並提醒，因為 401 也可能是伺服器設定的問題。
    /// </param>
    public static async Task<RevocationResult> RevokeAsync(CommandCall call, StoredConfig stored, bool acceptInvalid = false)
    {
        if (stored.Server is null || (stored.Token is null && stored.AgentToken is null))
        {
            return None;
        }

        var failures = new List<string>();
        var invalid = new List<string>();
        var cli = await RevokeOneAsync(call, stored.Server, stored.Token, "命令列的權杖", failures, acceptInvalid ? invalid : null);
        var agent = await RevokeOneAsync(call, stored.Server, stored.AgentToken, "本機代理的權杖", failures, acceptInvalid ? invalid : null);
        return new RevocationResult(cli, agent, failures) { AlreadyInvalid = invalid };
    }

    private static async Task<bool> RevokeOneAsync(
        CommandCall call, string server, string? token, string name, List<string> failures, List<string>? invalid)
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
        catch (CliException exception) when (invalid is not null && exception.HttpStatus == 401)
        {
            invalid.Add(name);
            return true;
        }
        catch (CliException exception)
        {
            failures.Add($"{name}：{exception.Message}");
            return false;
        }
    }
}
