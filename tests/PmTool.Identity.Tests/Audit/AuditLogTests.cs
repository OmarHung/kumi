using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Identity.Audit;

namespace PmTool.Identity.Tests.Audit;

/// <summary>讀稽核紀錄：權限、分頁、篩選與組織的隔離。寫入是模組內部的事，這裡只拿寫入工具造資料。</summary>
[TestFixture]
public sealed class AuditLogTests
{
    private static readonly CancellationToken NoCancel = CancellationToken.None;

    private static Task<IdentityTestHost> StartHostAsync(ManualTimeProvider? time = null) =>
        IdentityTestHost.StartAsync(services => services.AddSingleton<TimeProvider>(time ?? new ManualTimeProvider()));

    private static Actor OwnerActor(AuthenticatedUser owner) => new(owner.OrganizationId, owner.PersonId, IsOrganizationAdmin: true);

    [Test]
    public async Task ListAsync_Owner_ReturnsTheNewestFirst()
    {
        // Arrange
        var time = new ManualTimeProvider();
        await using var host = await StartHostAsync(time);
        var owner = await host.StartWithOwnerAsync();
        var first = await host.AddEventAsync(owner.OrganizationId, AuditActions.PasswordChanged, owner.PersonId);
        time.Advance(TimeSpan.FromMinutes(1));
        var second = await host.AddEventAsync(owner.OrganizationId, AuditActions.AccessTokenCreated, owner.PersonId);

        // Act
        var page = await host.Audit.ListAsync(OwnerActor(owner), new AuditQuery(), NoCancel);

        // Assert
        Assert.That(page.Succeeded, Is.True, page.Failure?.Message);
        Assert.That(page.Value.Items.Select(entry => entry.EventId), Is.EqualTo(new[] { second, first }));
        Assert.That(page.Value.NextCursor, Is.Null);
    }

    [Test]
    public async Task ListAsync_ReturnsTheRecordedFields()
    {
        // Arrange
        await using var host = await StartHostAsync();
        var owner = await host.StartWithOwnerAsync();
        var target = await host.AddUserAsync(OwnerActor(owner));
        host.Request.Set("203.0.113.5", "example-agent/1.0");
        var details = new Dictionary<string, string> { ["tokenName"] = "CI", ["scopes"] = "items:read items:write" };
        await host.AddEventAsync(owner.OrganizationId, AuditActions.AccessTokenCreated, owner.PersonId, target.PersonId, AuditOutcomes.Failed, details);

        // Act
        var page = await host.Audit.ListAsync(OwnerActor(owner), new AuditQuery(), NoCancel);

        // Assert
        var entry = page.Value.Items.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.Action, Is.EqualTo(AuditActions.AccessTokenCreated));
            Assert.That(entry.Outcome, Is.EqualTo(AuditOutcomes.Failed));
            Assert.That(entry.ActorPersonId, Is.EqualTo(owner.PersonId));
            Assert.That(entry.TargetPersonId, Is.EqualTo(target.PersonId));
            Assert.That(entry.SourceAddress, Is.EqualTo("203.0.113.5"));
            Assert.That(entry.Details, Is.EqualTo(details));
        }
    }

    [Test]
    public async Task ListAsync_NoRequest_RecordsNoSource()
    {
        // Arrange：背景工作沒有請求
        await using var host = await StartHostAsync();
        var owner = await host.StartWithOwnerAsync();
        await host.AddEventAsync(owner.OrganizationId, AuditActions.PersonCreated);

        // Act
        var page = await host.Audit.ListAsync(OwnerActor(owner), new AuditQuery(), NoCancel);

        // Assert
        Assert.That(page.Value.Items.Single().SourceAddress, Is.Null);
    }

    [TestCase(OrganizationRole.Admin, TestName = "ListAsync_Admin_IsForbidden")]
    [TestCase(OrganizationRole.Member, TestName = "ListAsync_Member_IsForbidden")]
    public async Task ListAsync_NotAnOwner_IsForbidden(OrganizationRole role)
    {
        // Arrange：就算呼叫端自稱是管理員，也要以資料庫裡的角色為準
        await using var host = await StartHostAsync();
        var owner = await host.StartWithOwnerAsync();
        var other = await host.AddUserAsync(OwnerActor(owner), role);
        await host.AddEventAsync(owner.OrganizationId, AuditActions.PersonCreated);

        // Act
        var page = await host.Audit.ListAsync(new Actor(other.OrganizationId, other.PersonId, IsOrganizationAdmin: true), new AuditQuery(), NoCancel);

        // Assert
        Assert.That(page.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
        Assert.That(page.Failure?.Code, Is.EqualTo(IdentityErrorCodes.Forbidden));
    }

    [Test]
    public async Task ListAsync_OwnerWhoWasDemoted_IsForbiddenWithoutRestart()
    {
        // Arrange：每次都問資料庫，角色剛被降級的人馬上看不到
        await using var host = await StartHostAsync();
        var owner = await host.StartWithOwnerAsync();
        var second = await host.AddUserAsync(OwnerActor(owner), OrganizationRole.Owner);
        var secondActor = new Actor(second.OrganizationId, second.PersonId, IsOrganizationAdmin: true);
        var before = await host.Audit.ListAsync(secondActor, new AuditQuery(), NoCancel);
        await using (var db = await host.CreateDbContextAsync())
        {
            await db.People.Where(p => p.Id == second.PersonId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Role, OrganizationRole.Member));
        }

        // Act
        var after = await host.Audit.ListAsync(secondActor, new AuditQuery(), NoCancel);

        // Assert
        Assert.That((before.Succeeded, after.Failure?.Code), Is.EqualTo((true, IdentityErrorCodes.Forbidden)));
    }

    [Test]
    public async Task ListAsync_OtherOrganization_SeesNothingOfThisOne()
    {
        // Arrange
        await using var host = await StartHostAsync();
        var owner = await host.StartWithOwnerAsync();
        await host.AddEventAsync(owner.OrganizationId, AuditActions.PersonCreated);
        var other = await host.AddOrganizationAsync("另一個範例組織");
        var otherOwnerId = await host.AddPersonWithoutAccountAsync(other.OrganizationId, "另一位擁有者", OrganizationRole.Owner);
        var otherEvent = await host.AddEventAsync(other.OrganizationId, AuditActions.PersonCreated);

        // Act
        var page = await host.Audit.ListAsync(new Actor(other.OrganizationId, otherOwnerId, true), new AuditQuery(), NoCancel);
        var own = await host.Audit.ListAsync(OwnerActor(owner), new AuditQuery(), NoCancel);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Value.Items.Select(e => e.EventId), Is.EqualTo(new[] { otherEvent }));
            Assert.That(own.Value.Items, Has.Count.EqualTo(1));
            Assert.That(own.Value.Items.Select(e => e.EventId), Does.Not.Contain(otherEvent));
        }
    }

    [Test]
    public async Task ListAsync_OwnerOfAnotherOrganization_CannotBorrowThisOnesPeople()
    {
        // Arrange：別的組織的擁有者拿這個組織的人編號當篩選，也只會看到自己組織的紀錄
        await using var host = await StartHostAsync();
        var owner = await host.StartWithOwnerAsync();
        await host.AddEventAsync(owner.OrganizationId, AuditActions.PasswordReset, owner.PersonId, owner.PersonId);
        var other = await host.AddOrganizationAsync("另一個範例組織");
        var otherOwnerId = await host.AddPersonWithoutAccountAsync(other.OrganizationId, "另一位擁有者", OrganizationRole.Owner);

        // Act
        var page = await host.Audit.ListAsync(new Actor(other.OrganizationId, otherOwnerId, true), new AuditQuery(PersonId: owner.PersonId), NoCancel);

        // Assert
        Assert.That(page.Value.Items, Is.Empty);
    }

    [Test]
    public async Task ListAsync_PagesWithoutDuplicatesOrGaps_EvenWhenTimesAreEqual()
    {
        // Arrange：時鐘不動，五筆的時間完全一樣，只能靠編號分先後
        await using var host = await StartHostAsync();
        var owner = await host.StartWithOwnerAsync();
        var all = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            all.Add(await host.AddEventAsync(owner.OrganizationId, AuditActions.SignIn, owner.PersonId));
        }

        // Act
        var seen = new List<Guid>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await host.Audit.ListAsync(OwnerActor(owner), new AuditQuery(cursor, Limit: 2), NoCancel);
            seen.AddRange(page.Value.Items.Select(entry => entry.EventId));
            cursor = page.Value.NextCursor;
            pages++;
        }
        while (cursor is not null && pages < 10);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(pages, Is.EqualTo(3));
            Assert.That(seen, Is.EquivalentTo(all));
            Assert.That(seen, Is.Unique);
        }
    }

    [Test]
    public async Task ListAsync_ExactlyOnePageOfRows_HasNoNextCursor()
    {
        // Arrange
        await using var host = await StartHostAsync();
        var owner = await host.StartWithOwnerAsync();
        await host.AddEventAsync(owner.OrganizationId, AuditActions.SignIn);
        await host.AddEventAsync(owner.OrganizationId, AuditActions.SignIn);

        // Act
        var page = await host.Audit.ListAsync(OwnerActor(owner), new AuditQuery(Limit: 2), NoCancel);

        // Assert
        Assert.That((page.Value.Items.Count, page.Value.NextCursor), Is.EqualTo((2, (string?)null)));
    }

    [Test]
    public async Task ListAsync_NoLimitGiven_ReturnsFiftyAtMost()
    {
        // Arrange
        await using var host = await StartHostAsync();
        var owner = await host.StartWithOwnerAsync();
        for (var i = 0; i < AuditRules.DefaultLimit + 3; i++)
        {
            await host.AddEventAsync(owner.OrganizationId, AuditActions.SignIn);
        }

        // Act
        var page = await host.Audit.ListAsync(OwnerActor(owner), new AuditQuery(), NoCancel);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Value.Items, Has.Count.EqualTo(AuditRules.DefaultLimit));
            Assert.That(page.Value.NextCursor, Is.Not.Null);
        }
    }

    [Test]
    public async Task ListAsync_FilteredByPerson_ReturnsWhatTheyDidAndWhatWasDoneToThem()
    {
        // Arrange
        await using var host = await StartHostAsync();
        var owner = await host.StartWithOwnerAsync();
        var member = await host.AddUserAsync(OwnerActor(owner));
        var third = await host.AddUserAsync(OwnerActor(owner));
        var byMember = await host.AddEventAsync(owner.OrganizationId, AuditActions.AccessTokenCreated, member.PersonId);
        var toMember = await host.AddEventAsync(owner.OrganizationId, AuditActions.PasswordReset, owner.PersonId, member.PersonId);
        await host.AddEventAsync(owner.OrganizationId, AuditActions.PasswordReset, owner.PersonId, third.PersonId);
        await host.AddEventAsync(owner.OrganizationId, AuditActions.AccessTokenCreated, third.PersonId);

        // Act
        var page = await host.Audit.ListAsync(OwnerActor(owner), new AuditQuery(PersonId: member.PersonId), NoCancel);

        // Assert
        Assert.That(page.Value.Items.Select(e => e.EventId), Is.EquivalentTo(new[] { byMember, toMember }));
    }

    [TestCase(0, null, "limit", TestName = "ListAsync_LimitZero_IsInvalid")]
    [TestCase(-1, null, "limit", TestName = "ListAsync_NegativeLimit_IsInvalid")]
    [TestCase(101, null, "limit", TestName = "ListAsync_LimitOverTheMaximum_IsInvalid")]
    [TestCase(10, "not a cursor!", "cursor", TestName = "ListAsync_CursorWithBadCharacters_IsInvalid")]
    [TestCase(10, "", "cursor", TestName = "ListAsync_EmptyCursor_IsInvalid")]
    [TestCase(10, "bm9wZQ", "cursor", TestName = "ListAsync_CursorWithTheWrongShape_IsInvalid")]
    public async Task ListAsync_BadParameters_AreInvalidWithThePath(int limit, string? cursor, string path)
    {
        // Arrange
        await using var host = await StartHostAsync();
        var owner = await host.StartWithOwnerAsync();

        // Act
        var page = await host.Audit.ListAsync(OwnerActor(owner), new AuditQuery(cursor, limit), NoCancel);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Failure?.Kind, Is.EqualTo(FailureKind.Invalid));
            Assert.That(page.Failure?.Code, Is.EqualTo(IdentityErrorCodes.AuditQueryInvalid));
            Assert.That(page.Failure?.Details.Select(d => d.Path), Is.EqualTo(new[] { path }));
        }
    }

    [Test]
    public async Task ListAsync_MaximumLimit_IsAccepted()
    {
        // Arrange
        await using var host = await StartHostAsync();
        var owner = await host.StartWithOwnerAsync();

        // Act
        var page = await host.Audit.ListAsync(OwnerActor(owner), new AuditQuery(Limit: AuditRules.MaxLimit), NoCancel);

        // Assert
        Assert.That(page.Succeeded, Is.True);
    }

    [Test]
    public async Task AddAsync_NulAndControlCharactersInDetails_AreReplacedInsteadOfFailing()
    {
        // Arrange：jsonb 不收 NUL；細項的值也可能來自使用者輸入
        await using var host = await StartHostAsync();
        var owner = await host.StartWithOwnerAsync();
        host.Request.Set("203.0.113.5", "agent\0/1.0\r\nInjected");

        // Act
        await host.AddEventAsync(owner.OrganizationId, AuditActions.AccessTokenCreated, details: new Dictionary<string, string> { ["tokenName"] = "a\0b​c" });
        var page = await host.Audit.ListAsync(OwnerActor(owner), new AuditQuery(), NoCancel);

        // Assert
        Assert.That(page.Value.Items.Single().Details["tokenName"], Is.EqualTo("a b c"));
    }
}
