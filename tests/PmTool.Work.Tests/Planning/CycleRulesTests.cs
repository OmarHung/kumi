using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using static PmTool.Work.Tests.Planning.PlanningSupport;

namespace PmTool.Work.Tests.Planning;

/// <summary>週期：新增、列出、修改、刪除，以及名稱、日期重疊與上限的規則。</summary>
[TestFixture]
public sealed class CycleRulesTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task CreateCycleAsync_StoresItAndListsItWithEmptyProgress()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var created = await _host.Planning.CreateCycleAsync(scenario.Admin, "WEB", CycleOf("  衝刺 1  ", 0, 13), CancellationToken.None);
        var listed = await _host.Planning.ListCyclesAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        Assert.That(created.Succeeded, Is.True, () => created.Failure!.Message);
        var empty = new ItemProgress(0, 0, 0, null, null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Value, Is.EqualTo(new Cycle(created.Value.Id, "衝刺 1", Day1, Day1.AddDays(13), empty)));
            Assert.That(listed.Value, Is.EqualTo(new[] { created.Value }));
        }
    }

    [Test]
    public async Task ListCyclesAsync_SortsByStartDate()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("後面", 28, 41));
        await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("前面", -14, -1));
        await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("中間", 0, 13));

        // Act
        var listed = await _host.Planning.ListCyclesAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        Assert.That(listed.Value.Select(c => c.Name), Is.EqualTo(new[] { "前面", "中間", "後面" }));
    }

    [Test]
    public async Task UpdateCycleAsync_ChangesNameAndDatesAndKeepsTheId()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var cycle = await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("衝刺 1", 0, 13));

        // Act
        var updated = await _host.Planning.UpdateCycleAsync(scenario.Admin, "WEB", cycle.Id, CycleOf("衝刺 一", 1, 20), CancellationToken.None);

        // Assert
        Assert.That(updated.Succeeded, Is.True, () => updated.Failure!.Message);
        Assert.That(updated.Value, Is.EqualTo(cycle with { Name = "衝刺 一", StartDate = Day1.AddDays(1), EndDate = Day1.AddDays(20) }));
    }

    [Test]
    public async Task UpdateCycleAsync_OwnDatesOrOwnNameWithOtherCase_IsNotAConflict()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var cycle = await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("Sprint", 0, 13));

        // Act
        var updated = await _host.Planning.UpdateCycleAsync(scenario.Admin, "WEB", cycle.Id, CycleOf("SPRINT", 0, 13), CancellationToken.None);

        // Assert
        Assert.That(updated.Succeeded, Is.True, () => updated.Failure!.Message);
    }

    [Test]
    public async Task UpdateCycleAsync_UnknownOrOtherProjectsCycle_IsNotFound()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateProjectAsync("OPS");
        var other = await CreateCycleAsync(_host, scenario.Admin, "OPS", CycleOf("衝刺 1", 0, 13));

        // Act
        var unknown = await _host.Planning.UpdateCycleAsync(scenario.Admin, "WEB", Guid.NewGuid(), CycleOf("x1", 0, 1), CancellationToken.None);
        var foreign = await _host.Planning.UpdateCycleAsync(scenario.Admin, "WEB", other.Id, CycleOf("x1", 0, 1), CancellationToken.None);
        var deleted = await _host.Planning.DeleteCycleAsync(scenario.Admin, "WEB", other.Id, CancellationToken.None);

        // Assert
        AssertFailure(unknown, FailureKind.NotFound, WorkErrorCodes.CycleNotFound);
        AssertFailure(foreign, FailureKind.NotFound, WorkErrorCodes.CycleNotFound);
        AssertFailure(deleted, FailureKind.NotFound, WorkErrorCodes.CycleNotFound);
        Assert.That((await _host.Planning.ListCyclesAsync(scenario.Admin, "OPS", CancellationToken.None)).Value, Is.EqualTo(new[] { other }));
    }

    [Test]
    public async Task DeleteCycleAsync_KeepsTheItemsButTheyHaveNoCycle()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var cycle = await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("衝刺 1", 0, 13));
        var item = await ItemInAsync(_host, scenario, "WEB", cycleId: cycle.Id);

        // Act
        var deleted = await _host.Planning.DeleteCycleAsync(scenario.Admin, "WEB", cycle.Id, CancellationToken.None);
        var read = await _host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted.Succeeded, Is.True);
            Assert.That(read.Value.CycleId, Is.Null);
            Assert.That((await _host.Planning.ListCyclesAsync(scenario.Admin, "WEB", CancellationToken.None)).Value, Is.Empty);
        }
    }

    [Test]
    public async Task CreateCycleAsync_SameNameIgnoringCase_IsNameTakenButAnotherProjectIsFine()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateProjectAsync("OPS");
        await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("Sprint", 0, 13));

        // Act
        var same = await _host.Planning.CreateCycleAsync(scenario.Admin, "WEB", CycleOf(" SPRINT ", 20, 30), CancellationToken.None);
        var elsewhere = await _host.Planning.CreateCycleAsync(scenario.Admin, "OPS", CycleOf("Sprint", 0, 13), CancellationToken.None);

        // Assert
        AssertFailure(same, FailureKind.Conflict, WorkErrorCodes.CycleNameTaken);
        Assert.That(elsewhere.Succeeded, Is.True);
    }

    [Test]
    public async Task UpdateCycleAsync_NameOfAnotherCycle_IsNameTaken()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("衝刺 1", 0, 13));
        var second = await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("衝刺 2", 14, 27));

        // Act
        var result = await _host.Planning.UpdateCycleAsync(scenario.Admin, "WEB", second.Id, CycleOf("衝刺 1", 14, 27), CancellationToken.None);

        // Assert
        AssertFailure(result, FailureKind.Conflict, WorkErrorCodes.CycleNameTaken);
    }

    [TestCase(-5, 0, TestName = "CreateCycleAsync_EndingOnTheFirstDay_Overlaps")]
    [TestCase(13, 20, TestName = "CreateCycleAsync_StartingOnTheLastDay_Overlaps")]
    [TestCase(3, 5, TestName = "CreateCycleAsync_InsideAnother_Overlaps")]
    [TestCase(-3, 30, TestName = "CreateCycleAsync_ContainingAnother_Overlaps")]
    public async Task CreateCycleAsync_OverlappingDates_NamesTheOtherCycle(int start, int end)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("衝刺 1", 0, 13));

        // Act
        var result = await _host.Planning.CreateCycleAsync(scenario.Admin, "WEB", CycleOf("衝刺 2", start, end), CancellationToken.None);

        // Assert
        AssertFailure(result, FailureKind.Conflict, WorkErrorCodes.CycleOverlaps);
        Assert.That(result.Failure!.Message, Does.Contain("衝刺 1"));
    }

    [Test]
    public async Task CreateCycleAsync_TheDayBeforeOrAfterAnother_IsNotAnOverlap()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("衝刺 2", 14, 27));

        // Act
        var before = await _host.Planning.CreateCycleAsync(scenario.Admin, "WEB", CycleOf("衝刺 1", 0, 13), CancellationToken.None);
        var after = await _host.Planning.CreateCycleAsync(scenario.Admin, "WEB", CycleOf("衝刺 3", 28, 41), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(before.Succeeded, Is.True);
            Assert.That(after.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task UpdateCycleAsync_IntoAnotherCyclesDates_Overlaps()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("衝刺 1", 0, 13));
        var second = await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("衝刺 2", 14, 27));

        // Act
        var result = await _host.Planning.UpdateCycleAsync(scenario.Admin, "WEB", second.Id, CycleOf("衝刺 2", 10, 27), CancellationToken.None);

        // Assert
        AssertFailure(result, FailureKind.Conflict, WorkErrorCodes.CycleOverlaps);
    }

    [Test]
    public async Task CreateCycleAsync_SameNameSentConcurrently_OnlyOneSucceeds()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => CreateInOwnRequestAsync(scenario, CycleOf("同名", i * 20, i * 20 + 5))));

        // Assert
        Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(1));
        Assert.That(results.Where(r => !r.Succeeded).Select(r => r.Failure!.Code), Has.All.EqualTo(WorkErrorCodes.CycleNameTaken));
    }

    [Test]
    public async Task CreateCycleAsync_OverlappingDatesSentConcurrently_OnlyOneSucceeds()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => CreateInOwnRequestAsync(scenario, CycleOf($"衝刺 {i}", i, i + 10))));

        // Assert
        Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(1));
        Assert.That(results.Where(r => !r.Succeeded).Select(r => r.Failure!.Code), Has.All.EqualTo(WorkErrorCodes.CycleOverlaps));
    }

    [Test]
    public async Task CreateCycleAsync_PastTheLimit_IsLimitReached()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        for (var i = 0; i < PlanningRules.MaxPerProject; i++)
        {
            await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf($"週期 {i}", i * 2, i * 2));
        }

        // Act
        var result = await _host.Planning.CreateCycleAsync(scenario.Admin, "WEB", CycleOf("多一個", 1_000, 1_001), CancellationToken.None);

        // Assert
        AssertFailure(result, FailureKind.Conflict, WorkErrorCodes.CycleLimitReached);
    }

    [TestCase("", 0, 5, "name", TestName = "CreateCycleAsync_EmptyName_IsInvalid")]
    [TestCase("   ", 0, 5, "name", TestName = "CreateCycleAsync_BlankName_IsInvalid")]
    [TestCase("a\0b", 0, 5, "name", TestName = "CreateCycleAsync_NulInName_IsInvalid")]
    [TestCase("a\nb", 0, 5, "name", TestName = "CreateCycleAsync_NewlineInName_IsInvalid")]
    [TestCase("a​b", 0, 5, "name", TestName = "CreateCycleAsync_ZeroWidthSpaceInName_IsInvalid")]
    [TestCase("a‮b", 0, 5, "name", TestName = "CreateCycleAsync_DirectionOverrideInName_IsInvalid")]
    [TestCase("五十一個字五十一個字五十一個字五十一個字五十一個字五十一個字五十一個字五十一個字五十一個字五十一個字五", 0, 5, "name", TestName = "CreateCycleAsync_NameOver50Characters_IsInvalid")]
    [TestCase("衝刺", 5, 4, "endDate", TestName = "CreateCycleAsync_EndBeforeStart_IsInvalid")]
    [TestCase("衝刺", 0, 365, "endDate", TestName = "CreateCycleAsync_Over365Days_IsInvalid")]
    public async Task CreateCycleAsync_InvalidFields_ReportsThePath(string name, int start, int end, string path)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Planning.CreateCycleAsync(scenario.Admin, "WEB", CycleOf(name, start, end), CancellationToken.None);

        // Assert
        AssertFailure(result, FailureKind.Invalid, WorkErrorCodes.PlanningInvalid);
        Assert.That(result.Failure!.Details.Select(d => (d.Code, d.Path)), Does.Contain((WorkErrorCodes.PlanningInvalid, path)));
    }

    [Test]
    public async Task CreateCycleAsync_LoneSurrogateInName_IsInvalid()
    {
        // Arrange：落單的代理字元轉不成 UTF-8，寫進資料庫會出錯。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var name = new string(['衝', '\ud800', '刺']);

        // Act
        var result = await _host.Planning.CreateCycleAsync(scenario.Admin, "WEB", CycleOf(name, 0, 5), CancellationToken.None);

        // Assert
        AssertFailure(result, FailureKind.Invalid, WorkErrorCodes.PlanningInvalid);
    }

    [Test]
    public async Task CreateCycleAsync_Exactly365DaysAndExactly50Characters_AreAccepted()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Planning.CreateCycleAsync(
            scenario.Admin, "WEB", CycleOf(new string('字', PlanningRules.MaxCycleNameLength), 0, PlanningRules.MaxCycleDays - 1), CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
    }

    [Test]
    public async Task CreateCycleAsync_NameAndDatesBothWrong_ReportsEveryProblem()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Planning.CreateCycleAsync(scenario.Admin, "WEB", CycleOf("", 5, 4), CancellationToken.None);

        // Assert
        Assert.That(result.Failure!.Details.Select(d => d.Path), Is.EquivalentTo(new[] { "name", "endDate" }));
    }

    [Test]
    public async Task Permissions_ReadNeedsItemReadAndWriteNeedsItemUpdate()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var viewer = await scenario.MemberAsync("WEB", "viewer");
        var editor = await scenario.MemberAsync("WEB", "editor");
        var stranger = scenario.Person();
        var cycle = await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("衝刺 1", 0, 13));

        // Act
        var viewerList = await _host.Planning.ListCyclesAsync(viewer, "WEB", CancellationToken.None);
        var viewerCreate = await _host.Planning.CreateCycleAsync(viewer, "WEB", CycleOf("衝刺 2", 20, 30), CancellationToken.None);
        var viewerUpdate = await _host.Planning.UpdateCycleAsync(viewer, "WEB", cycle.Id, CycleOf("改", 0, 13), CancellationToken.None);
        var viewerDelete = await _host.Planning.DeleteCycleAsync(viewer, "WEB", cycle.Id, CancellationToken.None);
        var editorCreate = await _host.Planning.CreateCycleAsync(editor, "WEB", CycleOf("衝刺 2", 20, 30), CancellationToken.None);
        var strangerList = await _host.Planning.ListCyclesAsync(stranger, "WEB", CancellationToken.None);
        var strangerCreate = await _host.Planning.CreateCycleAsync(stranger, "WEB", CycleOf("衝刺 3", 40, 50), CancellationToken.None);
        var missing = await _host.Planning.ListCyclesAsync(scenario.Admin, "NOPE", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewerList.Succeeded, Is.True);
            AssertFailure(viewerCreate, FailureKind.Forbidden, WorkErrorCodes.Forbidden);
            AssertFailure(viewerUpdate, FailureKind.Forbidden, WorkErrorCodes.Forbidden);
            AssertFailure(viewerDelete, FailureKind.Forbidden, WorkErrorCodes.Forbidden);
            Assert.That(editorCreate.Succeeded, Is.True);
            AssertFailure(strangerList, FailureKind.NotFound, WorkErrorCodes.ProjectNotFound);
            AssertFailure(strangerCreate, FailureKind.NotFound, WorkErrorCodes.ProjectNotFound);
            AssertFailure(missing, FailureKind.NotFound, WorkErrorCodes.ProjectNotFound);
        }
    }

    private async Task<Result<Cycle>> CreateInOwnRequestAsync(Scenario scenario, CycleDraft draft)
    {
        var (_, _, scope) = _host.NewRequest();
        await using (scope)
        {
            return await scope.ServiceProvider
                .GetRequiredService<IPlanningService>()
                .CreateCycleAsync(scenario.Admin, "WEB", draft, CancellationToken.None);
        }
    }
}
