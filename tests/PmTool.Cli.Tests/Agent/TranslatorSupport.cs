using PmTool.Cli.Agent;

namespace PmTool.Cli.Tests.Agent;

/// <summary>錄下來的引擎事件（Fixtures/*.jsonl）逐行餵給轉換器，收集轉出來的進度與結果。</summary>
internal sealed record TranslatorRun(IReadOnlyList<AgentEvent> Events, JobOutcome? Outcome, bool Aborted)
{
    public static TranslatorRun Play(IEngineTranslator translator, string fixture)
    {
        var events = new List<AgentEvent>();
        JobOutcome? outcome = null;
        foreach (var line in File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Agent", "Fixtures", fixture)))
        {
            var step = translator.Feed(line);
            events.AddRange(step.Events);
            outcome ??= step.Outcome;
            if (step.Abort)
            {
                return new TranslatorRun(events, outcome, true);
            }
        }

        return new TranslatorRun(events, outcome, false);
    }

    public string Text => string.Concat(Events.OfType<TextEvent>().Select(text => text.Delta));

    public IReadOnlyList<ToolEvent> Tools => [.. Events.OfType<ToolEvent>()];
}
