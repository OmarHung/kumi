using PmTool.Abstractions;
using static PmTool.Work.Tests.SavedViews.SavedViewCrudTests;

namespace PmTool.Work.Tests.SavedViews;

/// <summary>檢視的週期與里程碑條件：存得下 current、none 與編號，格式不對的擋下。</summary>
[TestFixture]
public sealed class SavedViewPlanningConditionTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [TestCase("current", "none")]
    [TestCase("none", null)]
    [TestCase(null, "none")]
    [TestCase("018f0000-0000-7000-8000-000000000001", "018f0000-0000-7000-8000-000000000002")]
    public async Task CreateAsync_PlanningConditions_AreStoredAndListedBack(string? cycle, string? milestone)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var query = Empty with { Cycle = cycle, Milestone = milestone };

        // Act
        var created = await Create(scenario.Admin, "規劃", query: query);
        var listed = await _host.SavedViews.ListAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        Assert.That(created.Succeeded, Is.True, () => created.Failure!.Message);
        Assert.That(listed.Value.Single().Query, Is.EqualTo(query));
    }

    [Test]
    public async Task ReplaceAsync_CanChangeAndClearThePlanningConditions()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var view = (await Create(scenario.Admin, "規劃", Empty with { Cycle = "current" })).Value;

        // Act
        var replaced = await _host.SavedViews.ReplaceAsync(
            scenario.Admin, "WEB", view.Id, new SavedViewDraft("規劃", SavedViewLayout.Table, Empty with { Milestone = "none" }), CancellationToken.None);

        // Assert
        Assert.That(replaced.Value.Query, Is.EqualTo(Empty with { Milestone = "none" }));
    }

    [TestCase("now", null, "query.cycle")]
    [TestCase("CURRENT", null, "query.cycle")]
    [TestCase("current ", null, "query.cycle")]
    [TestCase("018f0000-0000-7000-8000-000000000001x", null, "query.cycle")]
    [TestCase(null, "current", "query.milestone")]
    [TestCase(null, "x", "query.milestone")]
    [TestCase(null, "", "query.milestone")]
    public async Task CreateAsync_BadPlanningCondition_IsInvalidWithThePath(string? cycle, string? milestone, string path)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await Create(scenario.Admin, "壞的", Empty with { Cycle = cycle, Milestone = milestone });

        // Assert
        AssertFailure(result, FailureKind.Invalid, WorkErrorCodes.SavedViewInvalid);
        Assert.That(result.Failure!.Details.Select(d => d.Path), Is.EqualTo(new[] { path }));
    }

    private Task<Result<SavedView>> Create(Actor actor, string name, SavedViewQuery query) =>
        _host.SavedViews.CreateAsync(actor, "WEB", new SavedViewDraft(name, SavedViewLayout.Table, query), CancellationToken.None);
}
