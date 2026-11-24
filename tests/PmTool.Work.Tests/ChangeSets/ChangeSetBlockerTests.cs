using PmTool.Abstractions;
using PmTool.Blueprint.Model;
using PmTool.Blueprint.Planning;

namespace PmTool.Work.Tests.ChangeSets;

/// <summary>
/// 資料讓變更集套用不了：被刪除的類型底下還有項目、不再允許的上下層還掛著、還有成員是被刪除的角色。
/// 送出時就看得到原因與數量；資料處理好之後，再讀一次就能套用。
/// </summary>
[TestFixture]
public sealed class ChangeSetBlockerTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ApplyAsync_RemovedTypeStillHasItemsAndRemovedRoleStillHasMembers_IsBlockedWithTheCounts()
    {
        // Arrange：三個需求變更單、一位客戶。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        await scenario.MemberAsync("WEB", "client");
        foreach (var title in new[] { "加上社群登入", "改結帳流程", "多一種付款方式" })
        {
            await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "change_request", Title = title });
        }

        var proposed = await ChangeSetDrafts.ProposeAsync(scenario, "WEB", b => WithoutClient(WithoutChangeRequests(b)));

        // Act
        var result = await _host.Changes.ApplyAsync(scenario.Admin, "WEB", proposed.ChangeSet.Number, CancellationToken.None);
        var project = await _host.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        (string, string, string)[] blockers =
        [
            (WorkErrorCodes.ChangeSetItemsOfRemovedType, "impacts/0", "「需求變更單」底下還有 3 個項目，要先處理才能刪除這個類型。"),
            (WorkErrorCodes.ChangeSetMembersWithRemovedRole, "impacts/1", "還有 1 位成員的角色是「客戶」，要先換成別的角色，才能刪除這個角色。"),
        ];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                proposed.Impacts.Select(i => (i.Impact, i.Affected, i.BlocksApply)),
                Is.EqualTo(new[]
                {
                    ((DataImpact)new ItemTypeRemovalImpact("change_request"), 3L, true),
                    (new RoleRemovalImpact("client"), 1L, true),
                }));
            Assert.That(proposed.Blockers.Select(b => (b.Code, b.Path, b.Message)), Is.EqualTo(blockers));
            Assert.That(proposed.CanApply, Is.False);
            Assert.That((result.Failure!.Kind, result.Failure.Code), Is.EqualTo((FailureKind.Conflict, WorkErrorCodes.ChangeSetApplyBlocked)));
            Assert.That(result.Failure.Details.Select(b => (b.Code, b.Path, b.Message)), Is.EqualTo(blockers));
            Assert.That(result.Failure.Message, Is.EqualTo($"變更集 #1 現在還不能套用：{blockers[0].Item3}（另外還有 1 個原因）"));
            Assert.That(project.Value.Project.BlueprintVersion, Is.EqualTo(1));
            Assert.That(project.Value.Blueprint.Roles.Select(r => r.Key), Does.Contain("client"));
        }
    }

    [Test]
    public async Task GetAsync_AfterTheBlockingDataIsGone_RecountsAndCanApply()
    {
        // Arrange：沒有需求變更單，只有一位客戶擋著；把他改成檢視者之後就能套用。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var client = await scenario.MemberAsync("WEB", "client");
        var proposed = await ChangeSetDrafts.ProposeAsync(scenario, "WEB", b => WithoutClient(WithoutChangeRequests(b)));
        await _host.Projects.SetMemberAsync(scenario.Admin, "WEB", client.PersonId, "viewer", CancellationToken.None);

        // Act
        var recounted = await _host.Changes.GetAsync(scenario.Admin, "WEB", proposed.ChangeSet.Number, CancellationToken.None);
        var applied = await _host.Changes.ApplyAsync(scenario.Admin, "WEB", proposed.ChangeSet.Number, CancellationToken.None);
        var project = await _host.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        Assert.That(proposed.Impacts.Select(i => (i.Affected, i.BlocksApply)), Is.EqualTo(new[] { (0L, false), (1L, true) }));
        Assert.That(recounted.Value.Impacts.Select(i => (i.Affected, i.BlocksApply)), Is.EqualTo(new[] { (0L, false), (0L, false) }));
        Assert.That((recounted.Value.Blockers.Count, recounted.Value.CanApply), Is.EqualTo((0, true)));
        Assert.That(applied.Succeeded, Is.True, () => ChangeSetDrafts.Describe(applied.Failure!));
        Assert.That(project.Value.Blueprint.ItemTypes.Select(t => t.Key), Is.EqualTo(new[] { "requirement", "task", "subtask" }));
    }

    [Test]
    public async Task ApplyAsync_ParentNoLongerAllowedButStillUsed_IsBlockedUntilTheChildrenMove()
    {
        // Arrange：任務不能再掛在需求變更單底下；兩個任務還掛著，一個掛在需求底下。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var changeRequest = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "change_request", Title = "加上社群登入" });
        var requirement = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "會員登入" });
        var children = new List<WorkItem>();
        foreach (var parent in new[] { changeRequest, changeRequest, requirement })
        {
            children.Add(await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "子任務", ParentKey = parent.Key }));
        }

        var proposed = await ChangeSetDrafts.ProposeAsync(scenario, "WEB", b => b.ItemType("task", t => t with { Parents = ["requirement"] }));

        // Act
        var blocked = await _host.Changes.ApplyAsync(scenario.Admin, "WEB", proposed.ChangeSet.Number, CancellationToken.None);
        foreach (var child in children.Take(2))
        {
            await _host.Items.UpdateAsync(scenario.Admin, child.Key, new WorkItemPatch { ParentKey = requirement.Key }, CancellationToken.None);
        }

        var applied = await _host.Changes.ApplyAsync(scenario.Admin, "WEB", proposed.ChangeSet.Number, CancellationToken.None);

        // Assert
        Assert.That(
            proposed.Impacts.Select(i => (i.Impact, i.Affected, i.BlocksApply)),
            Is.EqualTo(new[] { ((DataImpact)new ParentRemovalImpact("task", "change_request"), 2L, true) }));
        Assert.That(
            blocked.Failure!.Details.Select(d => (d.Code, d.Message)),
            Is.EqualTo(new[] { (WorkErrorCodes.ChangeSetItemsUnderRemovedParent, "還有 2 個「任務」掛在「需求變更單」底下，要先把它們移開，才能取消這個上層關係。") }));
        Assert.That(applied.Succeeded, Is.True, () => ChangeSetDrafts.Describe(applied.Failure!));
    }

    /// <summary>拿掉需求變更單這個類型，連同引用它的上層與檢視的篩選。</summary>
    private static ProjectBlueprint WithoutChangeRequests(ProjectBlueprint blueprint) =>
        blueprint with
        {
            ItemTypes = [.. blueprint.ItemTypes
                .Where(t => t.Key != "change_request")
                .Select(t => t with { Parents = [.. t.Parents.Where(p => p != "change_request")] })],
            Views = [.. blueprint.Views.Select(v => v.Filter is { } filter
                ? v with { Filter = filter with { ItemTypes = [.. filter.ItemTypes.Where(t => t != "change_request")] } }
                : v)],
        };

    /// <summary>拿掉客戶這個角色，連同只給客戶看的檢視設定。</summary>
    private static ProjectBlueprint WithoutClient(ProjectBlueprint blueprint) =>
        blueprint with
        {
            Roles = [.. blueprint.Roles.Where(r => r.Key != "client")],
            Views = [.. blueprint.Views.Select(v => v with { VisibleTo = v.VisibleTo is null ? null : [.. v.VisibleTo.Where(r => r != "client")] })],
        };
}
