using PmTool.Abstractions;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Identity;

namespace PmTool.Ai.Tests.HostAgents;

/// <summary>
/// 所有有效成員（擁有者、管理員、一般成員）都能選主機來源、送出訊息，和組織的 AI 開關、個人連線的開關無關（測試環境兩個都是關的）；
/// 訪客與停用的人看不到也送不出；新成員自動適用。
/// </summary>
[TestFixture]
public sealed class HostMemberAccessTests
{
    private HostHarness _h = null!;
    private HostSetup _setup = null!;

    [SetUp]
    public async Task SetUp()
    {
        _h = await HostHarness.StartAsync();
        _setup = await _h.SetUpAsync();
    }

    [TearDown]
    public async Task TearDown() => await _h.DisposeAsync();

    private async Task<AssistantMessage> RoundTripAsync(Actor actor)
    {
        var run = await _h.SendAsync(actor, _setup.Source);
        var job = await _h.ClaimAsync(_setup.Worker);
        await _h.ReportAsync(_setup.Worker, job, 1, new LocalAgentText("你好"));
        await _h.FinishAsync(_setup.Worker, job);
        return (await run.ReadAllAsync()).OfType<AssistantRunCompleted>().Single().Message;
    }

    public static IEnumerable<TestCaseData> ValidRoles() =>
    [
        new TestCaseData(OrganizationRole.Owner).SetArgDisplayNames("owner"),
        new TestCaseData(OrganizationRole.Admin).SetArgDisplayNames("admin"),
        new TestCaseData(OrganizationRole.Member).SetArgDisplayNames("member"),
    ];

    [TestCaseSource(nameof(ValidRoles))]
    public async Task Every_valid_member_sees_the_hosted_source_and_can_send(OrganizationRole role)
    {
        // Arrange
        var actor = role switch { OrganizationRole.Owner => _h.Owner, OrganizationRole.Admin => _h.Admin, _ => _h.Member };

        // Act
        var options = await _h.OptionsOfAsync(actor);
        var reply = await RoundTripAsync(actor);

        // Assert
        var hosted = options.Single(o => o.Hosted);
        Assert.That(hosted.Id, Is.EqualTo(_setup.Source.Id));
        Assert.That(hosted.Name, Is.EqualTo("主機上的 Claude Code"));
        Assert.That(hosted.Kind, Is.EqualTo(AiProviderKind.ClaudeCode));
        Assert.That(hosted.Personal, Is.False);
        Assert.That(hosted.Notice, Is.Null);
        Assert.That(hosted.Models.Select(m => m.Id), Is.EqualTo(new[] { "default", "sonnet" }));
        Assert.That(reply.Status, Is.EqualTo(AssistantMessageStatus.Complete));
        Assert.That(reply.ConnectionName, Is.EqualTo("主機上的 Claude Code"));
    }

    [Test]
    public async Task Options_are_available_even_though_org_ai_and_personal_connections_are_off()
    {
        // Arrange
        // Act
        var options = await Host_options();

        // Assert
        Assert.That(options.Available, Is.True);
        Assert.That(options.Connections.Single().Hosted, Is.True);
        Assert.That(options.Default, Is.Null);
    }

    private Task<AiOptions> Host_options() => _h.Host.Get<IAiOptionsService>().GetAsync(_h.Member, default);

    [Test]
    public async Task A_member_who_joins_later_sees_the_source_without_any_setting()
    {
        // Arrange
        var newcomer = _h.NewMember();

        // Act
        var options = await _h.OptionsOfAsync(newcomer);
        var reply = await RoundTripAsync(newcomer);

        // Assert
        Assert.That(options.Single().Hosted, Is.True);
        Assert.That(reply.Status, Is.EqualTo(AssistantMessageStatus.Complete));
    }

    [Test]
    public async Task Guests_and_disabled_people_and_strangers_see_nothing_and_cannot_send()
    {
        // Arrange
        var stranger = new Actor(_h.Assistant.OrganizationId, Guid.CreateVersion7(), false);

        // Act
        var seen = new List<int>();
        var sends = new List<string?>();
        foreach (var actor in new[] { _h.Guest, _h.Disabled, stranger, _h.OtherOwner })
        {
            seen.Add((await _h.OptionsOfAsync(actor)).Count);
            sends.Add((await _h.TrySendAsync(actor, _setup.Source)).Failure?.Code);
        }

        // Assert
        Assert.That(seen, Is.All.EqualTo(0));
        Assert.That(sends, Is.All.Not.Null);
        Assert.That(await _h.JobRowCountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task Guest_who_holds_a_conversation_still_cannot_use_a_source_id()
    {
        // Arrange
        var conversation = await _h.Assistant.Service.CreateAsync(_h.Admin, AssistantHarness.ProjectKey, default);
        _h.People.SetRole(_h.Assistant.OrganizationId, _h.Admin.PersonId, OrganizationRole.Guest);

        // Act
        var started = await _h.Assistant.Service.StartAsync(
            _h.Admin, AssistantHarness.ProjectKey, conversation.Value.Id,
            new AssistantMessageInput("嗨", new AiSelection(_setup.Source.Id, "default", null)), default);

        // Assert
        Assert.That(started.Failure?.Code, Is.EqualTo(AiErrorCodes.SelectionInvalid));
        Assert.That(await _h.JobRowCountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task Member_who_is_disabled_after_seeing_the_option_is_refused_on_send()
    {
        // Arrange
        var options = await _h.OptionsOfAsync(_h.Member);
        _h.People.SetDisabled(_h.Assistant.OrganizationId, _h.Member.PersonId, true);

        // Act
        var started = await _h.TrySendAsync(_h.Member, _setup.Source);

        // Assert
        Assert.That(options, Is.Not.Empty);
        Assert.That(started.Succeeded, Is.False);
    }

    [Test]
    public async Task Offline_agent_gives_members_a_plain_notice_without_commands_and_refuses_the_send()
    {
        // Arrange
        _h.Clock.Advance(AiRules.HostAgentOnlineWindow + TimeSpan.FromSeconds(1));

        // Act
        var option = (await _h.OptionsOfAsync(_h.Member)).Single();
        var started = await _h.TrySendAsync(_h.Member, _setup.Source);

        // Assert
        Assert.That(option.Notice, Is.Not.Null.And.Contain("離線"));
        Assert.That(option.Notice, Does.Not.Contain("kumi"));
        Assert.That(started.Failure?.Code, Is.EqualTo(AiErrorCodes.HostSourceUnavailable));
        Assert.That(started.Failure!.Message, Does.Not.Contain("kumi agent start"));
        Assert.That(await _h.JobRowCountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task Notice_names_each_reason_in_one_sentence()
    {
        // Arrange
        var notices = new List<string?>();

        // Act
        _setup.Worker.Reports = [HostHarness.Report(loginId: Guid.NewGuid())];
        await _h.CheckInAsync(_setup.Worker);
        notices.Add((await _h.OptionsOfAsync(_h.Member)).Single().Notice);
        _setup.Worker.Reports = [HostHarness.Report(billing: HostBilling.Subscription)];
        await _h.CheckInAsync(_setup.Worker);
        notices.Add((await _h.OptionsOfAsync(_h.Member)).Single().Notice);
        _setup.Worker.Reports = [HostHarness.Report(installed: false)];
        await _h.CheckInAsync(_setup.Worker);
        notices.Add((await _h.OptionsOfAsync(_h.Member)).Single().Notice);

        // Assert
        Assert.That(notices, Is.All.Not.Null);
        Assert.That(notices.Distinct().Count(), Is.EqualTo(3));
        Assert.That(notices, Has.None.Contain("kumi"));
    }

    [Test]
    public async Task Disabled_source_is_hidden_and_a_stale_selection_is_refused()
    {
        // Arrange
        await _h.Service.UpdateSourceAsync(
            _h.Owner, _setup.Source.Id, HostHarness.SourceInput(_setup.Worker.AgentId, enabled: false, acknowledge: false), default);

        // Act
        var options = await _h.OptionsOfAsync(_h.Member);
        var started = await _h.TrySendAsync(_h.Member, _setup.Source);

        // Assert
        Assert.That(options, Is.Empty);
        Assert.That(started.Succeeded, Is.False);
    }

    [Test]
    public async Task Only_models_the_owner_opened_and_the_engine_reported_can_be_chosen()
    {
        // Arrange
        await _h.Service.UpdateSourceAsync(
            _h.Owner,
            _setup.Source.Id,
            HostHarness.SourceInput(_setup.Worker.AgentId, models: ["sonnet", "default"]) with { DefaultModel = "sonnet", DefaultEffort = AiEffort.Low },
            default);

        // Act
        var option = (await _h.OptionsOfAsync(_h.Member)).Single();
        var closed = await _h.TrySendAsync(_h.Member, _setup.Source, model: "opus");
        var badEffort = await _h.Assistant.Service.StartAsync(
            _h.Member,
            AssistantHarness.ProjectKey,
            (await _h.Assistant.Service.CreateAsync(_h.Member, AssistantHarness.ProjectKey, default)).Value.Id,
            new AssistantMessageInput("嗨", new AiSelection(_setup.Source.Id, "default", AiEffort.High)),
            default);

        // Assert
        Assert.That(option.Models.Select(m => m.Id), Is.EqualTo(new[] { "sonnet", "default" }));
        Assert.That(option.Models[0].DefaultEffort, Is.EqualTo(AiEffort.Low));
        Assert.That(closed.Failure?.Code, Is.EqualTo(AiErrorCodes.SelectionInvalid));
        Assert.That(badEffort.Failure?.Code, Is.EqualTo(AiErrorCodes.SelectionInvalid));
    }

    [Test]
    public async Task Job_content_does_not_mention_the_users_computer_or_the_local_agent_command()
    {
        // Arrange
        var run = await _h.SendAsync(_h.Member, _setup.Source, "請看一下 WEB-1");
        var job = await _h.ClaimAsync(_setup.Worker);

        // Act
        await _h.FinishAsync(_setup.Worker, job);
        await run.ReadAllAsync();

        // Assert
        Assert.That(job.SystemPrompt, Does.Not.Contain("你的電腦").And.Not.Contain("kumi agent"));
        Assert.That(job.SystemPrompt, Does.Contain("代替發問的人"));
        Assert.That(job.Prompt, Does.Contain("請看一下 WEB-1"));
        Assert.That(job.AllowedTools, Does.Contain("propose_item_changes").And.Contain("get_item"));
        Assert.That(job.McpUrl, Is.EqualTo(HostHarness.ServerUrl + "/mcp"));
        Assert.That(job.ToolToken, Does.StartWith("kjt_"));
        Assert.That(job.ProjectKey, Is.EqualTo(AssistantHarness.ProjectKey));
        Assert.That(job.Engine, Is.EqualTo(AiProviderKind.ClaudeCode));
    }
}
