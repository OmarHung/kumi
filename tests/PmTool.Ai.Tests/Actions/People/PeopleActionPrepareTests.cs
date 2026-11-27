using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai.Actions;
using PmTool.Ai.Actions.People;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Identity;

namespace PmTool.Ai.Tests.Actions.People;

/// <summary>人員管理建議建立時的檢查：誰能提、誰能動誰、每個欄位的問題一次回完、卡片上的文字與輸入。</summary>
[TestFixture]
public sealed class PeopleActionPrepareTests
{
    private AssistantHarness _h = null!;
    private RecordingPeople _people = null!;
    private Actor _owner = null!;
    private Guid _ownerId;
    private Guid _adminId;

    [SetUp]
    public async Task StartAsync()
    {
        _people = new RecordingPeople();
        _h = await AssistantHarness.StartAsync(configure: services =>
        {
            services.AddSingleton<IPeopleDirectory>(_people);
            services.AddSingleton<IPersonProfileService>(_people);
        });
        _ownerId = _people.Add("擁有者", OrganizationRole.Owner);
        _adminId = _people.Add("管理員", OrganizationRole.Admin);
        _owner = new Actor(_h.OrganizationId, _ownerId, IsOrganizationAdmin: true);
    }

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    private PeopleActionHandler Handler => new(_people, _h.Host.Services);

    private Actor AdminActor => new(_h.OrganizationId, _adminId, IsOrganizationAdmin: true);

    private Task<Result<PreparedAction>> PrepareAsync(Actor actor, string changesJson) =>
        Handler.PrepareAsync(new ActionContext(actor, null, null), Json($$"""{"changes":{{changesJson}}}"""), CancellationToken.None);

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    [Test]
    public async Task Prepare_ByNonAdmin_IsForbiddenWithTheWhoCanMessage()
    {
        // Arrange
        var member = _people.Add("一般成員");

        // Act
        var result = await PrepareAsync(new Actor(_h.OrganizationId, member, IsOrganizationAdmin: false), $$"""[{"action":"disable","person":"{{_adminId}}"}]""");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(result.Failure.Message, Is.EqualTo("人員管理只有組織管理員能做。"));
        }
    }

    [Test]
    public async Task Prepare_ByActorWhoIsNotListedAsAdminAnymore_IsForbidden()
    {
        // Arrange：旗標說是管理員，但身分模組現在查到他只是一般成員（剛被降級）。
        var demoted = _people.Add("剛被降級", OrganizationRole.Member);

        // Act
        var result = await PrepareAsync(new Actor(_h.OrganizationId, demoted, IsOrganizationAdmin: true), $$"""[{"action":"disable","person":"{{_adminId}}"}]""");

        // Assert
        Assert.That(result.Failure!.Kind, Is.EqualTo(FailureKind.Forbidden));
    }

    [Test]
    public async Task Prepare_CreateMember_DeclaresPasswordInputAndKeepsPasswordOutOfState()
    {
        // Act
        var result = await PrepareAsync(_owner, """[{"action":"create","displayName":"王小明","email":"Ming@Example.com","role":"member"}]""");

        // Assert
        var prepared = result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prepared.Steps, Has.Count.EqualTo(1));
            Assert.That(prepared.Steps[0].Label, Is.EqualTo("新增人員"));
            Assert.That(prepared.Steps[0].Target, Is.EqualTo("王小明"));
            Assert.That(prepared.Steps[0].After, Does.Contain("ming@example.com").And.Contain("一般成員"));
            Assert.That(prepared.Inputs, Has.Count.EqualTo(1));
            Assert.That(prepared.Inputs[0].Key, Is.EqualTo("password:0"));
            Assert.That(prepared.Inputs[0].Kind, Is.EqualTo(ActionInputKind.Password));
            Assert.That(prepared.Inputs[0].Label, Is.EqualTo("王小明的初始密碼"));
            Assert.That(prepared.Inputs[0].Hint, Does.Contain($"{IdentityRules.MinPasswordLength}"));
            Assert.That(prepared.State.GetRawText().ToLowerInvariant(), Does.Not.Contain("password"));
        }
    }

    [Test]
    public async Task Prepare_CreateWithPasswordProperty_IsRejectedAndTheMessageSaysTheCardAsksForIt()
    {
        // Act
        var result = await PrepareAsync(_owner, """[{"action":"create","displayName":"王小明","email":"ming@example.com","role":"member","password":"hunter2hunter2"}]""");

        // Assert
        var detail = result.Failure!.Details.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure.Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
            Assert.That(detail.Path, Is.EqualTo("changes[0].password"));
            Assert.That(detail.Message, Does.Contain("卡片上會請管理員自己輸入"));
            Assert.That(result.Failure.Message + detail.Message, Does.Not.Contain("hunter2hunter2"));
        }
    }

    [Test]
    public async Task Prepare_CreateWithManyProblems_ReportsEveryOneWithItsPath()
    {
        // Arrange
        _people.Add("已經有的人", email: "taken@example.com");

        // Act
        var result = await PrepareAsync(_owner, """
            [{"action":"create","displayName":"甲​","email":"not-an-email","role":"member"},
             {"action":"create","displayName":"乙","email":"Taken@Example.com","role":"boss"},
             {"action":"create","displayName":"丙","email":"c@example.com","role":"member"},
             {"action":"create","displayName":"丁","email":"C@example.com","role":"guest"}]
            """);

        // Assert
        var paths = result.Failure!.Details.Select(d => d.Path).ToList();
        Assert.That(paths, Is.EquivalentTo(new[] { "changes[0].displayName", "changes[0].email", "changes[1].email", "changes[1].role", "changes[3].email" }));
    }

    [Test]
    public async Task Prepare_AdminCreatingAdmin_IsForbiddenButCreatingMemberIsFine()
    {
        // Act
        var admin = await PrepareAsync(AdminActor, """[{"action":"create","displayName":"新管理員","email":"a@example.com","role":"admin"}]""");
        var member = await PrepareAsync(AdminActor, """[{"action":"create","displayName":"新成員","email":"m@example.com","role":"member"}]""");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(admin.Failure!.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(admin.Failure.Message, Does.Contain("只有組織的擁有者"));
            Assert.That(member.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task Prepare_SetRole_ShowsBeforeAndAfterAndRejectsSelfAndNoChange()
    {
        // Arrange
        var colleague = _people.Add("同事", OrganizationRole.Member);

        // Act
        var ok = await PrepareAsync(_owner, $$"""[{"action":"set_role","person":"{{colleague}}","role":"guest"}]""");
        var self = await PrepareAsync(_owner, $$"""[{"action":"set_role","person":"{{_ownerId}}","role":"admin"}]""");
        var same = await PrepareAsync(_owner, $$"""[{"action":"set_role","person":"{{colleague}}","role":"member"}]""");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ok.Value.Steps[0].Before, Is.EqualTo("一般成員"));
            Assert.That(ok.Value.Steps[0].After, Is.EqualTo("訪客"));
            Assert.That(ok.Value.Inputs, Is.Empty);
            Assert.That(self.Failure!.Details.Single().Message, Does.Contain("不能改自己的角色"));
            Assert.That(same.Failure!.Details.Single().Message, Does.Contain("現在就是一般成員"));
        }
    }

    [Test]
    public async Task Prepare_AdminTouchingAdminOrPromotingToAdmin_IsForbidden()
    {
        // Arrange
        var colleague = _people.Add("同事", OrganizationRole.Member);
        var otherAdmin = _people.Add("另一位管理員", OrganizationRole.Admin);

        // Act
        var demote = await PrepareAsync(AdminActor, $$"""[{"action":"set_role","person":"{{otherAdmin}}","role":"member"}]""");
        var promote = await PrepareAsync(AdminActor, $$"""[{"action":"set_role","person":"{{colleague}}","role":"admin"}]""");
        var disable = await PrepareAsync(AdminActor, $$"""[{"action":"disable","person":"{{otherAdmin}}"}]""");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(demote.Failure!.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(promote.Failure!.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(disable.Failure!.Kind, Is.EqualTo(FailureKind.Forbidden));
        }
    }

    [Test]
    public async Task Prepare_SetProfile_ShowsOnlyTheGivenFieldsAndTreatsEmptyStringAsClearing()
    {
        // Arrange
        var colleague = _people.Add("同事", jobTitle: "前端工程師", responsibilities: "官網");

        // Act
        var change = await PrepareAsync(_owner, $$"""[{"action":"set_profile","person":"{{colleague}}","jobTitle":"後端工程師"}]""");
        var clear = await PrepareAsync(_owner, $$"""[{"action":"set_profile","person":"{{colleague}}","responsibilities":""}]""");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Value.Steps[0].Before, Is.EqualTo("職稱：前端工程師"));
            Assert.That(change.Value.Steps[0].After, Is.EqualTo("職稱：後端工程師"));
            Assert.That(clear.Value.Steps[0].Before, Is.EqualTo("負責範圍：官網"));
            Assert.That(clear.Value.Steps[0].After, Is.EqualTo("負責範圍：（沒有）"));
        }
    }

    [Test]
    public async Task Prepare_SetProfileWithNothingToChangeOrTooLong_IsInvalid()
    {
        // Arrange
        var colleague = _people.Add("同事", jobTitle: "工程師");

        // Act
        var nothing = await PrepareAsync(_owner, $$"""[{"action":"set_profile","person":"{{colleague}}"}]""");
        var same = await PrepareAsync(_owner, $$"""[{"action":"set_profile","person":"{{colleague}}","jobTitle":"工程師"}]""");
        var longTitle = await PrepareAsync(_owner, $$"""[{"action":"set_profile","person":"{{colleague}}","jobTitle":"{{new string('字', 61)}}"}]""");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(nothing.Failure!.Details.Single().Path, Is.EqualTo("changes[0]"));
            Assert.That(same.Failure!.Details.Single().Message, Does.Contain("已經是這樣"));
            Assert.That(longTitle.Failure!.Details.Single().Path, Is.EqualTo("changes[0].jobTitle"));
        }
    }

    [Test]
    public async Task Prepare_DisableThenEnableTheSamePerson_SecondStepSeesTheFirstStepsResult()
    {
        // Arrange
        var colleague = _people.Add("同事");

        // Act
        var result = await PrepareAsync(_owner, $$"""[{"action":"disable","person":"{{colleague}}"},{"action":"enable","person":"{{colleague}}"}]""");
        var twice = await PrepareAsync(_owner, $$"""[{"action":"disable","person":"{{colleague}}"},{"action":"disable","person":"{{colleague}}"}]""");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Steps.Select(s => s.Label), Is.EqualTo(new[] { "停用帳號", "啟用帳號" }));
            Assert.That(twice.Failure!.Details.Single().Path, Is.EqualTo("changes[1].person"));
        }
    }

    [Test]
    public async Task Prepare_DisablingSelfOrAnUnknownPerson_IsInvalid()
    {
        // Act
        var self = await PrepareAsync(_owner, $$"""[{"action":"disable","person":"{{_ownerId}}"}]""");
        var unknown = await PrepareAsync(_owner, $$"""[{"action":"enable","person":"{{Guid.NewGuid()}}"}]""");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(self.Failure!.Details.Single().Message, Does.Contain("不能停用自己"));
            Assert.That(unknown.Failure!.Details.Single().Message, Does.Contain("list_people"));
        }
    }

    [Test]
    public async Task Prepare_BadShape_ReportsUnknownActionExtraPropertiesAndStepCount()
    {
        // Act
        var action = await PrepareAsync(_owner, """[{"action":"reset_password","person":"x"}]""");
        var extra = await PrepareAsync(_owner, $$"""[{"action":"disable","person":"{{_adminId}}","note":"x"}]""");
        var empty = await PrepareAsync(_owner, "[]");
        var tooMany = await PrepareAsync(_owner, "[" + string.Join(",", Enumerable.Repeat($$"""{"action":"enable","person":"{{_adminId}}"}""", AiRules.MaxActionSteps + 1)) + "]");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(action.Failure!.Details.Single().Path, Is.EqualTo("changes[0].action"));
            Assert.That(extra.Failure!.Details.Select(d => d.Path), Does.Contain("changes[0].note"));
            Assert.That(empty.Failure!.Details.Single().Path, Is.EqualTo("changes"));
            Assert.That(tooMany.Failure!.Details.Single().Path, Is.EqualTo("changes"));
        }
    }

    [Test]
    public async Task Prepare_CreateWithProfile_ShowsTitleAndResponsibilitiesOnTheSameStep()
    {
        // Act
        var result = await PrepareAsync(_owner, """[{"action":"create","displayName":"測試小明","email":"ming@example.com","role":"member","jobTitle":"後端工程師","responsibilities":"API 維運"}]""");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Steps, Has.Count.EqualTo(1));
            Assert.That(result.Value.Steps[0].After, Does.Contain("職稱：後端工程師").And.Contain("負責範圍：API 維運").And.Contain("一般成員"));
            Assert.That(result.Value.State.GetProperty("steps")[0].GetProperty("jobTitle").GetString(), Is.EqualTo("後端工程師"));
        }
    }

    [Test]
    public async Task Prepare_CreateWithTooLongOrInvisibleProfile_IsInvalidWithPaths()
    {
        // Act
        var result = await PrepareAsync(_owner, $$"""[{"action":"create","displayName":"甲","email":"a@example.com","role":"member","jobTitle":"{{new string('字', 61)}}","responsibilities":"x\u200by"}]""");

        // Assert
        Assert.That(result.Failure!.Details.Select(d => d.Path), Is.EquivalentTo(new[] { "changes[0].jobTitle", "changes[0].responsibilities" }));
    }
}
