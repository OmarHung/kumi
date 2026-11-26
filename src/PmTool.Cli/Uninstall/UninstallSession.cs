using System.Text.Json.Nodes;
using PmTool.Cli.Commands;
using PmTool.Cli.Output;
using PmTool.Cli.Setup;

namespace PmTool.Cli.Uninstall;

internal enum UninstallStatus
{
    /// <summary>做了。</summary>
    Done,

    /// <summary>問了，使用者說不要（或 --yes 時預設是不要）。</summary>
    Declined,

    /// <summary>--skip 略過。</summary>
    Skipped,

    /// <summary>這台電腦上沒有這樣東西，不用做。</summary>
    NotFound,

    /// <summary>kumi 做不到，要使用者自己處理（訊息說怎麼做）。</summary>
    Manual,
    Failed,
}

/// <param name="Name">步驟的代號（--skip 用的那個）。</param>
/// <param name="Engine">MCP 那一步是哪個引擎（<see cref="Agent.EngineKinds"/>）；其他步驟是 null。</param>
internal sealed record UninstallStep(string Name, UninstallStatus Status, IReadOnlyList<string> Messages, string? Engine = null);

/// <summary>
/// 一次 kumi uninstall：怎麼問（終端機逐題問，或 --yes 照預設）、步驟的結果、最後的提醒。
/// 步驟一個一個做，每一步做之前才問，前一步的結果（例如權杖撤銷了沒）會影響後一步的預設。
/// </summary>
internal sealed class UninstallSession(CommandCall call, bool assumeDefaults, IReadOnlySet<string> skipped)
{
    public const string Agent = "agent";
    public const string Tokens = "tokens";
    public const string Mcp = "mcp";
    public const string Skills = "skills";
    public const string Files = "files";
    public const string Executable = "executable";

    public static readonly IReadOnlyList<string> StepNames = [Agent, Tokens, Mcp, Skills, Files, Executable];

    private readonly List<UninstallStep> _steps = [];
    private readonly List<string> _reminders = [];
    private readonly SetupPrompt _prompt = new(call.Context, call.CancellationToken);

    public CommandCall Call { get; } = call;

    public CliContext Context => Call.Context;

    public CancellationToken CancellationToken => Call.CancellationToken;

    /// <summary>給步驟寫進度的地方：--json 時丟掉，免得混進 JSON。</summary>
    public TextWriter Progress => Call.Json ? TextWriter.Null : Call.Out;

    /// <summary>這台電腦存的權杖還沒在伺服器上撤銷（沒撤銷、撤銷失敗或略過）：刪設定的預設改成不刪，免得權杖再也找不回來撤銷。</summary>
    public bool TokensOutstanding { get; set; }

    public IReadOnlyList<UninstallStep> Steps => _steps;

    public bool IsSkipped(string step) => skipped.Contains(step);

    public Task HeadingAsync(string title) => Progress.WriteLineAsync($"\n{title}");

    /// <summary>問一題；--yes 時不問、照預設。</summary>
    public Task<bool> ConfirmAsync(string question, bool defaultYes) =>
        assumeDefaults ? Task.FromResult(defaultYes) : _prompt.AskAsync($"  {question}", defaultYes);

    public void Remind(string reminder)
    {
        if (!_reminders.Contains(reminder))
        {
            _reminders.Add(reminder);
        }
    }

    public async Task RecordAsync(UninstallStep step)
    {
        _steps.Add(step);
        var (mark, writer) = step.Status switch
        {
            UninstallStatus.Done => ("✓", Call.Out),
            UninstallStatus.Failed => ("✗", Call.Error),
            _ => ("–", Call.Out),
        };
        if (Call.Json)
        {
            return;
        }

        foreach (var message in step.Messages)
        {
            await writer.WriteLineAsync($"  {mark} {message}");
        }
    }

    public async Task<int> FinishAsync()
    {
        var failed = _steps.Any(step => step.Status == UninstallStatus.Failed);
        if (Call.Json)
        {
            await JsonOutput.WriteAsync(Call.Out, new JsonObject
            {
                ["steps"] = new JsonArray([.. _steps.Select(ToJson)]),
                ["reminders"] = new JsonArray([.. _reminders.Select(reminder => (JsonNode?)JsonValue.Create(reminder))]),
            });
            return failed ? ExitCodes.Error : ExitCodes.Success;
        }

        await Call.Out.WriteLineAsync();
        await Call.Out.WriteLineAsync(failed ? "有幾步沒有做成，原因寫在上面。" : "完成。");
        if (_reminders.Count > 0)
        {
            await Call.Out.WriteLineAsync("還要你自己處理的：");
            foreach (var reminder in _reminders)
            {
                await Call.Out.WriteLineAsync($"  • {reminder}");
            }
        }

        return failed ? ExitCodes.Error : ExitCodes.Success;
    }

    private static JsonNode ToJson(UninstallStep step)
    {
        var node = new JsonObject
        {
            ["step"] = step.Name,
            ["status"] = StatusCode(step.Status),
            ["messages"] = new JsonArray([.. step.Messages.Select(message => (JsonNode?)JsonValue.Create(message))]),
        };
        if (step.Engine is not null)
        {
            node["engine"] = step.Engine;
        }

        return node;
    }

    /// <summary>JSON 的 status：對外合約，不能改名。</summary>
    internal static string StatusCode(UninstallStatus status) => status switch
    {
        UninstallStatus.Done => "done",
        UninstallStatus.Declined => "declined",
        UninstallStatus.Skipped => "skipped",
        UninstallStatus.NotFound => "not_found",
        UninstallStatus.Manual => "manual",
        _ => "failed",
    };
}
