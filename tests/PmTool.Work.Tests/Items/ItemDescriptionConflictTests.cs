using PmTool.Abstractions;

namespace PmTool.Work.Tests.Items;

/// <summary>說明的衝突檢查：帶著開始編輯時的版本改說明，別人中間改過就整個不改。</summary>
[TestFixture]
public sealed class ItemDescriptionConflictTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task UpdateAsync_WithTheCurrentRevision_ChangesTheDescription()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", Description = "原本" });
        var patch = new WorkItemPatch { Description = "新的", ExpectedDescriptionRevision = item.DescriptionRevision };

        // Act
        var result = await _host.Items.UpdateAsync(scenario.Admin, item.Key, patch, CancellationToken.None);

        // Assert
        Assert.That(result.Value.Description, Is.EqualTo("新的"));
        Assert.That(result.Value.DescriptionRevision, Is.Not.EqualTo(item.DescriptionRevision));
    }

    [Test]
    public async Task UpdateAsync_WithARevisionSomeoneElseAlreadyReplaced_ConflictsAndChangesNothing()
    {
        // Arrange：兩個人從同一個版本開始編輯，B 先存。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", Description = "原本" });
        await _host.Items.UpdateAsync(
            scenario.Admin, item.Key, new WorkItemPatch { Description = "B 的", ExpectedDescriptionRevision = item.DescriptionRevision }, CancellationToken.None);

        // Act：A 帶著舊版本，連標題一起改。
        var result = await _host.Items.UpdateAsync(
            scenario.Admin,
            item.Key,
            new WorkItemPatch { Title = "A 改的標題", Description = "A 的", ExpectedDescriptionRevision = item.DescriptionRevision },
            CancellationToken.None);
        var current = await _host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        ItemAssert.Failed(result, FailureKind.Conflict, WorkErrorCodes.ItemDescriptionConflict);
        Assert.That((current.Value.Title, current.Value.Description), Is.EqualTo(("任務", "B 的")));
    }

    [Test]
    public async Task UpdateAsync_WithoutARevision_DoesNotCheck()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", Description = "原本" });
        await _host.Items.UpdateAsync(scenario.Admin, item.Key, new WorkItemPatch { Description = "別人的" }, CancellationToken.None);

        // Act
        var result = await _host.Items.UpdateAsync(scenario.Admin, item.Key, new WorkItemPatch { Description = "我的" }, CancellationToken.None);

        // Assert
        Assert.That(result.Value.Description, Is.EqualTo("我的"));
    }

    [Test]
    public async Task UpdateAsync_StaleRevisionButNoDescriptionInThePatch_IsIgnored()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", Description = "原本" });
        await _host.Items.UpdateAsync(scenario.Admin, item.Key, new WorkItemPatch { Description = "別人的" }, CancellationToken.None);

        // Act
        var result = await _host.Items.UpdateAsync(
            scenario.Admin, item.Key, new WorkItemPatch { Title = "新標題", ExpectedDescriptionRevision = item.DescriptionRevision }, CancellationToken.None);

        // Assert
        Assert.That((result.Value.Title, result.Value.Description), Is.EqualTo(("新標題", "別人的")));
    }

    [Test]
    public async Task UpdateAsync_ItemWithoutADescription_RevisionOfNothingMatches()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        var result = await _host.Items.UpdateAsync(
            scenario.Admin,
            item.Key,
            new WorkItemPatch { Description = "第一次寫", ExpectedDescriptionRevision = DescriptionRevisions.Of(null) },
            CancellationToken.None);

        // Assert
        Assert.That(result.Value.Description, Is.EqualTo("第一次寫"));
    }

    [Test]
    public async Task UpdateAsync_TwoEditorsSavingAtTheSameTime_OnlyOneWins()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", Description = "原本" });
        var revision = item.DescriptionRevision;

        // Act：同樣的版本、同時存；鎖住之後比對，一定只有一個成功。
        var requests = Enumerable.Range(0, 4).Select(_ => _host.NewRequest()).ToArray();
        var results = await Task.WhenAll(requests.Select((request, index) => Task.Run(() => request.Items.UpdateAsync(
            scenario.Admin, item.Key, new WorkItemPatch { Description = $"第 {index} 個", ExpectedDescriptionRevision = revision }, CancellationToken.None))));
        foreach (var request in requests)
        {
            await request.Scope.DisposeAsync();
        }

        // Assert
        Assert.That(results.Count(r => r.Failure is null), Is.EqualTo(1));
        Assert.That(results.Where(r => r.Failure is not null).Select(r => r.Failure!.Code), Is.All.EqualTo(WorkErrorCodes.ItemDescriptionConflict));
    }
}
