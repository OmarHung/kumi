using PmTool.Abstractions;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.Tests.LocalAgents;

/// <summary>登記電腦與引擎：內容的檢查、同一台電腦再登記、台數上限、組織不允許、我的帳號的電腦清單。</summary>
[TestFixture]
public sealed class LocalAgentCheckInTests
{
    private LocalAgentHarness _h = null!;

    [SetUp]
    public async Task StartAsync() => _h = await LocalAgentHarness.StartAsync();

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    [Test]
    public async Task CheckIn_NewMachine_IsListedWithItsEnginesAndOnline()
    {
        // Arrange
        var engines = new[] { LocalAgentHarness.ClaudeCode(), LocalAgentHarness.Codex() };

        // Act
        var result = await _h.CheckInAsync(engines: engines, name: "辦公室的 Mac");
        var machines = await _h.Directory.ListAsync(_h.Me, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Value, Is.Null);
            var machine = machines.Single();
            Assert.That(machine.Name, Is.EqualTo("辦公室的 Mac"));
            Assert.That(machine.AgentVersion, Is.EqualTo("0.9.0"));
            Assert.That(machine.Online, Is.True);
            Assert.That(
                machine.Engines,
                Is.EqualTo(new[]
                {
                    new LocalAgentEngineSummary(AiProviderKind.ClaudeCode, "2.1.330"),
                    new LocalAgentEngineSummary(AiProviderKind.Codex, "0.163.0"),
                }));
        }
    }

    [Test]
    public async Task CheckIn_SameMachineAgain_KeepsTheEngineIdUpdatesTheRestAndDropsEnginesThatAreGone()
    {
        // Arrange
        var first = await _h.RegisterAsync(engines: [LocalAgentHarness.ClaudeCode(), LocalAgentHarness.Codex()]);

        // Act
        await _h.CheckInAsync(engines: [LocalAgentHarness.ClaudeCode(new AiModelOption("opus", "Opus", [AiEffort.High]))], name: "改過名字");
        var options = await _h.Host.Get<IAiOptionsService>().GetAsync(_h.Me, CancellationToken.None);
        var machines = await _h.Directory.ListAsync(_h.Me, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            var engine = options.Connections.Single(c => c.Personal && c.Kind.IsLocal());
            Assert.That(engine.Id, Is.EqualTo(first.Id));
            Assert.That(engine.Name, Is.EqualTo("Claude Code（改過名字）"));
            Assert.That(engine.Models.Select(m => m.Id), Is.EqualTo(new[] { "opus" }));
            Assert.That(machines, Has.Count.EqualTo(1));
            Assert.That(machines[0].Engines, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task CheckIn_NoEngines_OnlyRefreshesThePresence()
    {
        // Arrange
        await _h.RegisterAsync();
        _h.Assistant.Clock.Advance(TimeSpan.FromSeconds(50));

        // Act
        await _h.CheckInAsync(engines: []);
        var machine = (await _h.Directory.ListAsync(_h.Me, CancellationToken.None)).Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(machine.Engines, Is.Empty);
            Assert.That(machine.LastSeenAt, Is.EqualTo(_h.Assistant.Clock.GetUtcNow()));
        }
    }

    [Test]
    public async Task CheckIn_OrganizationDoesNotAllowPersonalAccounts_Returns403PersonalDisabled()
    {
        // Arrange
        await _h.DisposeAsync();
        _h = await LocalAgentHarness.StartAsync(allowPersonal: false);

        // Act
        var result = await _h.CheckInAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(result.Failure?.Code, Is.EqualTo(AiErrorCodes.PersonalDisabled));
        }
    }

    [TestCase("short", TestName = "電腦編號太短")]
    [TestCase("has space in it", TestName = "電腦編號有空白")]
    [TestCase("machine-0001/../x", TestName = "電腦編號有斜線")]
    public async Task CheckIn_BadMachineKey_Returns422AgentInvalid(string key)
    {
        // Act
        var result = await _h.CheckInAsync(machineKey: key);

        // Assert
        AssertAgentInvalid(result, "machineKey");
    }

    [TestCase("", TestName = "電腦名稱是空的")]
    [TestCase("   ", TestName = "電腦名稱只有空白")]
    [TestCase("名稱\0有 NUL", TestName = "電腦名稱有 NUL")]
    [TestCase("名稱\n換行", TestName = "電腦名稱有換行")]
    public async Task CheckIn_BadMachineName_Returns422AgentInvalid(string name)
    {
        // Act
        var result = await _h.CheckInAsync(name: name);

        // Assert
        AssertAgentInvalid(result, "machineName");
    }

    [Test]
    public async Task CheckIn_MachineNameTooLong_Returns422AgentInvalid()
    {
        // Act
        var result = await _h.CheckInAsync(name: new string('甲', AiRules.MaxMachineNameLength + 1));

        // Assert
        AssertAgentInvalid(result, "machineName");
    }

    [Test]
    public async Task CheckIn_EngineKindNotLocal_Returns422AgentInvalid()
    {
        // Arrange
        var engines = new[] { new LocalEngineInfo(AiProviderKind.Anthropic, "1.0", [new AiModelOption("a", "A", [])]) };

        // Act
        var result = await _h.CheckInAsync(engines: engines);

        // Assert
        AssertAgentInvalid(result, "engines[0].kind");
    }

    [Test]
    public async Task CheckIn_DuplicateEngine_Returns422AgentInvalid()
    {
        // Act
        var result = await _h.CheckInAsync(engines: [LocalAgentHarness.ClaudeCode(), LocalAgentHarness.ClaudeCode()]);

        // Assert
        AssertAgentInvalid(result, "engines");
    }

    [Test]
    public async Task CheckIn_TooManyModels_Returns422AgentInvalid()
    {
        // Arrange
        var models = Enumerable.Range(0, AiRules.MaxLocalEngineModels + 1).Select(i => new AiModelOption($"m{i}", $"M{i}", [])).ToArray();

        // Act
        var result = await _h.CheckInAsync(engines: [LocalAgentHarness.ClaudeCode(models)]);

        // Assert
        AssertAgentInvalid(result, "engines[0].models");
    }

    [TestCase("has space", TestName = "模型代號有空白")]
    [TestCase("tab\tid", TestName = "模型代號有 Tab")]
    [TestCase("nul\0id", TestName = "模型代號有 NUL")]
    [TestCase("", TestName = "模型代號是空的")]
    public async Task CheckIn_BadModelId_Returns422AgentInvalid(string id)
    {
        // Act
        var result = await _h.CheckInAsync(engines: [LocalAgentHarness.ClaudeCode(new AiModelOption(id, "名稱", []))]);

        // Assert
        AssertAgentInvalid(result, "engines[0].models[0].id");
    }

    [Test]
    public async Task CheckIn_ReportedDefaultEffort_IsStoredAndOffered()
    {
        // Arrange
        var model = new AiModelOption("opus", "Opus", [AiEffort.Medium, AiEffort.High]) { DefaultEffort = AiEffort.Medium };

        // Act
        await _h.CheckInAsync(engines: [LocalAgentHarness.ClaudeCode(model)]);
        var options = await _h.Host.Get<IAiOptionsService>().GetAsync(_h.Me, CancellationToken.None);

        // Assert
        Assert.That(options.Connections.Single(c => c.Kind.IsLocal()).Models.Single().DefaultEffort, Is.EqualTo(AiEffort.Medium));
    }

    [Test]
    public async Task CheckIn_DefaultEffortNotAmongTheEfforts_IsDroppedWithoutFailing()
    {
        // Arrange
        var model = new AiModelOption("opus", "Opus", [AiEffort.Low]) { DefaultEffort = AiEffort.Max };

        // Act
        var result = await _h.CheckInAsync(engines: [LocalAgentHarness.ClaudeCode(model)]);
        var options = await _h.Host.Get<IAiOptionsService>().GetAsync(_h.Me, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(options.Connections.Single(c => c.Kind.IsLocal()).Models.Single().DefaultEffort, Is.Null);
        }
    }

    [Test]
    public async Task CheckIn_UnknownEffort_Returns422AgentInvalid()
    {
        // Act
        var result = await _h.CheckInAsync(engines: [LocalAgentHarness.ClaudeCode(new AiModelOption("a", "A", [(AiEffort)99]))]);

        // Assert
        AssertAgentInvalid(result, "engines[0].models[0].efforts");
    }

    [Test]
    public async Task CheckIn_DuplicateModelIds_Returns422AgentInvalid()
    {
        // Act
        var result = await _h.CheckInAsync(
            engines: [LocalAgentHarness.ClaudeCode(new AiModelOption("a", "A", []), new AiModelOption("a", "B", []))]);

        // Assert
        AssertAgentInvalid(result, "engines[0].models");
    }

    [Test]
    public async Task CheckIn_InvalidContent_StoresNothing()
    {
        // Act
        await _h.CheckInAsync(name: "壞\0名稱");
        var machines = await _h.Directory.ListAsync(_h.Me, CancellationToken.None);

        // Assert
        Assert.That(machines, Is.Empty);
    }

    [Test]
    public async Task CheckIn_MoreMachinesThanTheLimit_ReplacesTheOneSeenLongestAgo()
    {
        // Arrange
        for (var i = 0; i < AiRules.MaxLocalAgentMachines; i++)
        {
            await _h.CheckInAsync(machineKey: $"machine-{i:D4}", name: $"電腦{i}");
            _h.Assistant.Clock.Advance(TimeSpan.FromSeconds(1));
        }

        // Act
        await _h.CheckInAsync(machineKey: "machine-9999", name: "新的電腦");
        var machines = await _h.Directory.ListAsync(_h.Me, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(machines, Has.Count.EqualTo(AiRules.MaxLocalAgentMachines));
            Assert.That(machines.Select(m => m.Name), Does.Contain("新的電腦"));
            Assert.That(machines.Select(m => m.Name), Does.Not.Contain("電腦0"));
            Assert.That(machines[0].Name, Is.EqualTo("新的電腦"));
        }
    }

    [Test]
    public async Task CheckIn_TheLimitIsPerPerson()
    {
        // Arrange
        var other = new Actor(_h.Me.OrganizationId, Guid.CreateVersion7(), false);
        for (var i = 0; i < AiRules.MaxLocalAgentMachines; i++)
        {
            await _h.CheckInAsync(other, $"machine-{i:D4}", $"別人的{i}");
        }

        // Act
        await _h.CheckInAsync(machineKey: "machine-0001", name: "我的");

        // Assert
        Assert.That((await _h.Directory.ListAsync(other, CancellationToken.None)), Has.Count.EqualTo(AiRules.MaxLocalAgentMachines));
    }

    [Test]
    public async Task Directory_ListsOnlyMyMachinesNewestFirstAndMarksStaleOnesOffline()
    {
        // Arrange
        var other = new Actor(_h.Me.OrganizationId, Guid.CreateVersion7(), false);
        await _h.CheckInAsync(machineKey: "machine-0001", name: "舊的");
        _h.Assistant.Clock.Advance(AiRules.LocalAgentOnlineWindow + TimeSpan.FromSeconds(1));
        await _h.CheckInAsync(machineKey: "machine-0002", name: "新的");
        await _h.CheckInAsync(other, "machine-0003", "別人的");

        // Act
        var machines = await _h.Directory.ListAsync(_h.Me, CancellationToken.None);

        // Assert
        Assert.That(machines.Select(m => (m.Name, m.Online)), Is.EqualTo(new[] { ("新的", true), ("舊的", false) }));
    }

    [Test]
    public async Task Directory_ListStillWorksWhenThePolicyTurnedOffPersonalAccounts()
    {
        // Arrange
        await _h.CheckInAsync();
        await _h.Host.Get<IAiSettingsService>().SaveAsync(
            _h.Assistant.Admin, new AiSettingsInput(false, null) { AllowPersonalConnections = false }, CancellationToken.None);

        // Act
        var machines = await _h.Directory.ListAsync(_h.Me, CancellationToken.None);

        // Assert
        Assert.That(machines, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Directory_RemoveMine_DeletesTheMachineAndItsEnginesFromTheOptions()
    {
        // Arrange
        await _h.RegisterAsync();
        var machine = (await _h.Directory.ListAsync(_h.Me, CancellationToken.None)).Single();

        // Act
        var removed = await _h.Directory.RemoveAsync(_h.Me, machine.Id, CancellationToken.None);
        var options = await _h.Host.Get<IAiOptionsService>().GetAsync(_h.Me, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(removed.Succeeded, Is.True);
            Assert.That(await _h.Directory.ListAsync(_h.Me, CancellationToken.None), Is.Empty);
            Assert.That(options.Connections.Where(c => c.Kind.IsLocal()), Is.Empty);
        }
    }

    [Test]
    public async Task Directory_RemoveSomeoneElsesOrUnknown_ReturnsNotFoundAndKeepsIt()
    {
        // Arrange
        var other = new Actor(_h.Me.OrganizationId, Guid.CreateVersion7(), false);
        await _h.CheckInAsync(other, "machine-0003", "別人的");
        var theirs = (await _h.Directory.ListAsync(other, CancellationToken.None)).Single();

        // Act
        var foreign = await _h.Directory.RemoveAsync(_h.Me, theirs.Id, CancellationToken.None);
        var unknown = await _h.Directory.RemoveAsync(_h.Me, Guid.CreateVersion7(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(foreign.Failure?.Kind, Is.EqualTo(FailureKind.NotFound));
            Assert.That(foreign.Failure?.Code, Is.EqualTo(AiErrorCodes.ConnectionNotFound));
            Assert.That(unknown.Failure?.Code, Is.EqualTo(AiErrorCodes.ConnectionNotFound));
            Assert.That(await _h.Directory.ListAsync(other, CancellationToken.None), Has.Count.EqualTo(1));
        }
    }

    private static void AssertAgentInvalid(Result<LocalAgentJob?> result, string path)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Kind, Is.EqualTo(FailureKind.Invalid));
            Assert.That(result.Failure?.Code, Is.EqualTo(AiErrorCodes.AgentInvalid));
            Assert.That(result.Failure?.Details?.Select(d => d.Path), Does.Contain(path));
            Assert.That(result.Failure?.Message, Is.Not.Empty);
        }
    }
}

internal static class LocalKindExtensions
{
    public static bool IsLocal(this AiProviderKind kind) => kind is AiProviderKind.ClaudeCode or AiProviderKind.Codex;
}
