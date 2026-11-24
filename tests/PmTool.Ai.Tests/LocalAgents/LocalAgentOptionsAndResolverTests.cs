using PmTool.Abstractions;
using PmTool.Ai.Providers;
using PmTool.Ai.Tests.Assistant.Fakes;

namespace PmTool.Ai.Tests.LocalAgents;

/// <summary>能選的清單怎麼列出本機的引擎、選擇的檢查（離線、別人的、模型不在清單）、本機的引擎不能存成連線。</summary>
[TestFixture]
public sealed class LocalAgentOptionsAndResolverTests
{
    private LocalAgentHarness _h = null!;

    [SetUp]
    public async Task StartAsync() => _h = await LocalAgentHarness.StartAsync();

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    private Task<AiOptions> OptionsAsync(Actor? actor = null) =>
        _h.Host.Get<IAiOptionsService>().GetAsync(actor ?? _h.Me, CancellationToken.None);

    private Task<Result<ResolvedSelection>> ResolveAsync(AiSelection selection, Actor? actor = null) =>
        _h.Host.Get<AiSelectionResolver>().ResolveAsync(actor ?? _h.Me, selection, CancellationToken.None);

    [Test]
    public async Task Options_OnlineEngine_IsListedAsAPersonalConnectionAfterTheOrganizationsAndMyStoredOnes()
    {
        // Arrange
        await _h.Host.Get<IPersonalAiConnectionService>().CreateAsync(
            _h.Me, new AiConnectionInput("我自己存的", AiProviderKind.Anthropic, null, "sk-ant-key-0002", [ScriptedGateway.ModelId], []), CancellationToken.None);
        await _h.CheckInAsync(name: "辦公室的 Mac", engines: [LocalAgentHarness.ClaudeCode(), LocalAgentHarness.Codex()]);

        // Act
        var options = await OptionsAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.Available, Is.True);
            Assert.That(
                options.Connections.Select(c => (c.Name, c.Kind, c.Personal)),
                Is.EqualTo(new[]
                {
                    ("測試連線", AiProviderKind.Anthropic, false),
                    ("我自己存的", AiProviderKind.Anthropic, true),
                    ("Claude Code（辦公室的 Mac）", AiProviderKind.ClaudeCode, true),
                    ("Codex（辦公室的 Mac）", AiProviderKind.Codex, true),
                }));
            var claude = options.Connections.Single(c => c.Kind == AiProviderKind.ClaudeCode);
            Assert.That(claude.Models.Select(m => m.Id), Is.EqualTo(new[] { "default", "sonnet" }));
            Assert.That(claude.Models[1].Efforts, Is.EqualTo(new[] { AiEffort.Low, AiEffort.Medium, AiEffort.High }));
        }
    }

    [Test]
    public async Task Options_OrganizationAiOffButAnEngineOnline_IsAvailableWithOnlyTheEngine()
    {
        // Arrange
        await _h.Host.Get<IAiSettingsService>().SaveAsync(
            _h.Assistant.Admin, new AiSettingsInput(false, null) { AllowPersonalConnections = true }, CancellationToken.None);
        await _h.CheckInAsync();

        // Act
        var options = await OptionsAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.Available, Is.True);
            Assert.That(options.Connections.Select(c => c.Kind), Is.EqualTo(new[] { AiProviderKind.ClaudeCode }));
            Assert.That(options.Default, Is.Null);
        }
    }

    [Test]
    public async Task Options_MachineNotSeenForOverAMinute_IsLeftOut()
    {
        // Arrange
        await _h.CheckInAsync();
        _h.Assistant.Clock.Advance(AiRules.LocalAgentOnlineWindow + TimeSpan.FromSeconds(1));

        // Act
        var options = await OptionsAsync();

        // Assert
        Assert.That(options.Connections.Where(c => c.Kind.IsLocal()), Is.Empty);
    }

    [Test]
    public async Task Options_AnotherPersonsEngine_IsNotListedForMe()
    {
        // Arrange
        var other = new Actor(_h.Me.OrganizationId, Guid.CreateVersion7(), false);
        await _h.CheckInAsync(other, "machine-0009", "別人的電腦");

        // Act
        var mine = await OptionsAsync();
        var theirs = await OptionsAsync(other);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mine.Connections.Where(c => c.Kind.IsLocal()), Is.Empty);
            Assert.That(theirs.Connections.Count(c => c.Kind.IsLocal()), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Options_PersonalAccountsNotAllowed_NoEnginesEvenIfOnline()
    {
        // Arrange
        await _h.CheckInAsync();
        await _h.Host.Get<IAiSettingsService>().SaveAsync(
            _h.Assistant.Admin, new AiSettingsInput(false, null) { AllowPersonalConnections = false }, CancellationToken.None);

        // Act
        var options = await OptionsAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.Available, Is.False);
            Assert.That(options.Connections, Is.Empty);
        }
    }

    [Test]
    public async Task Options_EngineWithNoModels_IsLeftOut()
    {
        // Arrange
        await _h.CheckInAsync(engines: [new LocalEngineInfo(AiProviderKind.Codex, "0.1", [])]);

        // Act
        var options = await OptionsAsync();

        // Assert
        Assert.That(options.Connections.Where(c => c.Kind.IsLocal()), Is.Empty);
    }

    [Test]
    public async Task Resolve_MyOnlineEngineWithAKnownModelAndEffort_Succeeds()
    {
        // Arrange
        var engine = await _h.RegisterAsync(name: "辦公室的 Mac");

        // Act
        var resolved = await ResolveAsync(LocalAgentHarness.Select(engine, "sonnet", AiEffort.Medium));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved.Succeeded, Is.True, () => resolved.Failure?.Message ?? string.Empty);
            Assert.That(resolved.Value.Endpoint.Kind, Is.EqualTo(AiProviderKind.ClaudeCode));
            Assert.That(resolved.Value.Endpoint.Personal, Is.True);
            Assert.That(resolved.Value.ConnectionName, Is.EqualTo("Claude Code（辦公室的 Mac）"));
            Assert.That(resolved.Value.Model.Id, Is.EqualTo("sonnet"));
        }
    }

    [Test]
    public async Task Resolve_NoEffortOnTheDefaultModel_Succeeds()
    {
        // Arrange
        var engine = await _h.RegisterAsync();

        // Act
        var resolved = await ResolveAsync(LocalAgentHarness.Select(engine));

        // Assert
        Assert.That(resolved.Succeeded, Is.True, () => resolved.Failure?.Message ?? string.Empty);
    }

    [Test]
    public async Task Resolve_MachineOffline_ReturnsSelectionInvalidTellingToRunTheAgent()
    {
        // Arrange
        var engine = await _h.RegisterAsync();
        _h.Assistant.Clock.Advance(AiRules.LocalAgentOnlineWindow + TimeSpan.FromSeconds(1));

        // Act
        var resolved = await ResolveAsync(LocalAgentHarness.Select(engine));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved.Failure?.Code, Is.EqualTo(AiErrorCodes.SelectionInvalid));
            Assert.That(resolved.Failure?.Message, Is.EqualTo("你電腦上的 Kumi 代理目前沒有連線，請執行 kumi agent start。"));
        }
    }

    [Test]
    public async Task Resolve_AnotherPersonsEngine_IsSelectionInvalidWithoutSayingItExists()
    {
        // Arrange
        var other = new Actor(_h.Me.OrganizationId, Guid.CreateVersion7(), false);
        var theirs = await _h.RegisterAsync(other, "machine-0009", "別人的電腦");

        // Act
        var resolved = await ResolveAsync(LocalAgentHarness.Select(theirs));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved.Failure?.Code, Is.EqualTo(AiErrorCodes.SelectionInvalid));
            Assert.That(resolved.Failure?.Message, Does.Not.Contain("別人的電腦"));
        }
    }

    [Test]
    public async Task Resolve_ModelNotInTheEnginesList_IsSelectionInvalid()
    {
        // Arrange
        var engine = await _h.RegisterAsync();

        // Act
        var resolved = await ResolveAsync(LocalAgentHarness.Select(engine, "opus"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved.Failure?.Code, Is.EqualTo(AiErrorCodes.SelectionInvalid));
            Assert.That(resolved.Failure?.Message, Does.Contain("opus"));
        }
    }

    [Test]
    public async Task Resolve_EffortNotInTheModelsList_IsSelectionInvalid()
    {
        // Arrange
        var engine = await _h.RegisterAsync();

        // Act
        var forDefault = await ResolveAsync(LocalAgentHarness.Select(engine, "default", AiEffort.High));
        var tooDeep = await ResolveAsync(LocalAgentHarness.Select(engine, "sonnet", AiEffort.Max));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(forDefault.Failure?.Code, Is.EqualTo(AiErrorCodes.SelectionInvalid));
            Assert.That(tooDeep.Failure?.Code, Is.EqualTo(AiErrorCodes.SelectionInvalid));
        }
    }

    [Test]
    public async Task Resolve_OrganizationStoppedAllowingPersonalAccounts_IsSelectionInvalid()
    {
        // Arrange
        var engine = await _h.RegisterAsync();
        await _h.Host.Get<IAiSettingsService>().SaveAsync(
            _h.Assistant.Admin, new AiSettingsInput(false, null) { AllowPersonalConnections = false }, CancellationToken.None);

        // Act
        var resolved = await ResolveAsync(LocalAgentHarness.Select(engine));

        // Assert
        Assert.That(resolved.Failure?.Code, Is.EqualTo(AiErrorCodes.SelectionInvalid));
    }

    [Test]
    public async Task Resolve_RemovedMachine_IsSelectionInvalid()
    {
        // Arrange
        var engine = await _h.RegisterAsync();
        var machine = (await _h.Directory.ListAsync(_h.Me, CancellationToken.None)).Single();
        await _h.Directory.RemoveAsync(_h.Me, machine.Id, CancellationToken.None);

        // Act
        var resolved = await ResolveAsync(LocalAgentHarness.Select(engine));

        // Assert
        Assert.That(resolved.Failure?.Code, Is.EqualTo(AiErrorCodes.SelectionInvalid));
    }

    [Test]
    public async Task Start_EngineOffline_FailsBeforeAnythingIsSaved()
    {
        // Arrange
        var engine = await _h.RegisterAsync();
        var conversation = await _h.Assistant.NewConversationAsync();
        _h.Assistant.Clock.Advance(AiRules.LocalAgentOnlineWindow + TimeSpan.FromSeconds(1));

        // Act
        var started = await _h.Assistant.Service.StartAsync(
            _h.Me, AssistantHarnessProject, conversation.Id, new AssistantMessageInput("嗨", LocalAgentHarness.Select(engine)), CancellationToken.None);
        var row = await _h.Assistant.ConversationRowAsync(conversation.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(started.Failure?.Code, Is.EqualTo(AiErrorCodes.SelectionInvalid));
            Assert.That(row.MessageCount, Is.Zero);
            Assert.That(row.RunningUntil, Is.Null);
        }
    }

    [Test]
    public async Task Start_NoPublicUrlToBuildTheMcpUrl_FailsBeforeAnythingIsSaved()
    {
        // Arrange
        await _h.DisposeAsync();
        _h = await LocalAgentHarness.StartAsync(mcpUrl: null);
        var engine = await _h.RegisterAsync();
        var conversation = await _h.Assistant.NewConversationAsync();

        // Act
        var started = await _h.Assistant.Service.StartAsync(
            _h.Me, AssistantHarnessProject, conversation.Id, new AssistantMessageInput("嗨", LocalAgentHarness.Select(engine)), CancellationToken.None);
        var row = await _h.Assistant.ConversationRowAsync(conversation.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(started.Failure?.Code, Is.EqualTo(AiErrorCodes.SelectionInvalid));
            Assert.That(row.MessageCount, Is.Zero);
        }
    }

    [TestCase(AiProviderKind.ClaudeCode)]
    [TestCase(AiProviderKind.Codex)]
    public async Task StoredConnection_WithALocalEngineKind_IsRejectedAsFieldInvalid(AiProviderKind kind)
    {
        // Arrange
        var input = new AiConnectionInput("本機的", kind, null, null, ["default"], []);

        // Act
        var organization = await _h.Host.Get<IAiSettingsService>().CreateConnectionAsync(_h.Assistant.Admin, input, CancellationToken.None);
        var personal = await _h.Host.Get<IPersonalAiConnectionService>().CreateAsync(_h.Me, input, CancellationToken.None);
        var discovery = await _h.Host.Get<IPersonalAiConnectionService>().DiscoverModelsAsync(
            _h.Me, new AiDiscoveryInput(kind, null, null, null), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            foreach (var failure in new[] { organization.Failure, personal.Failure, discovery.Failure })
            {
                Assert.That(failure?.Code, Is.EqualTo(AiErrorCodes.SettingsInvalid));
                Assert.That(failure?.Details?.Select(d => (d.Code, d.Path)), Does.Contain((AiErrorCodes.FieldInvalid, "kind")));
            }
        }
    }

    private const string AssistantHarnessProject = AssistantHarness.ProjectKey;
}
