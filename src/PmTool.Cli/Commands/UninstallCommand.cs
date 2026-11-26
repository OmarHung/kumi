using PmTool.Cli.Settings;
using PmTool.Cli.Uninstall;

namespace PmTool.Cli.Commands;

/// <summary>
/// kumi uninstall：把 kumi 從這台電腦拿掉。一步一步做，每一步做之前先問（--yes 照每一步的預設、不問）：
/// 背景的本機代理 → 伺服器上的權杖 → Claude Code／Codex 裡的 MCP → skill → 設定與記錄 → 執行檔。
/// 前一步失敗不擋後一步；做不到的（MCP 的 OAuth 授權、shell 的 PATH）最後列成提醒。
/// </summary>
internal static class UninstallCommand
{
    public static readonly IReadOnlyList<CommandSpec> Specs =
    [
        new("uninstall", string.Empty, "從這台電腦移除 kumi：每一步先問要不要做",
            "kumi uninstall [--yes] [--skip 步驟]… [--json]",
            [
                new OptionSpec("yes", "", "不問，每一步照預設做（沒有終端機或加了 --json 時一定要加）", 'y'),
                new OptionSpec("skip", "步驟",
                    "不做這一步：agent（背景的本機代理）、tokens（撤銷權杖）、mcp、skills、files（設定與記錄）、executable（執行檔）；可以重複",
                    Repeatable: true),
            ],
            0, 0, RunAsync),
    ];

    private static async Task<int> RunAsync(CommandCall call)
    {
        var skipped = ParseSkip(call.Arguments.Values("skip"));
        var assumeDefaults = call.Arguments.Has("yes");
        if (!assumeDefaults && (call.Json || !call.Context.IsInteractive))
        {
            throw CliException.Usage("kumi uninstall 要一步一步問你：沒有終端機或加了 --json 時請加 --yes（照每一步的預設做），不想做的步驟用 --skip 略過。");
        }

        var session = new UninstallSession(call, assumeDefaults, skipped);
        // 一開始就讀好：撤銷權杖那一步會改掉設定檔，MCP 那一步還要知道原本登入的是哪一台伺服器。
        var stored = LoadConfig(call.Context);
        var server = stored.Server ?? call.Context.GetEnvironmentVariable(Credentials.ServerVariable);
        await session.Progress.WriteLineAsync("kumi uninstall：一步一步把 kumi 從這台電腦拿掉，每一步做之前都會先問你（直接按 Enter 是預設）。");

        await AccountSteps.StopAgentAsync(session);
        await AccountSteps.RevokeTokensAsync(session, stored);
        await IntegrationSteps.RemoveMcpAsync(session, server);
        await IntegrationSteps.RemoveSkillsAsync(session);
        await LocalFileSteps.DeleteFilesAsync(session);
        await ExecutableStep.RunAsync(session);

        session.Remind("Claude Code／Codex 用 MCP 連 Kumi 時的授權（OAuth）kumi 撤銷不了：請到網頁「設定 → 命令列與整合」撤銷。");
        if (Credentials.TokenFromEnvironment(call.Context) is { } fromEnvironment)
        {
            session.Remind($"環境變數 {fromEnvironment.Variable} 還設定著：請從 shell 的設定裡拿掉。");
        }

        return await session.FinishAsync();
    }

    private static IReadOnlySet<string> ParseSkip(IReadOnlyList<string> values)
    {
        var names = values
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(value => value.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        var unknown = names.Where(value => !UninstallSession.StepNames.Contains(value)).ToList();
        if (unknown.Count > 0)
        {
            throw CliException.Usage(
                $"--skip 不認得 {string.Join("、", unknown)}：只能是 {string.Join("、", UninstallSession.StepNames)}。");
        }

        return names;
    }

    /// <summary>設定檔讀不懂也要能移除：當作什麼都沒存，刪檔那一步照樣刪得掉它。</summary>
    private static StoredConfig LoadConfig(CliContext context)
    {
        try
        {
            return new ConfigStore(context.ConfigPath).Load();
        }
        catch (CliException exception)
        {
            context.Error.WriteLine($"注意：{exception.Message}");
            return new StoredConfig();
        }
        catch (UnauthorizedAccessException exception)
        {
            context.Error.WriteLine($"注意：讀不到設定檔 {context.ConfigPath}：{exception.Message}");
            return new StoredConfig();
        }
    }
}
