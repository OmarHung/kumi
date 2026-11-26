using PmTool.Abstractions;
using PmTool.Work.Tests.OwnItems;
using static PmTool.Work.Tests.Overview.OverviewSupport;

namespace PmTool.Work.Tests.Overview;

/// <summary>專案總覽的範圍：管理的／看得到的、封存的與看不到的專案、只看自己的人，以及組織時區的「今天」。</summary>
[TestFixture]
public sealed class ProjectOverviewScopeTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ListAsync_Member_ManagedListsOnlyProjectsTheyManageAndVisibleListsEveryProjectTheyCanRead()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("AAA");
        await scenario.CreateProjectAsync("BBB");
        await scenario.CreateProjectAsync("CCC");
        var person = scenario.Person();
        await AddAsync(scenario, "AAA", person, "admin");
        await AddAsync(scenario, "BBB", person, "viewer");

        // Act
        var managed = await ListAsync(_host, person, ProjectOverviewScope.Managed);
        var visible = await ListAsync(_host, person, ProjectOverviewScope.Visible);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(managed.Select(o => o.ProjectKey), Is.EqualTo(new[] { "AAA" }));
            Assert.That(visible.Select(o => o.ProjectKey), Is.EqualTo(new[] { "AAA", "BBB" }), "沒加進去的 CCC 不出現，依代號排序");
        }
    }

    [Test]
    public async Task ListAsync_OrganizationAdmin_ManagedAndVisibleBothListEveryProject()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("AAA");
        await scenario.CreateProjectAsync("BBB");

        // Act
        var managed = await ListAsync(_host, scenario.Admin, ProjectOverviewScope.Managed);
        var visible = await ListAsync(_host, scenario.Admin, ProjectOverviewScope.Visible);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(managed.Select(o => o.ProjectKey), Is.EqualTo(new[] { "AAA", "BBB" }));
            Assert.That(visible.Select(o => o.ProjectKey), Is.EqualTo(new[] { "AAA", "BBB" }));
            Assert.That(managed.Concat(visible).Any(o => o.OwnItemsOnly), Is.False);
        }
    }

    [Test]
    public async Task ListAsync_ArchivedProject_IsNotListedForAnyone()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("AAA");
        await scenario.CreateProjectAsync("OLD");
        var person = scenario.Person();
        await AddAsync(scenario, "AAA", person, "admin");
        await AddAsync(scenario, "OLD", person, "admin");
        var archived = await _host.Projects.ArchiveAsync(scenario.Admin, "OLD", CancellationToken.None);
        Assert.That(archived.Succeeded, Is.True, () => archived.Failure!.Message);

        // Act
        var forAdmin = await ListAsync(_host, scenario.Admin);
        var forMember = await ListAsync(_host, person, ProjectOverviewScope.Managed);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(forAdmin.Select(o => o.ProjectKey), Is.EqualTo(new[] { "AAA" }));
            Assert.That(forMember.Select(o => o.ProjectKey), Is.EqualTo(new[] { "AAA" }));
        }
    }

    [Test]
    public async Task ListAsync_OtherOrganizationsProjects_AreNotListed()
    {
        // Arrange
        var mine = new Scenario(_host);
        var theirs = new Scenario(_host);
        await mine.CreateProjectAsync("MIN");
        await theirs.CreateProjectAsync("THE");

        // Act
        var overview = await ListAsync(_host, mine.Admin);

        // Assert
        Assert.That(overview.Select(o => o.ProjectKey), Is.EqualTo(new[] { "MIN" }));
    }

    [Test]
    public async Task ListAsync_OwnItemsOnlyRole_CountsOnlyTheirOwnItemsAndFlagsTheProject()
    {
        // Arrange
        SetNow(_host, Noon);
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var contributor = await scenario.MemberAsync("WEB", "contributor");
        var editor = await scenario.MemberAsync("WEB", "editor");
        await TaskAsync(scenario, "WEB", "指派給執行者的過期項目", Today.AddDays(-1), contributor.PersonId, editor);
        await TaskAsync(scenario, "WEB", "執行者自己建的", Today.AddDays(2), actor: contributor);
        SetNow(_host, Noon.AddHours(3));
        var hidden = await TaskAsync(scenario, "WEB", "別人的過期項目", Today.AddDays(-9), editor.PersonId, editor);
        await MoveAsync(scenario, hidden.Key, "doing", editor);

        // Act
        var own = await OneAsync(_host, contributor, "WEB");
        var all = await OneAsync(_host, editor, "WEB");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(own.OwnItemsOnly, Is.True);
            Assert.That(own.Open, Is.EqualTo(2));
            Assert.That(own.Overdue, Is.EqualTo(1));
            Assert.That(own.DueSoon, Is.EqualTo(1));
            Assert.That(own.Unassigned, Is.EqualTo(1));
            Assert.That(own.States.Select(s => (s.Key, s.Count)), Is.EqualTo(new[] { ("todo", 2) }), "看不到的項目所在的狀態也不出現");
            Assert.That(own.LastUpdatedAt, Is.EqualTo(Noon), "看不到的項目比較晚更新，不能從這裡推出來");
            Assert.That(all.OwnItemsOnly, Is.False);
            Assert.That(all.Open, Is.EqualTo(3));
            Assert.That(all.LastUpdatedAt, Is.EqualTo(Noon.AddHours(3)));
        }
    }

    [Test]
    public async Task ListAsync_OwnItemsOnlyRoleThatManagesTheProject_IsListedAsManagedWithTheirOwnNumbers()
    {
        // Arrange
        SetNow(_host, Noon);
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await OwnItemsWorld.PublishAsync(_host, scenario, "WEB", blueprint => blueprint with
        {
            Roles = [.. blueprint.Roles, OwnItemsWorld.Role("lead", WorkPermissions.ItemReadOwn, WorkPermissions.ProjectManage, WorkPermissions.BlueprintRead)],
        });
        var lead = await scenario.MemberAsync("WEB", "lead");
        var editor = await scenario.MemberAsync("WEB", "editor");
        await TaskAsync(scenario, "WEB", "自己的", Today.AddDays(-1), lead.PersonId, editor);
        await TaskAsync(scenario, "WEB", "別人的", Today.AddDays(-1), editor.PersonId, editor);

        // Act
        var managed = await ListAsync(_host, lead, ProjectOverviewScope.Managed);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(managed.Select(o => o.ProjectKey), Is.EqualTo(new[] { "WEB" }));
            Assert.That(managed.Single().OwnItemsOnly, Is.True);
            Assert.That(managed.Single().Overdue, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task ListAsync_ManagerRoleWithoutAnyItemReadPermission_IsNotListedEvenAsManaged()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await OwnItemsWorld.PublishAsync(_host, scenario, "WEB", blueprint => blueprint with
        {
            Roles = [.. blueprint.Roles, OwnItemsWorld.Role("blind", WorkPermissions.ProjectManage, WorkPermissions.BlueprintRead)],
        });
        var blind = await scenario.MemberAsync("WEB", "blind");

        // Act
        var managed = await ListAsync(_host, blind, ProjectOverviewScope.Managed);

        // Assert
        Assert.That(managed, Is.Empty);
    }

    [Test]
    public async Task ListAsync_OrganizationTimeZone_DecidesWhichDayTodayIs()
    {
        // Arrange：UTC 還是 11-02 的晚上八點，台北（UTC+8）已經是 11-03 的凌晨四點。
        SetNow(_host, new DateTimeOffset(2026, 11, 2, 20, 0, 0, TimeSpan.Zero));
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var taipeiToday = new DateOnly(2026, 11, 3);
        await TaskAsync(scenario, "WEB", "UTC 的今天到期", taipeiToday.AddDays(-1));
        await TaskAsync(scenario, "WEB", "台北的今天到期", taipeiToday);
        await TaskAsync(scenario, "WEB", "台北第六天", taipeiToday.AddDays(6));
        await TaskAsync(scenario, "WEB", "台北第七天", taipeiToday.AddDays(7));

        // Act
        var overview = await OneAsync(_host, scenario.Admin, "WEB");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(overview.Overdue, Is.EqualTo(1), "11-02 在台北已經過了");
            Assert.That(overview.DueSoon, Is.EqualTo(2), "11-03 與 11-09 都在台北的 7 天內");
        }
    }
}
