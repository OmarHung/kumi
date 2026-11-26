using static PmTool.Work.Tests.Overview.OverviewSupport;

namespace PmTool.Work.Tests.Overview;

/// <summary>專案總覽每個數字的邊界：過期、快到期、沒有負責人、停滯、最近完成，以及已刪除的不算。</summary>
[TestFixture]
public sealed class ProjectOverviewCountsTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ListAsync_DueDates_TodayIsDueSoonNotOverdueAndDayAfterSixIsNeither()
    {
        // Arrange
        SetNow(_host, Noon);
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var member = await scenario.MemberAsync("WEB", "editor");
        await TaskAsync(scenario, "WEB", "昨天到期", Today.AddDays(-1), member.PersonId);
        await TaskAsync(scenario, "WEB", "今天到期", Today, member.PersonId);
        await TaskAsync(scenario, "WEB", "第六天到期", Today.AddDays(6), member.PersonId);
        await TaskAsync(scenario, "WEB", "第七天到期", Today.AddDays(7), member.PersonId);
        await TaskAsync(scenario, "WEB", "沒有到期日", null, member.PersonId);

        // Act
        var overview = await OneAsync(_host, scenario.Admin, "WEB");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(overview.Open, Is.EqualTo(5));
            Assert.That(overview.Overdue, Is.EqualTo(1));
            Assert.That(overview.DueSoon, Is.EqualTo(2), "今天與第六天算，第七天不算");
            Assert.That(overview.Unassigned, Is.Zero);
        }
    }

    [Test]
    public async Task ListAsync_FinishedItems_AreNotOpenOverdueDueSoonOrUnassigned()
    {
        // Arrange
        SetNow(_host, Noon);
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var late = await TaskAsync(scenario, "WEB", "過期但完成", Today.AddDays(-3));
        var soon = await TaskAsync(scenario, "WEB", "快到期但完成", Today.AddDays(1));
        await TaskAsync(scenario, "WEB", "還沒做", Today.AddDays(-3));
        await MoveAsync(scenario, late.Key, "done");
        await MoveAsync(scenario, soon.Key, "done");

        // Act
        var overview = await OneAsync(_host, scenario.Admin, "WEB");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(overview.Open, Is.EqualTo(1));
            Assert.That(overview.Overdue, Is.EqualTo(1));
            Assert.That(overview.DueSoon, Is.Zero);
            Assert.That(overview.Unassigned, Is.EqualTo(1));
            Assert.That(overview.States.Select(s => (s.Key, s.Count)), Is.EqualTo(new[] { ("todo", 1), ("done", 2) }));
        }
    }

    [Test]
    public async Task ListAsync_Stale_ExactlyFourteenDaysIsNotStaleAndAnyLaterIs()
    {
        // Arrange
        SetNow(_host, Noon);
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await TaskAsync(scenario, "WEB", "很久沒動");
        var finished = await TaskAsync(scenario, "WEB", "完成的不算停滯");
        await MoveAsync(scenario, finished.Key, "done");

        // Act
        SetNow(_host, Noon.AddDays(ProjectOverviewRules.StaleDays));
        var onTheLine = await OneAsync(_host, scenario.Admin, "WEB");
        SetNow(_host, Noon.AddDays(ProjectOverviewRules.StaleDays).AddSeconds(1));
        var past = await OneAsync(_host, scenario.Admin, "WEB");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(onTheLine.Stale, Is.Zero);
            Assert.That(past.Stale, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task ListAsync_DoneRecently_ExactlySevenDaysCountsAndLaterDoesNot()
    {
        // Arrange
        SetNow(_host, Noon);
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var finished = await TaskAsync(scenario, "WEB", "完成了");
        await MoveAsync(scenario, finished.Key, "done");
        await TaskAsync(scenario, "WEB", "還沒做");

        // Act
        SetNow(_host, Noon.AddDays(ProjectOverviewRules.DoneRecentlyDays));
        var onTheLine = await OneAsync(_host, scenario.Admin, "WEB");
        SetNow(_host, Noon.AddDays(ProjectOverviewRules.DoneRecentlyDays).AddSeconds(1));
        var past = await OneAsync(_host, scenario.Admin, "WEB");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(onTheLine.DoneRecently, Is.EqualTo(1));
            Assert.That(past.DoneRecently, Is.Zero);
        }
    }

    [Test]
    public async Task ListAsync_DeletedItems_AreNotCountedAnywhere()
    {
        // Arrange
        SetNow(_host, Noon);
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var gone = await TaskAsync(scenario, "WEB", "刪掉的", Today.AddDays(-2));
        await TaskAsync(scenario, "WEB", "留著的");
        var deleted = await _host.Items.DeleteAsync(scenario.Admin, gone.Key, CancellationToken.None);
        Assert.That(deleted.Succeeded, Is.True, () => deleted.Failure!.Message);

        // Act
        var overview = await OneAsync(_host, scenario.Admin, "WEB");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(overview.Open, Is.EqualTo(1));
            Assert.That(overview.Overdue, Is.Zero);
            Assert.That(overview.States.Sum(s => s.Count), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task ListAsync_ProjectWithoutItems_IsListedWithZerosAndNoLastUpdate()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("EMP");

        // Act
        var overview = await OneAsync(_host, scenario.Admin, "EMP");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(overview.Name, Is.EqualTo("範例專案 EMP"));
            Assert.That(overview.States, Is.Empty);
            Assert.That(overview.Open, Is.Zero);
            Assert.That(overview.LastUpdatedAt, Is.Null);
            Assert.That(overview.CurrentCycle, Is.Null);
            Assert.That(overview.NextMilestone, Is.Null);
        }
    }

    [Test]
    public async Task ListAsync_LastUpdatedAt_IsTheLatestUpdateOfAnyVisibleItemIncludingFinishedOnes()
    {
        // Arrange
        SetNow(_host, Noon);
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var first = await TaskAsync(scenario, "WEB", "先建立");
        await TaskAsync(scenario, "WEB", "後建立");
        SetNow(_host, Noon.AddHours(5));
        await MoveAsync(scenario, first.Key, "done");

        // Act
        var overview = await OneAsync(_host, scenario.Admin, "WEB");

        // Assert
        Assert.That(overview.LastUpdatedAt, Is.EqualTo(Noon.AddHours(5)));
    }
}
