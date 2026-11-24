using PmTool.Cli.Agent;

namespace PmTool.Cli.Setup;

/// <summary>找這台電腦上的 Claude Code 與 Codex：只找執行檔、問版本，不碰它們的登入狀態（MCP 的授權是它們自己的流程）。</summary>
internal sealed class EngineDetector(CliContext context)
{
    private const string ClaudeInstallUrl = "https://code.claude.com/docs/en/overview";
    private const string CodexInstallUrl = "https://github.com/openai/codex";

    public static string InstallUrl(string kind) => kind == EngineKinds.Codex ? CodexInstallUrl : ClaudeInstallUrl;

    public static string DisplayName(string kind) => kind == EngineKinds.Codex ? "Codex" : "Claude Code";

    public async Task<DetectedEngine?> DetectAsync(string kind, CancellationToken cancellation)
    {
        if (ExecutableLocator.Find(kind, context.GetEnvironmentVariable) is not { } executable)
        {
            return null;
        }

        var probe = await new EngineProber(context.AgentOptions).ProbeAsync(executable, ["--version"], cancellation);
        if (probe is not { ExitCode: 0 })
        {
            return null;
        }

        var version = kind == EngineKinds.Codex ? EngineProber.LastToken(probe.StandardOutput) : EngineProber.FirstToken(probe.StandardOutput);
        return new DetectedEngine(kind, DisplayName(kind), ExecutableLocator.CommandFor(kind), executable, version);
    }
}
