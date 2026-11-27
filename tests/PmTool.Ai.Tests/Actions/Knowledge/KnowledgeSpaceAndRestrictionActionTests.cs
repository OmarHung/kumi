using PmTool.Abstractions;
using PmTool.Ai.Actions;
using PmTool.Identity;
using PmTool.Knowledge;

namespace PmTool.Ai.Tests.Actions.Knowledge;

/// <summary>知識庫的空間（新增、設定、封存）、頁面限制，以及權限與參數形狀的檢查。</summary>
[TestFixture]
internal sealed class KnowledgeSpaceAndRestrictionActionTests : KnowledgeActionTestBase
{
    private static readonly Guid Alice = Guid.Parse("0190a000-0000-7000-8000-0000000000a1");
    private static readonly Guid Bob = Guid.Parse("0190a000-0000-7000-8000-0000000000a2");

    [Test]
    public async Task CreateSpace_AsAnOrganizationAdmin_ShowsTheStepAndCreatesIt()
    {
        // Act
        var prepared = await PrepareOkAsync(Payload(new { action = "create_space", key = "ops", name = "維運", description = "維運手冊" }), Admin);
        var result = await ApplyAsync(prepared, 0, Admin);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prepared.Steps.Single().Target, Is.EqualTo("OPS（維運）"));
            Assert.That(prepared.Steps.Single().After, Is.EqualTo("維運 · 維運手冊 · 組織空間"));
            Assert.That(Spaces.Created.Single(), Is.EqualTo(new NewSpace("OPS", "維運", "維運手冊", null)));
            Assert.That(result.Link, Is.EqualTo("/knowledge/OPS"));
        }
    }

    [Test]
    public async Task CreateSpace_OrganizationSpaceAsAMember_IsForbiddenAndNamesWhoCan()
    {
        // Act
        var result = await PrepareAsync(Payload(new { action = "create_space", key = "OPS", name = "維運" }));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(result.Failure.Code, Is.EqualTo(AiErrorCodes.Forbidden));
            Assert.That(result.Failure.Details.Single().Message, Does.Contain("組織管理員"));
        }
    }

    [Test]
    public async Task CreateSpace_KeyAlreadyUsed_IsRejected_AndPagesCanLiveInAnEarlierNewSpace()
    {
        // Act
        var taken = await PrepareAsync(Payload(new { action = "create_space", key = "hr", name = "重複" }), Admin);
        var chained = await PrepareOkAsync(
            Payload(
                new { action = "create_space", key = "OPS", name = "維運" },
                new { action = "create_page", spaceKey = "ops", title = "首頁" }),
            Admin);
        await ApplyAsync(chained, 1, Admin);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(taken.Failure!.Details.Single().Path, Is.EqualTo("changes[0].key"));
            Assert.That(chained.Steps.Select(s => s.Label), Is.EqualTo(new[] { "新增空間", "新增頁面" }));
            Assert.That(Pages.Created.Single(), Is.EqualTo(new NewPage(null, "首頁", null)));
        }
    }

    [Test]
    public async Task UpdateSpace_WithoutADescription_KeepsTheCurrentOne()
    {
        // Arrange
        Spaces.Spaces["HR"] = KnowledgeData.Space(canManage: true);

        // Act
        var prepared = await PrepareOkAsync(Payload(new { action = "update_space", spaceKey = "HR", name = "人力資源" }));
        await ApplyAsync(prepared, 0);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prepared.Steps.Single().Before, Is.EqualTo("人資 · 人資文件 · 組織空間"));
            Assert.That(Spaces.Updated.Single(), Is.EqualTo(("HR", new SpaceSettings("人力資源", "人資文件"))));
        }
    }

    [Test]
    public async Task ArchiveAndUnarchive_FollowTheCurrentState()
    {
        // Arrange
        Spaces.Spaces["HR"] = KnowledgeData.Space(canManage: true);
        Spaces.Spaces["OLD"] = KnowledgeData.Space("OLD", canManage: true, archived: true);

        // Act
        var archive = await PrepareOkAsync(Payload(new { action = "archive_space", spaceKey = "HR" }));
        await ApplyAsync(archive, 0);
        var unarchive = await PrepareOkAsync(Payload(new { action = "unarchive_space", spaceKey = "OLD" }));
        await ApplyAsync(unarchive, 0);
        var already = await PrepareAsync(Payload(new { action = "archive_space", spaceKey = "OLD" }));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Spaces.Archived, Is.EqualTo(new[] { ("HR", true), ("OLD", false) }));
            Assert.That(already.Failure!.Details.Single().Message, Does.Contain("已經是封存"));
        }
    }

    [Test]
    public async Task ManageActionsWithoutManagePermission_AreForbidden()
    {
        // Act（假資料裡 HR 的 canManage 預設是 false）
        var result = await PrepareAsync(Payload(new { action = "archive_space", spaceKey = "HR" }));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(result.Failure.Details.Single().Code, Is.EqualTo(AiErrorCodes.Forbidden));
        }
    }

    [Test]
    public async Task PageWritesWithoutEditPermission_AreForbidden_AndInAnArchivedSpaceAreInvalid()
    {
        // Arrange
        Spaces.Spaces["VIEW"] = KnowledgeData.Space("VIEW", canEdit: false);
        Spaces.Spaces["OLD"] = KnowledgeData.Space("OLD", archived: true);

        // Act
        var viewOnly = await PrepareAsync(Payload(new { action = "create_page", spaceKey = "VIEW", title = "新頁" }));
        var archived = await PrepareAsync(Payload(new { action = "create_page", spaceKey = "OLD", title = "新頁" }));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewOnly.Failure!.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(archived.Failure!.Kind, Is.EqualTo(FailureKind.Invalid));
            Assert.That(archived.Failure.Details.Single().Message, Does.Contain("封存"));
        }
    }

    [Test]
    public async Task SetRestriction_ToNamedPeople_ShowsNamesAndSendsTheList()
    {
        // Arrange
        People.Set(Member.OrganizationId, Alice, OrganizationRole.Member, name: "小美");
        People.Set(Member.OrganizationId, Bob, OrganizationRole.Member, name: "阿強");

        // Act
        var prepared = await PrepareOkAsync(Payload(new { action = "set_restriction", spaceKey = "HR", number = 2, people = new[] { Alice, Bob } }));
        await ApplyAsync(prepared, 0);

        // Assert
        var step = prepared.Steps.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(step.After, Is.EqualTo("只有這些人看得到：小美、阿強"));
            Assert.That(step.Note, Does.Contain("自動加進名單"));
            Assert.That(Pages.Restrictions.Single().People, Is.EqualTo(new[] { Alice, Bob }));
        }
    }

    [Test]
    public async Task SetRestriction_EmptyList_RemovesTheRestrictionAsNull()
    {
        // Arrange
        People.Set(Member.OrganizationId, Alice, OrganizationRole.Member, name: "小美");
        Pages.OnRestriction = () => new PageRestriction([Alice], []) { CanRelax = true, RestrictedBy = Alice };

        // Act
        var prepared = await PrepareOkAsync(Payload(new { action = "set_restriction", spaceKey = "HR", number = 2, people = Array.Empty<Guid>() }));
        await ApplyAsync(prepared, 0);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prepared.Steps.Single().Before, Is.EqualTo("只有這些人看得到：小美"));
            Assert.That(Pages.Restrictions.Single().People, Is.Null);
        }
    }

    [Test]
    public async Task SetRestriction_RelaxingSomeoneElsesRestriction_IsForbiddenButTighteningIsAllowed()
    {
        // Arrange
        People.Set(Member.OrganizationId, Alice, OrganizationRole.Member, name: "小美");
        People.Set(Member.OrganizationId, Bob, OrganizationRole.Member, name: "阿強");
        Pages.OnRestriction = () => new PageRestriction([Alice, Bob], []) { CanRelax = false, RestrictedBy = Alice };

        // Act
        var relax = await PrepareAsync(Payload(new { action = "set_restriction", spaceKey = "HR", number = 2, people = Array.Empty<Guid>() }));
        var tighten = await PrepareAsync(Payload(new { action = "set_restriction", spaceKey = "HR", number = 2, people = new[] { Alice } }));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(relax.Failure!.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(relax.Failure.Details.Single().Message, Does.Contain("小美"));
            Assert.That(tighten.Failure, Is.Null);
        }
    }

    [Test]
    public async Task SetRestriction_UnknownPerson_IsRejectedWithTheIndex()
    {
        // Act
        var result = await PrepareAsync(Payload(new { action = "set_restriction", spaceKey = "HR", number = 2, people = new[] { Alice } }));

        // Assert
        Assert.That(IssuePaths(result), Is.EqualTo(new[] { "changes[0].people[0]" }));
    }

    [Test]
    public async Task Payload_WithMistakes_ReportsEveryProblemAndSavesNothing()
    {
        // Arrange
        var payload = Payload(
            new { action = "create_page", spaceKey = "HR" },
            new { action = "delete_page", spaceKey = "HR", number = 2, title = "多的" },
            new { action = "fly" },
            new { action = "create_page", spaceKey = "HR", title = "含\0控制字元" },
            new { action = "update_page", spaceKey = "HR", number = 2, expectedRevision = 3 });

        // Act
        var result = await PrepareAsync(payload);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
            Assert.That(IssuePaths(result), Is.EqualTo(new[] { "changes[0].title", "changes[1].title", "changes[2].action", "changes[3].title", "changes[4]" }));
            Assert.That(Pages.Created, Is.Empty);
        }
    }

    [Test]
    public async Task Payload_EmptyUnknownOrTooLong_IsRejected()
    {
        // Act
        var empty = await PrepareAsync(Payload());
        var unknown = await PrepareAsync(System.Text.Json.JsonSerializer.SerializeToElement(new { changes = Array.Empty<object>(), extra = 1 }));
        var tooMany = await PrepareAsync(Payload([.. Enumerable.Range(0, AiRules.MaxActionSteps + 1).Select(i => (object)new { action = "delete_page", spaceKey = "HR", number = 2 })]));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(empty.Failure!.Details.Single().Path, Is.EqualTo("changes"));
            Assert.That(unknown.Failure!.Details.Select(d => d.Path), Does.Contain("extra"));
            Assert.That(tooMany.Failure!.Details.Single().Message, Does.Contain($"{AiRules.MaxActionSteps}"));
        }
    }

    [Test]
    public async Task Apply_WhenTheServiceRefuses_ReturnsThatFailureForThatStep()
    {
        // Arrange
        var prepared = await PrepareOkAsync(Payload(new { action = "delete_page", spaceKey = "HR", number = 2 }));
        var refused = new Failure(FailureKind.Conflict, KnowledgeErrorCodes.PageHasHiddenDescendants, "底下有看不到的頁面");
        Pages.OnDelete = _ => refused;

        // Act
        var result = await ApplyAsync(prepared, 0);

        // Assert
        Assert.That(result.Failure, Is.EqualTo(refused));
    }
}
