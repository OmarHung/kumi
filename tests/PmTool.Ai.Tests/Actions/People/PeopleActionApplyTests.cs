using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai.Actions;
using PmTool.Ai.Actions.People;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Identity;

namespace PmTool.Ai.Tests.Actions.People;

/// <summary>人員管理建議套用：一步一步呼叫身分模組、初始密碼只在這一刻從卡片的輸入讀、不進任何儲存的東西。</summary>
[TestFixture]
public sealed class PeopleActionApplyTests
{
    private const string Password = "S3cret-pass-phrase!";

    private AssistantHarness _h = null!;
    private RecordingPeople _people = null!;
    private Actor _owner = null!;
    private Guid _ownerId;
    private IsolatedActions? _actions;

    [SetUp]
    public async Task StartAsync()
    {
        _actions = null;
        _people = new RecordingPeople();
        _h = await AssistantHarness.StartAsync(configure: services =>
        {
            services.AddSingleton<IPeopleDirectory>(_people);
            services.AddSingleton<IPersonProfileService>(_people);
        });
        _ownerId = _people.Add("擁有者", OrganizationRole.Owner);
        _owner = new Actor(_h.OrganizationId, _ownerId, IsOrganizationAdmin: true);
    }

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private IsolatedActions Actions => _actions ??= new IsolatedActions(_h, new PeopleActionHandler(_people, _h.Host.Services));

    private Task<Result<ActionProposal>> CreateAsync(string changesJson) =>
        Actions.Creator.CreateAsync(
            _owner, new ActionProposalDraft(ActionKinds.People, null, Json($$"""{"changes":{{changesJson}}}""")), CancellationToken.None);

    private Task<Result<ActionProposal>> ApplyAsync(Guid id, params (string Key, string Value)[] inputs) =>
        Actions.Service.ApplyAsync(
            _owner, id, inputs.ToDictionary(i => i.Key, i => i.Value), CancellationToken.None);

    [Test]
    public async Task Apply_CreatePerson_PassesTheCardPasswordToTheDirectoryAndNeverStoresIt()
    {
        // Arrange
        var proposal = (await CreateAsync("""[{"action":"create","displayName":"王小明","email":"ming@example.com","role":"member"}]""")).Value;

        // Act
        var applied = await ApplyAsync(proposal.Id, ("password:0", Password));

        // Assert
        await using var db = await _h.Host.CreateDbContextAsync();
        var row = await db.ActionProposals.AsNoTracking().SingleAsync(p => p.Id == proposal.Id);
        var everythingStored = string.Join('\n', row.State, row.Steps, row.Inputs, row.Error, row.Title, row.Reason);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Succeeded, Is.True, () => applied.Failure?.Message ?? string.Empty);
            Assert.That(applied.Value.Status, Is.EqualTo(NewItemProposalStatus.Applied));
            Assert.That(_people.Created.Single(), Is.EqualTo(new NewPerson("王小明", "ming@example.com", Password, OrganizationRole.Member)));
            Assert.That(everythingStored, Does.Not.Contain(Password));
            Assert.That(JsonSerializer.Serialize(applied.Value), Does.Not.Contain(Password));
        }
    }

    [Test]
    public async Task Apply_CreatePersonWithoutThePasswordInput_IsRejectedAndNothingIsCreated()
    {
        // Arrange
        var proposal = (await CreateAsync("""[{"action":"create","displayName":"王小明","email":"ming@example.com","role":"member"}]""")).Value;

        // Act
        var missing = await ApplyAsync(proposal.Id);
        var stillPending = await Actions.Service.GetAsync(_owner, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(missing.Failure!.Details.Single().Path, Is.EqualTo("inputs.password:0"));
            Assert.That(_people.Created, Is.Empty);
            Assert.That(stillPending.Value.Status, Is.EqualTo(NewItemProposalStatus.Pending));
        }
    }

    [Test]
    public async Task ApplyStep_CreatePersonWithShortPassword_FailsWithoutCallingTheDirectoryOrEchoingThePassword()
    {
        // Arrange
        var handler = new PeopleActionHandler(_people, _h.Host.Services);
        var prepared = (await handler.PrepareAsync(
            new ActionContext(_owner, null, null),
            Json("""{"changes":[{"action":"create","displayName":"王小明","email":"ming@example.com","role":"member"}]}"""),
            CancellationToken.None)).Value;

        // Act
        var result = await handler.ApplyStepAsync(
            new ActionContext(_owner, null, null), prepared.State, 0, new Dictionary<string, string> { ["password:0"] = "short" }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
            Assert.That(result.Failure.Message, Does.Not.Contain("short"));
            Assert.That(_people.Created, Is.Empty);
        }
    }

    [Test]
    public async Task Apply_MixedSteps_RunsEachThroughTheMatchingServiceInOrder()
    {
        // Arrange
        var colleague = _people.Add("同事", OrganizationRole.Member, jobTitle: "前端工程師", responsibilities: "官網");
        var other = _people.Add("另一位", OrganizationRole.Member);
        var proposal = (await CreateAsync($$"""
            [{"action":"set_role","person":"{{colleague}}","role":"guest"},
             {"action":"set_profile","person":"{{colleague}}","jobTitle":"後端工程師"},
             {"action":"disable","person":"{{other}}"}]
            """)).Value;

        // Act
        var applied = await ApplyAsync(proposal.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Value.Steps.All(s => s.Done), Is.True);
            Assert.That(_people.RoleCalls, Is.EqualTo(new[] { (colleague, OrganizationRole.Guest) }));
            Assert.That(_people.DisabledCalls, Is.EqualTo(new[] { (other, true) }));
            Assert.That(_people.ProfileCalls.Single().Profile, Is.EqualTo(new PersonProfile("後端工程師", "官網")), "沒要改的負責範圍維持套用當下的值");
        }
    }

    [Test]
    public async Task Apply_FirstStepRejectedByTheDirectory_ReturnsTheFailureAndTheProposalStaysPending()
    {
        // Arrange
        var first = _people.Add("甲");
        var proposal = (await CreateAsync($$"""[{"action":"disable","person":"{{first}}"}]""")).Value;
        _people.RejectWith = new Failure(FailureKind.Forbidden, IdentityErrorCodes.Forbidden, "不行");

        // Act
        var failed = await ApplyAsync(proposal.Id);
        var again = await Actions.Service.GetAsync(_owner, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.Failure!.Code, Is.EqualTo(IdentityErrorCodes.Forbidden));
            Assert.That(again.Value.Status, Is.EqualTo(NewItemProposalStatus.Pending));
        }
    }

    [Test]
    public async Task ApplyStep_SecondStepRejected_ReturnsItsFailureWhileTheFirstStepWasDone()
    {
        // Arrange
        var first = _people.Add("甲");
        var second = _people.Add("乙");
        var handler = new PeopleActionHandler(_people, _h.Host.Services);
        var prepared = (await handler.PrepareAsync(
            new ActionContext(_owner, null, null),
            Json($$"""{"changes":[{"action":"disable","person":"{{first}}"},{"action":"set_role","person":"{{second}}","role":"guest"}]}"""),
            CancellationToken.None)).Value;
        var context = new ActionContext(_owner, null, null);

        // Act
        var one = await handler.ApplyStepAsync(context, prepared.State, 0, new Dictionary<string, string>(), CancellationToken.None);
        _people.RejectWith = new Failure(FailureKind.Forbidden, IdentityErrorCodes.Forbidden, "只有擁有者能改。");
        var two = await handler.ApplyStepAsync(context, prepared.State, 1, new Dictionary<string, string>(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(one.Failure, Is.Null);
            Assert.That(_people.DisabledCalls, Has.Count.EqualTo(1));
            Assert.That(two.Failure!.Message, Is.EqualTo("只有擁有者能改。"));
        }
    }

    [Test]
    public async Task Apply_ThirdPersonPasswordTooShort_RejectsAtTheFirstStepSoNobodyIsCreatedAndTheProposalStaysPending()
    {
        // Arrange
        var proposal = (await CreateAsync("""
            [{"action":"create","displayName":"甲","email":"a@example.com","role":"member"},
             {"action":"create","displayName":"乙","email":"b@example.com","role":"member"},
             {"action":"create","displayName":"丙","email":"c@example.com","role":"member"}]
            """)).Value;

        // Act
        var failed = await ApplyAsync(proposal.Id, ("password:0", Password), ("password:1", Password), ("password:2", "short-pw"));
        var again = await Actions.Service.GetAsync(_owner, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.Failure!.Details.Select(d => d.Path), Is.EqualTo(new[] { "inputs.password:2" }));
            Assert.That(JsonSerializer.Serialize(failed.Failure), Does.Not.Contain("short-pw"));
            Assert.That(_people.Created, Is.Empty);
            Assert.That(again.Value.Status, Is.EqualTo(NewItemProposalStatus.Pending));
        }
    }

    [Test]
    public async Task ApplyStep_FirstStepChecksEveryCreatePasswordAndListsAllTheBadOnes()
    {
        // Arrange
        var handler = new PeopleActionHandler(_people, _h.Host.Services);
        var context = new ActionContext(_owner, null, null);
        var prepared = (await handler.PrepareAsync(
            context,
            Json("""{"changes":[{"action":"create","displayName":"甲","email":"a@example.com","role":"member"},{"action":"create","displayName":"乙","email":"b@example.com","role":"member"}]}"""),
            CancellationToken.None)).Value;

        // Act
        var result = await handler.ApplyStepAsync(
            context, prepared.State, 0, new Dictionary<string, string> { ["password:0"] = "x", ["password:1"] = new string('a', IdentityRules.MaxPasswordLength + 1) }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Details.Select(d => d.Path), Is.EqualTo(new[] { "inputs.password:0", "inputs.password:1" }));
            Assert.That(_people.Created, Is.Empty);
        }
    }

    [Test]
    public async Task Apply_CreateWithProfile_SetsTheProfileOfTheNewPersonInTheSameStep()
    {
        // Arrange
        var proposal = (await CreateAsync("""[{"action":"create","displayName":"測試小明","email":"ming@example.com","role":"member","jobTitle":"後端工程師"}]""")).Value;

        // Act
        var applied = await ApplyAsync(proposal.Id, ("password:0", Password));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Value.Steps.Single().Done, Is.True);
            Assert.That(_people.ProfileCalls.Single().Profile, Is.EqualTo(new PersonProfile("後端工程師", null)));
            var created = (await _people.ListAsync(_h.OrganizationId, CancellationToken.None)).Single(p => p.DisplayName == "測試小明");
            Assert.That(_people.ProfileCalls.Single().Person, Is.EqualTo(created.PersonId));
        }
    }

    [Test]
    public async Task Apply_CreateWithoutProfile_DoesNotCallTheProfileService()
    {
        // Arrange
        var proposal = (await CreateAsync("""[{"action":"create","displayName":"甲","email":"a@example.com","role":"member"}]""")).Value;

        // Act
        await ApplyAsync(proposal.Id, ("password:0", Password));

        // Assert
        Assert.That(_people.ProfileCalls, Is.Empty);
    }

    [Test]
    public async Task Apply_CreateThenProfileFailsAsALaterStep_SaysTheAccountWasCreated()
    {
        // Arrange
        var handler = new PeopleActionHandler(_people, _h.Host.Services);
        var context = new ActionContext(_owner, null, null);
        var prepared = (await handler.PrepareAsync(
            context,
            Json("""{"changes":[{"action":"disable","person":"%PERSON%"},{"action":"create","displayName":"甲","email":"a@example.com","role":"member","jobTitle":"工程師"}]}""".Replace("%PERSON%", _people.Add("同事").ToString())),
            CancellationToken.None)).Value;
        var inputs = new Dictionary<string, string> { ["password:1"] = Password };
        await handler.ApplyStepAsync(context, prepared.State, 0, inputs, CancellationToken.None);
        _people.RejectProfileWith = new Failure(FailureKind.Invalid, IdentityErrorCodes.ProfileInvalid, "職稱不合規定。");

        // Act
        var result = await handler.ApplyStepAsync(context, prepared.State, 1, inputs, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Message, Is.EqualTo("帳號已建立，職稱沒有設定成功。"));
            Assert.That(_people.Created, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Apply_CreateAsTheFirstStepWhoseProfileFails_IsAppliedWithTheStepErrorSoTheAccountIsNeverCreatedTwice()
    {
        // Arrange
        var proposal = (await CreateAsync("""[{"action":"create","displayName":"甲","email":"a@example.com","role":"member","jobTitle":"工程師"}]""")).Value;
        _people.RejectProfileWith = new Failure(FailureKind.Invalid, IdentityErrorCodes.ProfileInvalid, "職稱不合規定。");

        // Act：帳號已經建好、職稱沒設成功（寫進去了一部分）：即使是第一步也不能退回待決定，那一步記原因。
        var applied = await ApplyAsync(proposal.Id, ("password:0", Password));
        var again = await Actions.Service.GetAsync(_owner, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Succeeded, Is.True, () => applied.Failure?.Message ?? string.Empty);
            Assert.That(_people.Created, Has.Count.EqualTo(1));
            Assert.That(again.Value.Status, Is.EqualTo(NewItemProposalStatus.Applied));
            Assert.That(again.Value.Steps.Single().Error, Is.EqualTo("帳號已建立，職稱沒有設定成功。"));
        }
    }
}
