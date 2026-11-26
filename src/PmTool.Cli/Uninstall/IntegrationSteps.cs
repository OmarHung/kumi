using PmTool.Cli.Agent;
using PmTool.Cli.Setup;
using PmTool.Cli.Skills;

namespace PmTool.Cli.Uninstall;

/// <summary>Claude Code／Codex 裡的 kumi MCP 與 kumi 裝的 skill。</summary>
internal static class IntegrationSteps
{
    private static readonly string[] Engines = [EngineKinds.ClaudeCode, EngineKinds.Codex];

    /// <param name="server">這台電腦登入的伺服器；指到別的伺服器的 MCP 預設不移除（可能是另一個 Kumi，不是這一份 kumi 加的）。</param>
    public static async Task RemoveMcpAsync(UninstallSession session, string? server)
    {
        const string name = UninstallSession.Mcp;
        await session.HeadingAsync("Claude Code／Codex 裡的 kumi MCP");
        if (session.IsSkipped(name))
        {
            await session.RecordAsync(new(name, UninstallStatus.Skipped, ["略過（--skip mcp）。"]));
            return;
        }

        var found = false;
        foreach (var kind in Engines)
        {
            found |= await RemoveFromEngineAsync(session, kind, server);
        }

        if (!found)
        {
            await session.RecordAsync(new(name, UninstallStatus.NotFound, ["Claude Code 與 Codex 裡都沒有 kumi 這個 MCP 伺服器（或找不到它們）。"]));
        }
    }

    /// <summary>回傳這個引擎裡有沒有 kumi 的 MCP。</summary>
    private static async Task<bool> RemoveFromEngineAsync(UninstallSession session, string kind, string? server)
    {
        if (await new EngineDetector(session.Context).DetectAsync(kind, session.CancellationToken) is not { } engine)
        {
            return false;
        }

        var configurator = new McpConfigurator(session.Context, session.Progress);
        var existing = await configurator.InspectAsync(engine, session.CancellationToken);
        if (!existing.Exists)
        {
            return false;
        }

        var where = existing.Url ?? "網址不明";
        if (!existing.IsUserScope)
        {
            // kumi setup 只加在使用者層；專案或本機層的是使用者自己加的，用 --scope user 移除不掉，也不該替他決定。
            await session.RecordAsync(new(UninstallSession.Mcp, UninstallStatus.Manual,
                [$"{engine.DisplayName} 裡的 kumi MCP（{where}）不在使用者層，不是 kumi setup 加的，沒有動：要移除請在那個專案裡執行 claude mcp remove kumi。"], kind));
            return true;
        }

        var same = server is null || existing.Url is null || McpConfigurator.SameUrl(existing.Url, configurator.McpUrl(server));
        var question = same
            ? $"移除 {engine.DisplayName} 裡的 kumi MCP（{where}）？"
            : $"{engine.DisplayName} 裡的 kumi MCP 指到 {where}，不是這台電腦登入的 {server}。也要移除嗎？";
        UninstallStep step;
        if (!await session.ConfirmAsync(question, defaultYes: same))
        {
            step = new(UninstallSession.Mcp, UninstallStatus.Declined, [$"沒有動 {engine.DisplayName} 裡的 kumi MCP（{where}）。"], kind);
        }
        else if (await configurator.RemoveAsync(engine, session.CancellationToken) is { } failure)
        {
            step = new(UninstallSession.Mcp, UninstallStatus.Failed, [failure], kind);
        }
        else
        {
            step = new(UninstallSession.Mcp, UninstallStatus.Done, [$"已從 {engine.DisplayName} 移除 kumi MCP（{where}）。"], kind);
        }

        await session.RecordAsync(step);
        return true;
    }

    public static async Task RemoveSkillsAsync(UninstallSession session)
    {
        const string name = UninstallSession.Skills;
        await session.HeadingAsync("給 AI 代理用的 skill");
        if (session.IsSkipped(name))
        {
            await session.RecordAsync(new(name, UninstallStatus.Skipped, ["略過（--skip skills）。"]));
            return;
        }

        var locations = SkillLocations.AllGlobal(session.Context);
        var service = SkillService.Current();
        var installed = service.Status(locations)
            .Where(outcome => outcome.Action is SkillAction.UpToDate or SkillAction.Updated or SkillAction.SkippedModified or SkillAction.SkippedNewer)
            .Select(outcome => outcome.Path)
            .ToList();
        if (installed.Count == 0)
        {
            await session.RecordAsync(new(name, UninstallStatus.NotFound,
                ["使用者層沒有 kumi 裝的 skill。裝在專案資料夾的要在那裡執行 kumi skill uninstall --project 資料夾。"]));
            return;
        }

        await session.Progress.WriteLineAsync("  kumi 裝的 skill：");
        foreach (var path in installed)
        {
            await session.Progress.WriteLineAsync($"    {path}");
        }

        if (!await session.ConfirmAsync("移除這些 skill？（你自己放的、改過的不動）", defaultYes: true))
        {
            await session.RecordAsync(new(name, UninstallStatus.Declined, ["沒有動 skill。"]));
            return;
        }

        var outcomes = service.Uninstall(locations);
        var messages = outcomes.Select(Describe).OfType<string>().ToList();
        var status = outcomes.Any(outcome => outcome.Action == SkillAction.Failed) ? UninstallStatus.Failed : UninstallStatus.Done;
        await session.RecordAsync(new(name, status, messages));
    }

    private static string? Describe(SkillOutcome outcome) => outcome.Action switch
    {
        SkillAction.Removed => $"已移除 {outcome.Path}",
        SkillAction.KeptModified => $"{outcome.Path} 的檔案改過，沒有刪（要連你改的一起刪：kumi skill uninstall --force）",
        SkillAction.Failed => $"{outcome.Path} 刪不掉：{outcome.Detail}",
        _ => null,
    };
}
