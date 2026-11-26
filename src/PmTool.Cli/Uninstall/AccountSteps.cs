using PmTool.Cli.Agent.Autostart;
using PmTool.Cli.Login;
using PmTool.Cli.Settings;

namespace PmTool.Cli.Uninstall;

/// <summary>背景的本機代理與伺服器上的權杖。先停代理再撤銷：它還在跑的話，下一次輪詢會拿著剛撤銷的權杖得到 401。</summary>
internal static class AccountSteps
{
    public static async Task StopAgentAsync(UninstallSession session)
    {
        const string name = UninstallSession.Agent;
        await session.HeadingAsync("背景的本機代理");
        if (session.IsSkipped(name))
        {
            await session.RecordAsync(new(name, UninstallStatus.Skipped, ["略過（--skip agent）。"]));
            return;
        }

        if (AutostartBackends.For(session.Context.Autostart)?.IsInstalled() != true)
        {
            await session.RecordAsync(new(name, UninstallStatus.NotFound, ["沒有設定開機自動啟動。"]));
            return;
        }

        if (!await session.ConfirmAsync("停掉背景的本機代理，並取消登入電腦時自動啟動？", defaultYes: true))
        {
            await session.RecordAsync(new(name, UninstallStatus.Declined, ["沒有動：背景的本機代理照樣會在登入電腦時啟動。"]));
            return;
        }

        // 用現有的 kumi agent disable：停程序之前的種種確認（是不是 kumi、是不是同一個程序）都在那裡。它的說明不印，結果由這裡說。
        var quiet = session.Context.WithWriters(TextWriter.Null, session.Context.Error);
        var exit = await session.Context.Login.RunCommand(["agent", "disable"], quiet, session.CancellationToken);
        await session.RecordAsync(exit == ExitCodes.Success
            ? new(name, UninstallStatus.Done, ["已停掉背景的本機代理，並取消開機自動啟動。"])
            : new(name, UninstallStatus.Failed, ["背景的本機代理沒有停下來：請執行 kumi agent disable 看原因。"]));
    }

    public static async Task RevokeTokensAsync(UninstallSession session, StoredConfig stored)
    {
        const string name = UninstallSession.Tokens;
        await session.HeadingAsync("伺服器上的權杖");
        var hasTokens = stored.Token is not null || stored.AgentToken is not null;
        session.TokensOutstanding = hasTokens;
        if (session.IsSkipped(name))
        {
            await session.RecordAsync(new(name, UninstallStatus.Skipped, ["略過（--skip tokens）。"]));
            return;
        }

        if (!hasTokens)
        {
            await session.RecordAsync(new(name, UninstallStatus.NotFound, ["這台電腦上沒有存權杖。"]));
            return;
        }

        if (stored.Server is null)
        {
            await session.RecordAsync(new(name, UninstallStatus.Failed,
                ["設定檔裡有權杖但沒有伺服器，不知道要去哪裡撤銷：請到網頁「設定 → 命令列與整合」的「存取權杖」撤銷。"]));
            return;
        }

        if (!await session.ConfirmAsync($"撤銷伺服器 {stored.Server} 上的權杖（{Names(stored)}）？撤銷之後它們就不能再用。", defaultYes: true))
        {
            await session.RecordAsync(new(name, UninstallStatus.Declined, ["沒有撤銷：這些權杖在伺服器上還有效。"]));
            return;
        }

        var revocation = await TokenRevocation.RevokeAsync(session.Call, stored, acceptInvalid: true);
        // 撤銷了（或早就失效）的才從本機刪：沒撤銷成功的留著，之後還能再撤銷一次，刪掉了就只能去網頁找。
        var remaining = stored with
        {
            Token = revocation.Cli ? null : stored.Token,
            AgentToken = revocation.Agent ? null : stored.AgentToken,
        };
        var saveFailure = SaveRemaining(session, remaining);
        session.TokensOutstanding = remaining.Token is not null || remaining.AgentToken is not null;
        List<string> messages = [.. revocation.AlreadyInvalid.Select(token => $"{token}已經失效（伺服器不認得它），不用撤銷。")];
        if (revocation.Failures.Count > 0 || saveFailure is not null)
        {
            messages.AddRange(revocation.Failures.Select(failure => $"沒有撤銷成功：{failure}"));
            messages.Add(saveFailure ?? "請到網頁「設定 → 命令列與整合」的「存取權杖」撤銷。");
            await session.RecordAsync(new(name, UninstallStatus.Failed, messages));
            return;
        }

        messages.Add($"伺服器上的權杖（{Names(stored)}）已經不能用了，這台電腦存的也刪掉了。");
        await session.RecordAsync(new(name, UninstallStatus.Done, messages));
    }

    /// <summary>把還留著的權杖寫回設定檔；寫不進去回原因（伺服器上已經撤銷了，不能因為這裡中斷後面的步驟）。</summary>
    private static string? SaveRemaining(UninstallSession session, StoredConfig remaining)
    {
        try
        {
            new ConfigStore(session.Context.ConfigPath).Save(remaining);
            return null;
        }
        catch (CliException exception)
        {
            return $"伺服器上撤銷了，但{exception.Message}（裡面的權杖已經不能用了）。";
        }
    }

    private static string Names(StoredConfig stored) => (stored.Token, stored.AgentToken) switch
    {
        ({ }, { }) => "命令列的與本機代理的",
        (null, { }) => "本機代理的",
        _ => "命令列的",
    };
}
