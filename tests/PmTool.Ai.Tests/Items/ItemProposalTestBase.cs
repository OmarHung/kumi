using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.Items;

/// <summary>修改建議測試的共同環境：助理的環境加上一個有成員的專案與一個項目。</summary>
public abstract class ItemProposalTestBase
{
    private AssistantHarness _h = null!;

    internal AssistantHarness H => _h;

    internal IItemProposalCreator Creator => _h.Host.Get<IItemProposalCreator>();

    internal IItemProposalService Proposals => _h.Host.Get<IItemProposalService>();

    protected Actor Me => _h.Actor;

    [SetUp]
    public async Task StartAsync()
    {
        _h = await AssistantHarness.StartAsync();
        _h.Projects.OnGet = (_, _) => ItemSamples.DetailWithMember();
    }

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    protected static ItemFieldChange Change(string field, object? value) =>
        new(field, JsonSerializer.SerializeToElement(value));

    protected static readonly string OldRevision = DescriptionRevisions.Of("舊的說明");

    /// <param name="revision">改說明時帶的版本；預設是項目現在的說明版本，null 是沒帶。</param>
    protected Task<Result<ItemProposal>> CreateAsync(
        IReadOnlyList<ItemFieldChange> changes, string? comment = null, string itemKey = "WEB-12", Actor? actor = null, string? revision = "current") =>
        Creator.CreateAsync(
            actor ?? Me,
            new ItemProposalDraft("WEB", itemKey, changes, comment) { DescriptionRevision = revision == "current" ? OldRevision : revision },
            CancellationToken.None);

    protected async Task<ItemProposal> CreatedAsync(IReadOnlyList<ItemFieldChange> changes, string? comment = null)
    {
        var created = await CreateAsync(changes, comment);
        Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
        return created.Value;
    }
}
