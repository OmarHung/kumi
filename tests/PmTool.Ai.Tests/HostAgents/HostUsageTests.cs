using PmTool.Abstractions;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Ai.Tests.LocalAgents;
using PmTool.Work;

namespace PmTool.Ai.Tests.HostAgents;

/// <summary>用量：依成員與專案分、只有則數、狀態與 token；月份依組織時區；格式不對 422。</summary>
[TestFixture]
public sealed class HostUsageTests
{
    private HostHarness _h = null!;
    private HostSetup _setup = null!;

    [SetUp]
    public async Task SetUp()
    {
        _h = await HostHarness.StartAsync();
        _setup = await _h.SetUpAsync();
        _h.Assistant.Projects.OnGet = (_, key) => new ProjectDetail(
            new ProjectSummary(IdOf(key), key, $"專案 {key}", 3, "admin", DateTimeOffset.UnixEpoch),
            WorkSamples.Blueprint,
            [],
            WorkSamples.AllPermissions);
    }

    // 同一個專案代號每次都是同一個編號：對話記的是專案編號。
    private static Guid IdOf(string key) => new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(key)));

    [TearDown]
    public async Task TearDown() => await _h.DisposeAsync();

    private async Task ReplyAsync(Actor actor, string project, AssistantMessageStatus status, AssistantUsage? usage = null)
    {
        var run = await _h.SendAsync(actor, _setup.Source, projectKey: project);
        var job = await _h.ClaimAsync(_setup.Worker);
        await _h.ReportAsync(_setup.Worker, job, 1, new LocalAgentText("跑過"));
        await _h.FinishAsync(_setup.Worker, job, status, status == AssistantMessageStatus.Failed ? "壞了" : null, usage);
        await run.ReadAllAsync();
    }

    [Test]
    public async Task Usage_is_grouped_by_person_and_project_most_requests_first()
    {
        // Arrange
        await ReplyAsync(_h.Member, "WEB", AssistantMessageStatus.Complete, new AssistantUsage(10, 5));
        await ReplyAsync(_h.Member, "WEB", AssistantMessageStatus.Failed);
        await ReplyAsync(_h.Member2, "APP", AssistantMessageStatus.Stopped, new AssistantUsage(1, 1));

        // Act
        var usage = (await _h.Service.GetUsageAsync(_h.Owner, _setup.Source.Id, null, default)).Value;

        // Assert
        Assert.That(usage.Totals, Is.EqualTo(new HostUsageTotals(3, 1, 1, 1, 11, 6)));
        Assert.That(usage.People.Select(p => (p.PersonId, p.Totals.Requests)), Is.EqualTo(new[] { (_h.Member.PersonId, 2), (_h.Member2.PersonId, 1) }));
        Assert.That(usage.Projects.Select(p => (p.ProjectKey, p.Totals.Requests)), Is.EqualTo(new[] { ("WEB", 2), ("APP", 1) }));
        Assert.That(usage.Month, Is.EqualTo("2026-11"));
    }

    [Test]
    public async Task Other_months_are_empty_and_a_bad_month_is_invalid()
    {
        // Arrange
        await ReplyAsync(_h.Member, "WEB", AssistantMessageStatus.Complete);

        // Act
        var october = await _h.Service.GetUsageAsync(_h.Owner, _setup.Source.Id, "2026-10", default);
        var november = await _h.Service.GetUsageAsync(_h.Owner, _setup.Source.Id, "2026-11", default);
        var bad = await _h.Service.GetUsageAsync(_h.Owner, _setup.Source.Id, "11/2026", default);

        // Assert
        Assert.That(october.Value.Totals.Requests, Is.EqualTo(0));
        Assert.That(november.Value.Totals.Requests, Is.EqualTo(1));
        Assert.That(bad.Failure?.Code, Is.EqualTo(AiErrorCodes.SettingsInvalid));
    }

    [Test]
    public async Task Month_boundary_follows_the_organization_time_zone()
    {
        // Arrange
        // 時鐘從 UTC 11/18 08:00 開始；撥到 11/30 17:00 UTC（台北 12/1 01:00）再回答一則。
        _h.Clock.Advance(new DateTimeOffset(2026, 11, 30, 17, 0, 0, TimeSpan.Zero) - _h.Clock.GetUtcNow());
        await _h.CheckInAsync(_setup.Worker);
        await ReplyAsync(_h.Member, "WEB", AssistantMessageStatus.Complete);

        // Act
        var november = await _h.Service.GetUsageAsync(_h.Owner, _setup.Source.Id, "2026-11", default);
        var december = await _h.Service.GetUsageAsync(_h.Owner, _setup.Source.Id, "2026-12", default);

        // Assert
        Assert.That(november.Value.Totals.Requests, Is.EqualTo(0));
        Assert.That(december.Value.Totals.Requests, Is.EqualTo(1));
    }

    [Test]
    public async Task Usage_shape_carries_no_conversation_content()
    {
        // Arrange
        // Act
        var members = typeof(HostSourceUsage).GetProperties().Select(p => p.Name)
            .Concat(typeof(HostPersonUsage).GetProperties().Select(p => p.Name))
            .Concat(typeof(HostProjectUsage).GetProperties().Select(p => p.Name))
            .Concat(typeof(HostUsageTotals).GetProperties().Select(p => p.Name))
            .ToList();

        // Assert
        Assert.That(members, Has.None.Matches<string>(name => name.Contains("Title") || name.Contains("Text") || name.Contains("Message") || name.Contains("Conversation")));
        await Task.CompletedTask;
    }

    [Test]
    public async Task Usage_of_another_organizations_or_unknown_source_is_not_found()
    {
        // Arrange
        // Act
        var other = await _h.Service.GetUsageAsync(_h.OtherOwner, _setup.Source.Id, null, default);
        var unknown = await _h.Service.GetUsageAsync(_h.Owner, Guid.NewGuid(), null, default);

        // Assert
        Assert.That(other.Failure?.Code, Is.EqualTo(AiErrorCodes.HostSourceNotFound));
        Assert.That(unknown.Failure?.Code, Is.EqualTo(AiErrorCodes.HostSourceNotFound));
    }
}
