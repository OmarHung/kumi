using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Blueprint;
using PmTool.Blueprint.Model;
using PmTool.Blueprint.Planning;

namespace PmTool.Work.Tests.ChangeSets;

/// <summary>送出變更集：通過驗證與計畫的存成等待核准，回傳差異、資料影響與現在能不能套用；有問題的不存。</summary>
[TestFixture]
public sealed class ChangeSetProposalTests
{
    private const int Concurrency = 8;

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ProposeAsync_ValidChangeSet_IsStoredAsPendingWithTheFirstNumber()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        _host.Clock.Advance(TimeSpan.FromMinutes(5));

        // Act
        var proposed = await ChangeSetDrafts.ProposeAsync(
            scenario, "WEB", blueprint => blueprint.State("task_flow", "doing", s => s with { Name = "處理中" }));
        var readBack = await _host.Changes.GetAsync(scenario.Admin, "WEB", proposed.ChangeSet.Number, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(proposed.ChangeSet, Is.EqualTo(new ChangeSetSummary(
                1, ChangeSetStatus.Pending, 1, ChangeSetDrafts.Summary, scenario.Admin.PersonId, WorkTestHost.StartTime.AddMinutes(5))));
            Assert.That(
                proposed.Changes.Select(c => (c.Kind, c.Resource, c.Key, c.ParentKey, string.Join(",", c.ChangedProperties))),
                Is.EqualTo(new[] { (ChangeKind.Modified, ResourceKind.State, "doing", (string?)"task_flow", "name") }));
            Assert.That(proposed.Impacts, Is.Empty);
            Assert.That(proposed.Blockers, Is.Empty);
            Assert.That(proposed.CanApply, Is.True);
            Assert.That(proposed.Blueprint.Workflows[0].States[1].Name, Is.EqualTo("處理中"));
            Assert.That(readBack.Value.ChangeSet, Is.EqualTo(proposed.ChangeSet));
        }
    }

    [Test]
    public async Task ProposeAsync_YamlDocument_IsReadLikeJson()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var (blueprint, _) = await ChangeSetDrafts.CurrentAsync(scenario, "WEB");
        var yaml = $"baseVersion: 1\nsummary: 用 YAML 送出\nblueprint: {BlueprintSerializer.ToJson(blueprint with { Views = [] })}\n";

        // Act
        var result = await _host.Changes.ProposeAsync(scenario.Admin, "WEB", yaml, DocumentFormat.Yaml, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => ChangeSetDrafts.Describe(result.Failure!));
        Assert.That(result.Value.ChangeSet.Summary, Is.EqualTo("用 YAML 送出"));
        Assert.That(
            result.Value.Changes.Select(c => (c.Kind, c.Resource, c.Key)),
            Is.EqualTo(new[] { (ChangeKind.Removed, ResourceKind.View, "board"), (ChangeKind.Removed, ResourceKind.View, "all_tasks") }));
    }

    [Test]
    public async Task ProposeAsync_DocumentThatFailsValidation_ReportsEveryIssueAndIsNotStored()
    {
        // Arrange：搬移指示的流程不存在，被拿掉的欄位其實還在。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var migrations = new MigrationHints
        {
            MoveStates = [new StateMove { Workflow = "nowhere", From = "doing", To = "todo" }],
            DropFields = ["summary"],
        };

        // Act
        var result = await ChangeSetDrafts.TryProposeAsync(scenario, "WEB", blueprint => blueprint, migrations);
        var listed = await _host.Changes.ListAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        AssertInvalid(result, [
            (IssueCodes.ChangeSetMoveUnknownWorkflow, "/migrations/moveStates/0/workflow"),
            (IssueCodes.ChangeSetDropFieldStillExists, "/migrations/dropFields/0"),
        ]);
        Assert.That(listed.Value, Is.Empty);
    }

    [Test]
    public async Task ProposeAsync_UnreadableDocument_IsInvalid()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Changes.ProposeAsync(scenario.Admin, "WEB", "{ \"baseVersion\": ", DocumentFormat.Json, CancellationToken.None);

        // Assert
        Assert.That(result.Failure!.Code, Is.EqualTo(WorkErrorCodes.ChangeSetInvalid));
        Assert.That(result.Failure.Details.Select(d => d.Code), Is.EqualTo(new[] { IssueCodes.DocumentSyntax }));
    }

    [Test]
    public async Task ProposeAsync_RemovedStateWithoutAMove_IsBlockedByThePlanAndNotStored()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await ChangeSetDrafts.TryProposeAsync(scenario, "WEB", blueprint => blueprint.WithoutState("task_flow", "doing"));
        var next = await ChangeSetDrafts.ProposeAsync(scenario, "WEB", blueprint => blueprint with { Views = [] });

        // Assert
        AssertInvalid(result, [(IssueCodes.PlanStateMoveRequired, "/migrations/moveStates")]);
        Assert.That(next.ChangeSet.Number, Is.EqualTo(1), "沒存的不佔流水號");
    }

    [TestCase(0)]
    [TestCase(2)]
    public async Task ProposeAsync_BaseVersionOtherThanTheCurrent_IsInvalid(long baseVersion)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var (blueprint, _) = await ChangeSetDrafts.CurrentAsync(scenario, "WEB");

        // Act
        var result = await _host.Changes.ProposeAsync(
            scenario.Admin, "WEB", ChangeSetDrafts.Json(baseVersion, blueprint), DocumentFormat.Json, CancellationToken.None);

        // Assert
        AssertInvalid(result, [(IssueCodes.PlanBaseVersionMismatch, "/baseVersion")]);
    }

    [Test]
    public async Task ProposeAsync_ControlCharactersInTexts_AreInvalidAndPointAtTheText()
    {
        // Arrange：驗證不管名稱與摘要裡的控制字元，但 NUL 存不進資料庫。換行可以用。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var (blueprint, version) = await ChangeSetDrafts.CurrentAsync(scenario, "WEB");
        var changed = blueprint
            .Field("category", f => f with { Name = "分類" + (char)0x0001 })
            .State("task_flow", "doing", s => s with { Name = "處理\n中" });
        var document = ChangeSetDrafts.Json(version, changed, summary: "摘要" + (char)0x0000);

        // Act
        var result = await _host.Changes.ProposeAsync(scenario.Admin, "WEB", document, DocumentFormat.Json, CancellationToken.None);

        // Assert
        AssertInvalid(result, [
            (WorkErrorCodes.ChangeSetInvalid, "/summary"),
            (WorkErrorCodes.ChangeSetInvalid, "/blueprint/fields/0/name"),
        ]);
    }

    [Test]
    public async Task ProposeAsync_SummaryLongerThanTheLimit_IsInvalidAndPointsAtTheSummary()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var (blueprint, version) = await ChangeSetDrafts.CurrentAsync(scenario, "WEB");
        var tooLong = ChangeSetDrafts.Json(version, blueprint with { Views = [] }, summary: new string('長', WorkRules.MaxChangeSetSummaryLength + 1));
        var atTheLimit = ChangeSetDrafts.Json(version, blueprint with { Views = [] }, summary: new string('長', WorkRules.MaxChangeSetSummaryLength));

        // Act
        var refused = await _host.Changes.ProposeAsync(scenario.Admin, "WEB", tooLong, DocumentFormat.Json, CancellationToken.None);
        var accepted = await _host.Changes.ProposeAsync(scenario.Admin, "WEB", atTheLimit, DocumentFormat.Json, CancellationToken.None);
        var listed = await _host.Changes.ListAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        AssertInvalid(refused, [(WorkErrorCodes.ChangeSetInvalid, "/summary")]);
        Assert.That(refused.Failure!.Details[0].Message, Is.EqualTo("摘要最多 500 個字，現在有 501 個字；請刪短一點。"));
        Assert.That(accepted.Succeeded, Is.True, () => ChangeSetDrafts.Describe(accepted.Failure!));
        Assert.That(listed.Value.Select(c => (c.Number, c.Summary!.Length)), Is.EqualTo(new[] { (1L, WorkRules.MaxChangeSetSummaryLength) }));
    }

    [Test]
    public async Task ProposeAsync_Editor_IsForbidden()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var editor = await scenario.MemberAsync("WEB", "editor");

        // Act
        var result = await ChangeSetDrafts.TryProposeAsync(scenario, "WEB", blueprint => blueprint with { Views = [] }, proposer: editor);
        var listed = await _host.Changes.ListAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        ItemAssert.Failed(result, FailureKind.Forbidden, WorkErrorCodes.Forbidden);
        Assert.That(result.Failure!.Message, Does.Contain("提出設定的修改"));
        Assert.That(listed.Value, Is.Empty);
    }

    [Test]
    public async Task ProposeAsync_CustomRoleWithProposeAndApply_CanProposeAndIsOfferedToApply()
    {
        // Arrange：接案專案的專案經理有 blueprint.propose 與 blueprint.apply。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var manager = await scenario.MemberAsync("WEB", "project_manager");

        // Act
        var result = await ChangeSetDrafts.TryProposeAsync(scenario, "WEB", blueprint => blueprint with { Views = [] }, proposer: manager);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => ChangeSetDrafts.Describe(result.Failure!));
        Assert.That((result.Value.ChangeSet.ProposedBy, result.Value.CanApply), Is.EqualTo((manager.PersonId, true)));
    }

    [Test]
    public async Task ProposeAsync_ProjectNotVisible_IsNotFound()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var document = await ChangeSetDrafts.DraftAsync(scenario, "WEB", blueprint => blueprint);
        var outsider = new Scenario(_host).Admin;

        // Act
        var notMember = await _host.Changes.ProposeAsync(scenario.Person(), "WEB", document, DocumentFormat.Json, CancellationToken.None);
        var otherOrganization = await _host.Changes.ProposeAsync(outsider, "WEB", document, DocumentFormat.Json, CancellationToken.None);
        var missing = await _host.Changes.ProposeAsync(scenario.Admin, "NOPE", document, DocumentFormat.Json, CancellationToken.None);

        // Assert
        ItemAssert.Failed(notMember, FailureKind.NotFound, WorkErrorCodes.ProjectNotFound);
        ItemAssert.Failed(otherOrganization, FailureKind.NotFound, WorkErrorCodes.ProjectNotFound);
        ItemAssert.Failed(missing, FailureKind.NotFound, WorkErrorCodes.ProjectNotFound);
    }

    [Test]
    public async Task ProposeAsync_ManyAtTheSameTime_GetDistinctConsecutiveNumbers()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var document = await ChangeSetDrafts.DraftAsync(scenario, "WEB", blueprint => blueprint with { Views = [] });
        var requests = Enumerable.Range(0, Concurrency).Select(_ => _host.NewRequest()).ToArray();
        using var gate = new SemaphoreSlim(0, Concurrency);

        // Act
        var tasks = requests.Select(request => Task.Run(async () =>
        {
            var changes = request.Scope.ServiceProvider.GetRequiredService<IBlueprintChangeService>();
            await gate.WaitAsync();
            return await changes.ProposeAsync(scenario.Admin, "WEB", document, DocumentFormat.Json, CancellationToken.None);
        })).ToArray();
        gate.Release(Concurrency);
        var results = await Task.WhenAll(tasks);
        foreach (var request in requests)
        {
            await request.Scope.DisposeAsync();
        }

        // Assert
        Assert.That(results.Select(r => r.Succeeded), Has.All.True);
        Assert.That(results.Select(r => r.Value.ChangeSet.Number).Order(), Is.EqualTo(Enumerable.Range(1, Concurrency).Select(n => (long)n)));
    }

    private static void AssertInvalid(Result<ChangeSetDetail> result, (string Code, string Path)[] details)
    {
        Assert.That(result.Failure, Is.Not.Null, "應該被擋下來");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Kind, Is.EqualTo(FailureKind.Invalid));
            Assert.That(result.Failure.Code, Is.EqualTo(WorkErrorCodes.ChangeSetInvalid));
            Assert.That(result.Failure.Details.Select(d => (d.Code, d.Path)), Is.EqualTo(details));
            Assert.That(result.Failure.Details.Select(d => d.Message), Has.All.Not.Empty);
            Assert.That(result.Failure.Message, Does.StartWith("變更集沒有通過檢查："));
        }
    }
}
