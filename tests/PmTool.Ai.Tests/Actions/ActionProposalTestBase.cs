using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.Actions;

/// <summary>動作建議測試的共同環境：助理的環境加上一個假的處理器（蓋過知識庫的骨架）。</summary>
public abstract class ActionProposalTestBase
{
    private AssistantHarness _h = null!;

    internal AssistantHarness H => _h;

    internal FakeActionHandler Handler { get; private set; } = null!;

    internal IActionProposalCreator Creator => _h.Host.Get<IActionProposalCreator>();

    internal IActionProposalService Proposals => _h.Host.Get<IActionProposalService>();

    protected Actor Me => _h.Actor;

    [SetUp]
    public async Task StartAsync()
    {
        Handler = new FakeActionHandler();
        _h = await AssistantHarness.StartAsync(configure: services => services.AddSingleton<PmTool.Ai.Actions.IActionProposalHandler>(Handler));
    }

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    protected static ActionProposalDraft Draft(string kind = FakeActionHandler.TestKind, string? projectKey = null) =>
        new(kind, projectKey, JsonSerializer.SerializeToElement(new { changes = Array.Empty<string>() }));

    protected async Task<ActionProposal> CreatedAsync(ActionProposalDraft? draft = null)
    {
        var created = await Creator.CreateAsync(Me, draft ?? Draft(), CancellationToken.None);
        Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
        return created.Value;
    }

    protected static IReadOnlyDictionary<string, string> NoInputs { get; } = new Dictionary<string, string>();
}
