using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Identity;

namespace PmTool.Ai.Tests.LocalAgents;

/// <summary>偷到本機代理權杖的人可以登記一台假電腦：同一個人第一次出現沒見過的機器編號時，要通知本人（安全修復第四輪）。</summary>
[TestFixture]
public sealed class LocalAgentMachineNoticeTests
{
    private RecordingSink _sink = null!;
    private AssistantHarness _assistant = null!;

    [SetUp]
    public async Task SetUp()
    {
        _sink = new RecordingSink();
        _assistant = await AssistantHarness.StartAsync(configure: services =>
        {
            services.AddSingleton(LocalAgentHarness.FastTimings);
            services.AddSingleton<IAccountEventSink>(_sink);
            services.AddSingleton<IRequestSource>(new FixedSource());
        });
        var saved = await _assistant.Host.Get<IAiSettingsService>().SaveAsync(
            _assistant.Admin,
            new AiSettingsInput(true, new AiSelection(_assistant.Connection.Id, ScriptedGateway.ModelId, null)) { AllowPersonalConnections = true },
            CancellationToken.None);
        Assert.That(saved.Succeeded, Is.True);
    }

    [TearDown]
    public async Task TearDown() => await _assistant.DisposeAsync();

    private Task<Result<LocalAgentJob?>> CheckInAsync(string machineKey, string name = "我的電腦") =>
        _assistant.Host.Get<ILocalAgentService>().CheckInAsync(
            _assistant.Actor, new LocalAgentCheckIn(machineKey, name, "0.9.0", [LocalAgentHarness.ClaudeCode()]), 0, CancellationToken.None);

    [Test]
    public async Task A_machine_key_seen_for_the_first_time_publishes_one_event_with_the_name_and_source()
    {
        // Arrange & Act
        var first = await CheckInAsync("machine-0001", "辦公室的 Mac");
        var again = await CheckInAsync("machine-0001", "辦公室的 Mac");

        // Assert
        Assert.That(first.Succeeded && again.Succeeded, Is.True);
        var sent = _sink.Events.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent.Kind, Is.EqualTo(AccountEventKind.LocalAgentMachineAdded));
            Assert.That(sent.PersonId, Is.EqualTo(_assistant.Actor.PersonId));
            Assert.That(sent.OrganizationId, Is.EqualTo(_assistant.Actor.OrganizationId));
            Assert.That(sent.MachineName, Is.EqualTo("辦公室的 Mac"));
            Assert.That(sent.SourceAddress, Is.EqualTo("198.51.100.7"));
            Assert.That(sent.EventId, Is.Not.EqualTo(Guid.Empty));
        }
    }

    [Test]
    public async Task A_second_machine_key_after_the_interval_publishes_a_second_event_with_a_new_event_id()
    {
        // Arrange & Act
        await CheckInAsync("machine-0001");
        _assistant.Clock.Advance(LocalAgentMachineNotice.Interval + TimeSpan.FromMinutes(1));
        await CheckInAsync("machine-0002", "另一台");

        // Assert
        Assert.That(_sink.Events, Has.Count.EqualTo(2));
        Assert.That(_sink.Events.Select(e => e.EventId).Distinct().Count(), Is.EqualTo(2));
    }

    [Test]
    public async Task Several_new_machine_keys_within_ten_minutes_publish_only_one_event_but_all_are_registered()
    {
        // Arrange & Act
        await CheckInAsync("machine-0001");
        _assistant.Clock.Advance(TimeSpan.FromMinutes(9));
        var second = await CheckInAsync("machine-0002", "另一台");
        var third = await CheckInAsync("machine-0003", "再一台");

        // Assert
        Assert.That(second.Succeeded && third.Succeeded, Is.True);
        Assert.That(_sink.Events, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task A_machine_added_after_the_interval_counted_from_the_latest_new_machine_publishes_again()
    {
        // Arrange & Act
        await CheckInAsync("machine-0001");
        _assistant.Clock.Advance(TimeSpan.FromMinutes(9));
        await CheckInAsync("machine-0002");
        _assistant.Clock.Advance(LocalAgentMachineNotice.Interval + TimeSpan.FromSeconds(1));
        await CheckInAsync("machine-0003");

        // Assert
        Assert.That(_sink.Events, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task A_failing_sink_does_not_break_the_registration()
    {
        // Arrange
        _sink.Throw = true;

        // Act
        var registered = await CheckInAsync("machine-0001");

        // Assert
        Assert.That(registered.Succeeded, Is.True, () => registered.Failure?.Message ?? string.Empty);
        var options = await _assistant.Host.Get<IAiOptionsService>().GetAsync(_assistant.Actor, CancellationToken.None);
        Assert.That(options.Connections.Any(c => c.Name.Contains("我的電腦", StringComparison.Ordinal)), Is.True);
    }

    private sealed class RecordingSink : IAccountEventSink
    {
        private readonly ConcurrentQueue<AccountEvent> _events = new();

        public bool Throw { get; set; }

        public IReadOnlyList<AccountEvent> Events => [.. _events];

        public Task PublishAsync(AccountEvent accountEvent, CancellationToken cancellationToken)
        {
            if (Throw)
            {
                throw new InvalidOperationException("通知壞了。");
            }

            _events.Enqueue(accountEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedSource : IRequestSource
    {
        public RequestSource? Current => new("198.51.100.7", "kumi-test");
    }
}
