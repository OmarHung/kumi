using PmTool.Abstractions;
using PmTool.Identity;

namespace PmTool.Ai.Tests.HostAgents;

/// <summary>管理的每個方法：只有擁有者能做；管理員、一般成員、訪客、停用的人、被降權的擁有者一律 403 而且什麼都不改。別的組織的代理當作不存在。</summary>
[TestFixture]
public sealed class HostOwnerOnlyTests
{
    private HostHarness _h = null!;
    private HostSetup _setup = null!;
    private Guid _loginId;

    private delegate Task<Failure?> Call(HostHarness h, Actor actor, Guid agentId, Guid sourceId, Guid loginId);

    private static readonly (string Name, Call Run, string NotFoundCode)[] Operations =
    [
        ("Get", async (h, a, _, _, _) => (await h.Service.GetAsync(a, default)).Failure, string.Empty),
        ("CreateAgent", async (h, a, _, _, _) => (await h.Service.CreateAgentAsync(a, new HostAgentInput("新的"), default)).Failure, string.Empty),
        ("UpdateAgent", async (h, a, agent, _, _) => (await h.Service.UpdateAgentAsync(a, agent, new HostAgentInput("改名"), default)).Failure, AiErrorCodes.HostAgentNotFound),
        ("ResetPairing", async (h, a, agent, _, _) => (await h.Service.ResetPairingAsync(a, agent, default)).Failure, AiErrorCodes.HostAgentNotFound),
        ("RemoveAgent", async (h, a, agent, _, _) => (await h.Service.RemoveAgentAsync(a, agent, default)).Failure, AiErrorCodes.HostAgentNotFound),
        ("StartLogin", async (h, a, agent, _, _) => (await h.Service.StartLoginAsync(a, agent, AiProviderKind.Codex, default)).Failure, AiErrorCodes.HostAgentNotFound),
        ("CancelLogin", async (h, a, _, _, login) => (await h.Service.CancelLoginAsync(a, login, default)).Failure, AiErrorCodes.HostLoginNotFound),
        ("Logout", async (h, a, agent, _, _) => (await h.Service.LogoutAsync(a, agent, AiProviderKind.ClaudeCode, default)).Failure, AiErrorCodes.HostAgentNotFound),
        ("CreateSource", async (h, a, agent, _, _) => (await h.Service.CreateSourceAsync(a, HostHarness.SourceInput(agent, AiProviderKind.Codex, "Codex 來源", enabled: false), default)).Failure, AiErrorCodes.HostAgentNotFound),
        ("UpdateSource", async (h, a, agent, source, _) => (await h.Service.UpdateSourceAsync(a, source, HostHarness.SourceInput(agent, name: "改名"), default)).Failure, AiErrorCodes.HostSourceNotFound),
        ("DeleteSource", async (h, a, _, source, _) => (await h.Service.DeleteSourceAsync(a, source, default)).Failure, AiErrorCodes.HostSourceNotFound),
        ("GetUsage", async (h, a, _, source, _) => (await h.Service.GetUsageAsync(a, source, null, default)).Failure, AiErrorCodes.HostSourceNotFound),
    ];

    public static IEnumerable<TestCaseData> OperationCases() => Operations.Select(o => new TestCaseData(o.Name).SetArgDisplayNames(o.Name));

    public static IEnumerable<TestCaseData> IdOperationCases() =>
        Operations.Where(o => o.NotFoundCode.Length > 0).Select(o => new TestCaseData(o.Name).SetArgDisplayNames(o.Name));

    private static (Call Run, string NotFoundCode) Find(string name)
    {
        var found = Operations.Single(o => o.Name == name);
        return (found.Run, found.NotFoundCode);
    }

    [SetUp]
    public async Task SetUp()
    {
        _h = await HostHarness.StartAsync();
        _setup = await _h.SetUpAsync();
        var login = await _h.Service.StartLoginAsync(_h.Owner, _setup.Worker.AgentId, AiProviderKind.Codex, default);
        _loginId = login.Value.Login.Id;
    }

    [TearDown]
    public async Task TearDown() => await _h.DisposeAsync();

    [TestCaseSource(nameof(OperationCases))]
    public async Task Admin_is_refused_even_though_actor_says_admin(string name)
    {
        // Arrange
        var (run, _) = Find(name);

        // Act
        var failure = await run(_h, _h.Admin, _setup.Worker.AgentId, _setup.Source.Id, _loginId);

        // Assert
        Assert.That(failure?.Code, Is.EqualTo(AiErrorCodes.HostOwnerOnly));
        Assert.That(failure!.Kind, Is.EqualTo(FailureKind.Forbidden));
    }

    [TestCaseSource(nameof(OperationCases))]
    public async Task Member_guest_and_disabled_people_are_refused(string name)
    {
        // Arrange
        var (run, _) = Find(name);

        // Act
        var failures = new List<string?>();
        foreach (var actor in new[] { _h.Member, _h.Guest, _h.Disabled })
        {
            failures.Add((await run(_h, actor, _setup.Worker.AgentId, _setup.Source.Id, _loginId))?.Code);
        }

        // Assert
        Assert.That(failures, Is.All.EqualTo(AiErrorCodes.HostOwnerOnly));
    }

    [TestCaseSource(nameof(OperationCases))]
    public async Task Owner_who_was_demoted_is_refused_on_the_next_call(string name)
    {
        // Arrange
        var (run, _) = Find(name);
        _h.People.SetRole(_h.Assistant.OrganizationId, _h.Owner.PersonId, OrganizationRole.Admin);

        // Act
        var failure = await run(_h, _h.Owner, _setup.Worker.AgentId, _setup.Source.Id, _loginId);

        // Assert
        Assert.That(failure?.Code, Is.EqualTo(AiErrorCodes.HostOwnerOnly));
    }

    [TestCaseSource(nameof(IdOperationCases))]
    public async Task Owner_of_another_organization_sees_nothing_of_this_one(string name)
    {
        // Arrange
        var (run, notFound) = Find(name);

        // Act
        var failure = await run(_h, _h.OtherOwner, _setup.Worker.AgentId, _setup.Source.Id, _loginId);

        // Assert
        Assert.That(failure?.Code, Is.EqualTo(notFound));
    }

    [Test]
    public async Task Refused_calls_change_nothing()
    {
        // Arrange
        var before = (await _h.Service.GetAsync(_h.Owner, default)).Value;

        // Act
        foreach (var operation in Operations)
        {
            await operation.Run(_h, _h.Admin, _setup.Worker.AgentId, _setup.Source.Id, _loginId);
        }

        // Assert
        var after = (await _h.Service.GetAsync(_h.Owner, default)).Value;
        Assert.That(after.Agents.Select(a => (a.Id, a.Name, a.State)), Is.EqualTo(before.Agents.Select(a => (a.Id, a.Name, a.State))));
        Assert.That(after.Sources.Select(s => (s.Id, s.Name, s.Enabled)), Is.EqualTo(before.Sources.Select(s => (s.Id, s.Name, s.Enabled))));
    }

    [Test]
    public async Task Owner_can_do_everything_and_other_organization_overview_is_empty()
    {
        // Arrange
        // Act
        var overview = await _h.Service.GetAsync(_h.Owner, default);
        var other = await _h.Service.GetAsync(_h.OtherOwner, default);

        // Assert
        Assert.That(overview.Value.Agents, Has.Count.EqualTo(1));
        Assert.That(overview.Value.Sources, Has.Count.EqualTo(1));
        Assert.That(other.Value.Agents, Is.Empty);
        Assert.That(other.Value.Sources, Is.Empty);
    }
}
