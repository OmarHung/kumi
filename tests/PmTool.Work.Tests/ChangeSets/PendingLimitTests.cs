using PmTool.Abstractions;

namespace PmTool.Work.Tests.ChangeSets;

/// <summary>一個專案同時等待核准的變更集有上限：塞滿的人不能讓清單與核准的人被淹沒。</summary>
[TestFixture]
public sealed class PendingLimitTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ProposeAsync_TheTwentyFirstPendingChangeSet_IsAConflictAndUsesNoNumber()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await FillAsync(scenario, WorkRules.MaxPendingChangeSetsPerProject);

        // Act
        var result = await ChangeSetDrafts.TryProposeAsync(scenario, "WEB", b => b);
        var listed = await _host.Changes.ListAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((result.Failure?.Kind, result.Failure?.Code), Is.EqualTo((FailureKind.Conflict, WorkErrorCodes.ChangeSetTooManyPending)));
            Assert.That(result.Failure!.Message, Does.Contain("20"));
            Assert.That(listed.Value, Has.Count.EqualTo(WorkRules.MaxPendingChangeSetsPerProject));
            Assert.That(listed.Value.Max(c => c.Number), Is.EqualTo(WorkRules.MaxPendingChangeSetsPerProject), "失敗的送出不佔流水號");
        }
    }

    [Test]
    public async Task ProposeAsync_AfterOneIsRejected_AnotherCanBeProposed()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await FillAsync(scenario, WorkRules.MaxPendingChangeSetsPerProject);
        await _host.Changes.RejectAsync(scenario.Admin, "WEB", 1, CancellationToken.None);

        // Act
        var result = await ChangeSetDrafts.TryProposeAsync(scenario, "WEB", b => b);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
    }

    [Test]
    public async Task ProposeAsync_PendingOnesWhoseBaseVersionIsOld_DoNotCount()
    {
        // Arrange：20 份等待中，套用其中一份之後其他的都對照舊版本，馬上又能送出新的。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await FillAsync(scenario, WorkRules.MaxPendingChangeSetsPerProject - 1);
        var applied = await ChangeSetDrafts.ProposeAsync(scenario, "WEB", b => b with { Metadata = b.Metadata with { Description = "新的說明" } });
        await ChangeSetDrafts.ApplyAsync(scenario, "WEB", applied.ChangeSet.Number);

        // Act
        var results = new List<Result<ChangeSetDetail>>();
        for (var index = 0; index < 3; index++)
        {
            results.Add(await ChangeSetDrafts.TryProposeAsync(scenario, "WEB", b => b));
        }

        // Assert
        Assert.That(results.Select(r => r.Succeeded), Has.All.True);
    }

    [Test]
    public async Task ProposeAsync_TwoAtTheSameTimeWithNineteenPending_OnlyOneSucceeds()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await FillAsync(scenario, WorkRules.MaxPendingChangeSetsPerProject - 1);
        var document = await ChangeSetDrafts.DraftAsync(scenario, "WEB", b => b);

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => _host.Changes.ProposeAsync(
            scenario.Admin, "WEB", document, PmTool.Blueprint.DocumentFormat.Json, CancellationToken.None)));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(1));
            Assert.That(results.Single(r => !r.Succeeded).Failure!.Code, Is.EqualTo(WorkErrorCodes.ChangeSetTooManyPending));
        }
    }

    private static async Task FillAsync(Scenario scenario, int count)
    {
        for (var index = 0; index < count; index++)
        {
            await ChangeSetDrafts.ProposeAsync(scenario, "WEB", b => b);
        }
    }
}
