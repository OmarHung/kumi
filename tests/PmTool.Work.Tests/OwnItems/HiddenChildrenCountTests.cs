using PmTool.Abstractions;

namespace PmTool.Work.Tests.OwnItems;

/// <summary>下層擋住刪除或改類型時，看不到的下層只合併成一筆說明：筆數與訊息不隨它們的數量改變。</summary>
[TestFixture]
public sealed class HiddenChildrenCountTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task Delete_OneOrThreeHiddenChildren_GivesTheSameFailureShape()
    {
        // Arrange
        var one = await DeleteWithHiddenChildrenAsync(1);
        var three = await DeleteWithHiddenChildrenAsync(3);

        // Assert
        AssertSameShape(one, three);
    }

    [Test]
    public async Task ChangeType_OneOrThreeHiddenChildren_GivesTheSameFailureShape()
    {
        // Arrange
        var one = await ChangeTypeWithHiddenChildrenAsync(1);
        var three = await ChangeTypeWithHiddenChildrenAsync(3);

        // Assert
        AssertSameShape(one, three);
    }

    [Test]
    public async Task Delete_TwoVisibleAndThreeHiddenChildren_ListsTheVisibleOnesAndOneMergedDetail()
    {
        // Arrange
        var (requirement, actor, hidden) = await RequirementWithChildrenAsync(visible: 2, hidden: 3);

        // Act
        var failure = (await _host.Items.DeleteAsync(actor, requirement.Key, CancellationToken.None)).Failure;

        // Assert
        AssertTwoNamedAndOneMerged(failure, hidden);
    }

    [Test]
    public async Task ChangeType_TwoVisibleAndThreeHiddenChildren_ListsTheVisibleOnesAndOneMergedDetail()
    {
        // Arrange
        var (requirement, actor, hidden) = await RequirementWithChildrenAsync(visible: 2, hidden: 3);

        // Act
        var failure = (await _host.Items.ChangeTypeAsync(actor, requirement.Key, "task", CancellationToken.None)).Failure;

        // Assert
        AssertTwoNamedAndOneMerged(failure, hidden);
    }

    [Test]
    public async Task ChangeType_HiddenChildrenAllowedOrNotUnderTheNewType_GiveTheSameFailure()
    {
        // Arrange：看不到的任務可以掛在需求變更單底下，卻不能掛在任務底下；兩種改法的失敗不能讓人分辨出下層的類型。
        var (requirement, actor, _) = await RequirementWithChildrenAsync(visible: 0, hidden: 2);

        // Act
        var allowed = (await _host.Items.ChangeTypeAsync(actor, requirement.Key, "change_request", CancellationToken.None)).Failure;
        var notAllowed = (await _host.Items.ChangeTypeAsync(actor, requirement.Key, "task", CancellationToken.None)).Failure;

        // Assert
        AssertSameShape(allowed, notAllowed);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(allowed!.Code, Is.EqualTo(notAllowed!.Code));
            Assert.That(allowed.Details[0].Path, Is.EqualTo(notAllowed.Details[0].Path));
            Assert.That(allowed.Details[0].Code, Is.EqualTo(notAllowed.Details[0].Code));
        }
    }

    private static void AssertTwoNamedAndOneMerged(Failure? failure, WorkItem[] hidden)
    {
        Assert.That(failure, Is.Not.Null);
        var texts = string.Join("\n", [failure!.Message, .. failure.Details.SelectMany(d => new[] { d.Path, d.Message })]);
        var merged = failure.Details.Where(d => d.Path == "children").ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure.Details, Has.Count.EqualTo(3));
            Assert.That(failure.Details.Count(d => d.Path.StartsWith("children/WEB-", StringComparison.Ordinal)), Is.EqualTo(2));
            Assert.That(merged, Has.Length.EqualTo(1));
            foreach (var item in hidden)
            {
                Assert.That(texts, Does.Not.Contain(item.Key).And.Not.Contain(item.Title));
            }
        }
    }

    private static void AssertSameShape(Failure? one, Failure? three)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(one, Is.Not.Null);
            Assert.That(three, Is.Not.Null);
            Assert.That(three!.Message, Is.EqualTo(one!.Message));
            Assert.That(three.Details, Has.Count.EqualTo(1));
            Assert.That(one.Details, Has.Count.EqualTo(1));
            Assert.That(three.Details[0].Message, Is.EqualTo(one.Details[0].Message));
        }
    }

    private async Task<Failure?> DeleteWithHiddenChildrenAsync(int hidden)
    {
        var (requirement, actor, _) = await RequirementWithChildrenAsync(visible: 0, hidden);
        return (await _host.Items.DeleteAsync(actor, requirement.Key, CancellationToken.None)).Failure;
    }

    private async Task<Failure?> ChangeTypeWithHiddenChildrenAsync(int hidden)
    {
        var (requirement, actor, _) = await RequirementWithChildrenAsync(visible: 0, hidden);
        return (await _host.Items.ChangeTypeAsync(actor, requirement.Key, "task", CancellationToken.None)).Failure;
    }

    private async Task<(WorkItem Requirement, Actor Actor, WorkItem[] Hidden)> RequirementWithChildrenAsync(int visible, int hidden)
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        await OwnItemsWorld.PublishAsync(_host, scenario, "WEB", blueprint => blueprint with
        {
            Roles =
            [
                .. blueprint.Roles,
                OwnItemsWorld.Role(
                    "owner_own", WorkPermissions.ItemReadOwn, WorkPermissions.ItemCreate, WorkPermissions.ItemUpdate, WorkPermissions.ItemDelete),
            ],
        });
        var actor = await scenario.MemberAsync("WEB", "owner_own");
        var editor = await scenario.MemberAsync("WEB", "editor");
        var requirement = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "我的需求" }, actor);
        for (var index = 0; index < visible; index++)
        {
            await scenario.CreateItemAsync(
                "WEB", new NewWorkItem { Type = "task", Title = $"我的任務 {index}", ParentKey = requirement.Key }, actor);
        }

        var others = new List<WorkItem>();
        for (var index = 0; index < hidden; index++)
        {
            others.Add(await scenario.CreateItemAsync(
                "WEB",
                new NewWorkItem { Type = "task", Title = $"別人的任務 {index}", ParentKey = requirement.Key, AssigneeId = editor.PersonId },
                editor));
        }

        return (requirement, actor, [.. others]);
    }
}
