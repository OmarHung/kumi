using PmTool.Abstractions;
using PmTool.Blueprint;
using PmTool.Blueprint.Model;
using PmTool.Blueprint.Planning;

namespace PmTool.Work.Tests.ChangeSets;

/// <summary>預覽變更集：和送出共用驗證與計畫，但什麼都不存；需要 blueprint.read。</summary>
[TestFixture]
public sealed class ChangeSetPreviewTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task PreviewAsync_ValidChangeSet_ReturnsTheDiffAndCanApply()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var document = await ChangeSetDrafts.DraftAsync(
            scenario, "WEB", blueprint => blueprint.State("task_flow", "doing", s => s with { Name = "處理中" }));

        // Act
        var result = await _host.Changes.PreviewAsync(scenario.Admin, "WEB", document, DocumentFormat.Json, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => ChangeSetDrafts.Describe(result.Failure!));
        using (Assert.EnterMultipleScope())
        {
            Assert.That((result.Value.BaseVersion, result.Value.CurrentVersion), Is.EqualTo((1L, 1L)));
            Assert.That(
                result.Value.Changes.Select(c => (c.Kind, c.Resource, c.Key, string.Join(",", c.ChangedProperties))),
                Is.EqualTo(new[] { (ChangeKind.Modified, ResourceKind.State, "doing", "name") }));
            Assert.That(result.Value.Impacts, Is.Empty);
            Assert.That(result.Value.Blockers, Is.Empty);
            Assert.That(result.Value.CanApply, Is.True);
            Assert.That(result.Value.Blueprint.Workflows[0].States[1].Name, Is.EqualTo("處理中"));
        }
    }

    [Test]
    public async Task PreviewAsync_AfterwardsTheChangeSetListIsStillEmptyAndNumbersAreNotUsed()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var document = await ChangeSetDrafts.DraftAsync(scenario, "WEB", blueprint => blueprint with { Views = [] });

        // Act
        await _host.Changes.PreviewAsync(scenario.Admin, "WEB", document, DocumentFormat.Json, CancellationToken.None);
        var listed = await _host.Changes.ListAsync(scenario.Admin, "WEB", CancellationToken.None);
        var proposed = await _host.Changes.ProposeAsync(scenario.Admin, "WEB", document, DocumentFormat.Json, CancellationToken.None);

        // Assert
        Assert.That(listed.Value, Is.Empty);
        Assert.That(proposed.Value.ChangeSet.Number, Is.EqualTo(1));
    }

    [Test]
    public async Task PreviewAsync_StateMoveOnExistingItems_CountsTheAffectedItems()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateTaskAsync("WEB", "一");
        await scenario.CreateTaskAsync("WEB", "二");
        var migrations = new MigrationHints { MoveStates = [new StateMove { Workflow = "task_flow", From = "todo", To = "doing" }] };
        var document = await ChangeSetDrafts.DraftAsync(scenario, "WEB", b => b.WithoutState("task_flow", "todo"), migrations);

        // Act
        var result = await _host.Changes.PreviewAsync(scenario.Admin, "WEB", document, DocumentFormat.Json, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => ChangeSetDrafts.Describe(result.Failure!));
        Assert.That(
            result.Value.Impacts.Select(i => (i.Impact is StateMoveImpact, i.Affected, i.BlocksApply)),
            Is.EqualTo(new[] { (true, 2L, false) }));
        Assert.That(result.Value.CanApply, Is.True);
    }

    [Test]
    public async Task PreviewAsync_RemovedRoleStillHasMembers_ListsTheBlockerAndCannotApply()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        await scenario.MemberAsync("WEB", "client");
        var document = await ChangeSetDrafts.DraftAsync(scenario, "WEB", WithoutClient);

        // Act
        var result = await _host.Changes.PreviewAsync(scenario.Admin, "WEB", document, DocumentFormat.Json, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => ChangeSetDrafts.Describe(result.Failure!));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Blockers.Select(b => (b.Code, b.Path)), Is.EqualTo(new[] { (WorkErrorCodes.ChangeSetMembersWithRemovedRole, "impacts/0") }));
            Assert.That(result.Value.Impacts.Select(i => (i.Affected, i.BlocksApply)), Is.EqualTo(new[] { (1L, true) }));
            Assert.That(result.Value.CanApply, Is.False);
        }
    }

    [Test]
    public async Task PreviewAsync_BaseVersionIsOutdated_ShowsTheDiffAndBlocksWithTheCurrentVersion()
    {
        // Arrange：先套用一份，讓目前是第 2 版，再拿第 1 版寫的文件來預覽。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var stale = await ChangeSetDrafts.DraftAsync(scenario, "WEB", b => b.State("task_flow", "doing", s => s with { Name = "處理中" }));
        var first = await ChangeSetDrafts.ProposeAsync(scenario, "WEB", b => b with { Views = [] });
        await ChangeSetDrafts.ApplyAsync(scenario, "WEB", first.ChangeSet.Number);

        // Act
        var result = await _host.Changes.PreviewAsync(scenario.Admin, "WEB", stale, DocumentFormat.Json, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => ChangeSetDrafts.Describe(result.Failure!));
        using (Assert.EnterMultipleScope())
        {
            Assert.That((result.Value.BaseVersion, result.Value.CurrentVersion), Is.EqualTo((1L, 2L)));
            Assert.That(result.Value.Blockers.Select(b => (b.Code, b.Path)), Is.EqualTo(new[] { (IssueCodes.PlanBaseVersionMismatch, "/baseVersion") }));
            Assert.That(result.Value.CanApply, Is.False);
            Assert.That(result.Value.Changes, Is.Not.Empty);
        }
    }

    [Test]
    public async Task PreviewAsync_DocumentThatFailsValidation_IsInvalidLikePropose()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var unreadable = await _host.Changes.PreviewAsync(scenario.Admin, "WEB", "{ \"baseVersion\": ", DocumentFormat.Json, CancellationToken.None);
        var unplanned = await _host.Changes.PreviewAsync(
            scenario.Admin, "WEB", await ChangeSetDrafts.DraftAsync(scenario, "WEB", b => b.WithoutState("task_flow", "doing")),
            DocumentFormat.Json, CancellationToken.None);

        // Assert
        ItemAssert.Failed(unreadable, FailureKind.Invalid, WorkErrorCodes.ChangeSetInvalid);
        Assert.That(unreadable.Failure!.Details.Select(d => d.Code), Is.EqualTo(new[] { IssueCodes.DocumentSyntax }));
        ItemAssert.Failed(unplanned, FailureKind.Invalid, WorkErrorCodes.ChangeSetInvalid);
        Assert.That(unplanned.Failure!.Details.Select(d => d.Code), Is.EqualTo(new[] { IssueCodes.PlanStateMoveRequired }));
    }

    [Test]
    public async Task PreviewAsync_ControlCharacterInSummary_IsInvalid()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var (blueprint, version) = await ChangeSetDrafts.CurrentAsync(scenario, "WEB");

        // Act
        var result = await _host.Changes.PreviewAsync(
            scenario.Admin, "WEB", ChangeSetDrafts.Json(version, blueprint, summary: "摘要" + (char)0), DocumentFormat.Json, CancellationToken.None);

        // Assert
        ItemAssert.Failed(result, FailureKind.Invalid, WorkErrorCodes.ChangeSetInvalid);
    }

    [Test]
    public async Task PreviewAsync_ViewerCanReadButCannotApply_AndOthersAreRefused()
    {
        // Arrange：檢視者有 blueprint.read 沒有 blueprint.apply；接案專案的客戶沒有 blueprint.read；陌生人看不到專案。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var viewer = await scenario.MemberAsync("WEB", "viewer");
        var client = await scenario.MemberAsync("WEB", "client");
        var document = await ChangeSetDrafts.DraftAsync(scenario, "WEB", b => b with { Views = [] });

        // Act
        var asViewer = await _host.Changes.PreviewAsync(viewer, "WEB", document, DocumentFormat.Json, CancellationToken.None);
        var asClient = await _host.Changes.PreviewAsync(client, "WEB", document, DocumentFormat.Json, CancellationToken.None);
        var asStranger = await _host.Changes.PreviewAsync(scenario.Person(), "WEB", document, DocumentFormat.Json, CancellationToken.None);
        var missing = await _host.Changes.PreviewAsync(scenario.Admin, "NOPE", document, DocumentFormat.Json, CancellationToken.None);

        // Assert
        Assert.That(asViewer.Succeeded, Is.True, () => ChangeSetDrafts.Describe(asViewer.Failure!));
        Assert.That(asViewer.Value.CanApply, Is.False);
        ItemAssert.Failed(asClient, FailureKind.Forbidden, WorkErrorCodes.Forbidden);
        ItemAssert.Failed(asStranger, FailureKind.NotFound, WorkErrorCodes.ProjectNotFound);
        ItemAssert.Failed(missing, FailureKind.NotFound, WorkErrorCodes.ProjectNotFound);
    }

    /// <summary>拿掉客戶這個角色，連同只給客戶看的檢視設定。</summary>
    private static ProjectBlueprint WithoutClient(ProjectBlueprint blueprint) =>
        blueprint with
        {
            Roles = [.. blueprint.Roles.Where(r => r.Key != "client")],
            Views = [.. blueprint.Views.Select(v => v with { VisibleTo = v.VisibleTo is null ? null : [.. v.VisibleTo.Where(r => r != "client")] })],
        };
}
