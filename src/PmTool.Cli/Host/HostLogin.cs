using System.Text.Json.Nodes;
using PmTool.Cli.Agent;
using PmTool.Cli.Api;

namespace PmTool.Cli.Host;

/// <summary>
/// <c>kumi host login</c> 的流程：登入碼認領 → 跑 CLI 自己的登入（繼承終端機）→ 探測登入狀態與計費 → 回報 → 存下 Kumi 的登入編號。
/// 規則由伺服器決定（擁有者、期限、是不是 API 計費）；被拒絕時這裡負責登出、說明原因。
/// Kumi 不碰密碼、Cookie、OAuth 權杖、授權碼、API 金鑰：它們只在使用者與 CLI 之間。
/// </summary>
internal sealed class HostLogin(
    HostStateStore store, ApiClient api, HostEngineProbe probe, Func<string, string?> environment, TextWriter error)
{
    public async Task RunAsync(string kind, string code, CancellationToken cancellation)
    {
        var name = EngineKinds.DisplayName(kind);
        var claim = await api.PostAsync("host-agent/logins/claim", new JsonObject { ["code"] = code }, cancellation);
        if (claim.Text("loginId") is not { } loginText || !Guid.TryParse(loginText, out var loginId))
        {
            throw new CliException(ExitCodes.Error, "伺服器的回應不正確（少了登入編號）。請確認伺服器的版本。");
        }

        if (claim.Text("engine") != kind)
        {
            await ReportAsync(loginId, new HostLoginOutcome(false, Billing.Unknown, "這個登入碼不是給這個引擎的。"), cancellation);
            throw new CliException(ExitCodes.Error, $"這個登入碼是給 {EngineKinds.DisplayName(claim.Text("engine") ?? "?")} 的，不是 {name}。請在網頁重新開始登入。");
        }

        if (ExecutableLocator.Find(kind, environment) is not { } executable)
        {
            await ReportAsync(loginId, new HostLoginOutcome(false, Billing.Unknown, $"主機上沒有 {name}。"), cancellation);
            throw new CliException(ExitCodes.Error, $"主機上找不到 {name}。請使用 Kumi 提供的 ai-worker 映像檔。");
        }

        var cliExit = await RunCliLoginAsync(kind, executable, cancellation);
        var probed = await probe.ProbeAsync(kind, cancellation);
        var signedIn = cliExit == 0 && probed.LoggedIn;
        var outcome = new HostLoginOutcome(
            signedIn, probed.Billing, signedIn ? null : $"{name} 的登入沒有完成（結束代碼 {cliExit?.ToString() ?? "無法啟動"}）。");
        try
        {
            await ReportAsync(loginId, outcome, cancellation);
        }
        catch (CliException exception) when (exception.Code == "ai.host_login_rejected" || exception.HttpStatus == 422)
        {
            await RejectAsync(kind, cliExit == 0, exception, cancellation);
            return;
        }

        store.Update(state => state.WithLogin(kind, new HostLoginRecord(loginId, probed.Fingerprint)));
    }

    private async Task<int?> RunCliLoginAsync(string kind, string executable, CancellationToken cancellation)
    {
        if (kind == EngineKinds.Codex)
        {
            await error.WriteLineAsync("貼上 OpenAI API 金鑰後按 Ctrl-D（金鑰只交給 codex，不經過 Kumi）：");
            return await InteractiveProcess.RunAsync(executable, ["login", "--with-api-key"], cancellation);
        }

        await error.WriteLineAsync("接下來是 Claude Code 自己的登入，請選 Anthropic Console（API 用量計費），依畫面完成（網址與授權碼只在你與 Claude Code 之間）：");
        return await InteractiveProcess.RunAsync(executable, ["auth", "login", "--console"], cancellation);
    }

    /// <summary>伺服器拒絕：丟掉這份登入（CLI 的登出），清掉存的授權，把原因告訴使用者；結束代碼 1。</summary>
    private async Task RejectAsync(string kind, bool cliSignedIn, CliException rejection, CancellationToken cancellation)
    {
        if (cliSignedIn)
        {
            await probe.LogoutAsync(kind, cancellation);
        }

        store.Update(state => state.WithLogin(kind, null));
        throw new CliException(ExitCodes.Error, $"{rejection.Message}{(cliSignedIn ? $"（已經登出 {EngineKinds.DisplayName(kind)}。）" : string.Empty)}");
    }

    private async Task ReportAsync(Guid loginId, HostLoginOutcome outcome, CancellationToken cancellation) =>
        await api.PostAsync($"host-agent/logins/{loginId}/complete", new JsonObject
        {
            ["loggedIn"] = outcome.LoggedIn,
            ["billing"] = outcome.Billing,
            ["problem"] = outcome.Problem,
        }, cancellation);
}

internal sealed record HostLoginOutcome(bool LoggedIn, string Billing, string? Problem);
