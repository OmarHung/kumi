using PmTool.Abstractions;
using PmTool.Blueprint;
using PmTool.Blueprint.Model;
using PmTool.Blueprint.Planning;

namespace PmTool.Work.Tests.ChangeSets;

/// <summary>
/// 黃金案例：套用前的資料 + 變更集 → 套用後的資料。套用後一律透過公開介面讀回來比對：
/// 項目、專案的藍圖與版本、項目的活動。
/// </summary>
[TestFixture]
public sealed class ChangeSetGoldenTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ApplyAsync_MergingClientAcceptanceIntoInternalAcceptance_MovesTheItemsAndRecordsTheMove()
    {
        // Arrange：WEB-1 需求與 WEB-3 需求變更單停在客戶驗收，WEB-2 在內部驗收，WEB-4 是任務。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var manager = await scenario.MemberAsync("WEB", "project_manager");
        var atClient = await DeliveryItemAsync(scenario, manager, "requirement", "會員登入", "client_qa");
        var atInternal = await DeliveryItemAsync(scenario, manager, "requirement", "購物車", "internal_qa");
        var changeRequest = await DeliveryItemAsync(scenario, manager, "change_request", "加上社群登入", "client_qa");
        var task = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "寫測試" });
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        var proposed = await ChangeSetDrafts.ProposeAsync(
            scenario, "WEB", MergeAcceptance, BlueprintEdits.Moves(("delivery", "client_qa", "internal_qa")), manager);
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        var appliedAt = _host.Clock.GetUtcNow();

        // Act
        var applied = await ChangeSetDrafts.ApplyAsync(scenario, "WEB", proposed.ChangeSet.Number, manager);
        var project = (await _host.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None)).Value;
        var items = await ReadAsync(scenario, atClient.Key, atInternal.Key, changeRequest.Key, task.Key);
        var moved = await ActivityLog.LastAsync(_host, scenario.Admin, atClient.Key);
        var untouched = await ActivityLog.LastAsync(_host, scenario.Admin, atInternal.Key);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                proposed.Impacts.Select(i => (i.Impact, i.Affected, i.BlocksApply)),
                Is.EqualTo(new[] { ((DataImpact)new StateMoveImpact("delivery", "client_qa", "internal_qa"), 2L, false) }));
            Assert.That(
                (applied.ChangeSet.Status, applied.ChangeSet.AppliedVersion, applied.ChangeSet.DecidedBy, applied.ChangeSet.DecidedAt),
                Is.EqualTo((ChangeSetStatus.Applied, (long?)2, (Guid?)manager.PersonId, (DateTimeOffset?)appliedAt)));
            Assert.That(applied.Impacts.Select(i => i.Affected), Is.EqualTo(new[] { 0L }), "已套用的不再依資料重算");
            Assert.That(applied.CanApply, Is.False);
            Assert.That(project.Project.BlueprintVersion, Is.EqualTo(2));
            Assert.That(project.Blueprint.Workflows[0].States.Select(s => s.Key), Does.Not.Contain("client_qa"));
            Assert.That(
                items.Select(i => (i.Key, i.State, i.StateCategory)),
                Is.EqualTo(new[]
                {
                    (atClient.Key, "internal_qa", StateCategory.InProgress),
                    (atInternal.Key, "internal_qa", StateCategory.InProgress),
                    (changeRequest.Key, "internal_qa", StateCategory.InProgress),
                    (task.Key, "todo", StateCategory.NotStarted),
                }));
            Assert.That(items[0].UpdatedAt, Is.EqualTo(appliedAt));
            Assert.That(items[1].UpdatedAt, Is.EqualTo(atInternal.UpdatedAt));
            Assert.That(
                (moved.Kind, moved.ActorId, moved.At, moved.FromState, moved.ToState, moved.BlueprintVersion, moved.Changes.Count),
                Is.EqualTo((ActivityKind.Migrated, manager.PersonId, appliedAt, (string?)"client_qa", (string?)"internal_qa", (long?)2, 0)));
            Assert.That(untouched.Kind, Is.EqualTo(ActivityKind.Transitioned), "沒有搬的項目不留活動");
        }
    }

    [Test]
    public async Task ApplyAsync_DroppingFieldsWithValues_ArchivesTheValuesAndRemovesThemFromTheItems()
    {
        // Arrange：WEB-1 兩個欄位都有值，WEB-2 都沒有，WEB-3 只有長文字的備註。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var both = await TaskWithAsync(scenario, ("summary", "範例摘要"), ("notes", "第一行\n第二行"), ("category", "bug"));
        var none = await scenario.CreateTaskAsync("WEB");
        var notesOnly = await TaskWithAsync(scenario, ("notes", "只有備註"));
        var proposed = await ChangeSetDrafts.ProposeAsync(
            scenario, "WEB", b => b.WithoutField("summary").WithoutField("notes"), BlueprintEdits.Drops("summary", "notes"));
        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        await ChangeSetDrafts.ApplyAsync(scenario, "WEB", proposed.ChangeSet.Number);
        var items = await ReadAsync(scenario, both.Key, none.Key, notesOnly.Key);
        var archived = await ArchivedValues.ListAsync(_host, scenario);
        var dropped = await ActivityLog.LastAsync(_host, scenario.Admin, both.Key);
        var rewrite = await _host.Items.UpdateAsync(
            scenario.Admin, both.Key, new WorkItemPatch { Fields = Scenario.Fields(("summary", "再寫一次")) }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                proposed.Impacts.Select(i => (i.Impact, i.Affected)),
                Is.EqualTo(new[] { ((DataImpact)new FieldDropImpact("summary"), 1L), (new FieldDropImpact("notes"), 2L) }));
            Assert.That(items.Select(i => string.Join(",", i.Fields.Keys)), Is.EqualTo(new[] { "category", string.Empty, string.Empty }));
            Assert.That(archived, Is.EqualTo(new[]
            {
                (1L, "notes", "\"第一行\\n第二行\"", 1L),
                (1L, "summary", "\"範例摘要\"", 1L),
                (3L, "notes", "\"只有備註\"", 1L),
            }));
            Assert.That((dropped.Kind, dropped.FromState, dropped.BlueprintVersion), Is.EqualTo((ActivityKind.Migrated, (string?)null, (long?)2)));
            Assert.That(
                ActivityLog.Shapes(dropped),
                Is.EqualTo(new (string, string?, string?)[] { ("summary", "\"範例摘要\"", null), ("notes", null, null) }));
            Assert.That((await ActivityLog.LastAsync(_host, scenario.Admin, none.Key)).Kind, Is.EqualTo(ActivityKind.Created));
        }

        ItemAssert.Invalid(rewrite, WorkErrorCodes.ItemUnknownField, "summary");
    }

    [Test]
    public async Task ApplyAsync_FieldsTakenOffOneItemType_ArchivesThatTypesValuesAndKeepsTheOtherTypes()
    {
        // Arrange：需求變更單不再有變更原因（長文字）與報價金額；兩個欄位本身還在，需求也還有報價金額。
        // WEB-1 需求有報價，WEB-2 需求變更單兩個都有、另外有影響程度，WEB-3 需求變更單只有變更原因，WEB-4 什麼都沒有。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var requirement = await ItemWithAsync(scenario, "requirement", ("quote_amount", 30000));
        var both = await ItemWithAsync(scenario, "change_request", ("change_reason", "客戶改需求"), ("quote_amount", 1200), ("impact", "high"));
        var reasonOnly = await ItemWithAsync(scenario, "change_request", ("change_reason", "只有原因"));
        var empty = await ItemWithAsync(scenario, "change_request");
        var proposed = await ChangeSetDrafts.ProposeAsync(
            scenario, "WEB", b => b.ItemType("change_request", t => t with { Fields = ["impact", "client_signoff_date"] }));
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        var appliedAt = _host.Clock.GetUtcNow();

        // Act
        await ChangeSetDrafts.ApplyAsync(scenario, "WEB", proposed.ChangeSet.Number);
        var items = await ReadAsync(scenario, requirement.Key, both.Key, reasonOnly.Key, empty.Key);
        var archived = await ArchivedValues.ListAsync(_host, scenario);
        var detached = await ActivityLog.LastAsync(_host, scenario.Admin, both.Key);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                proposed.Impacts.Select(i => (i.Impact, i.Affected, i.BlocksApply)),
                Is.EqualTo(new[]
                {
                    ((DataImpact)new FieldDetachImpact("change_request", "change_reason"), 2L, false),
                    (new FieldDetachImpact("change_request", "quote_amount"), 1L, false),
                }));
            Assert.That(
                items.Select(i => string.Join(",", i.Fields.Keys.Order(StringComparer.Ordinal))),
                Is.EqualTo(new[] { "quote_amount", "impact", string.Empty, string.Empty }));
            Assert.That(items[0].Fields["quote_amount"].GetDecimal(), Is.EqualTo(30000m));
            Assert.That((items[0].UpdatedAt, items[1].UpdatedAt, items[3].UpdatedAt), Is.EqualTo((requirement.UpdatedAt, appliedAt, empty.UpdatedAt)));
            Assert.That(archived, Is.EqualTo(new[]
            {
                (2L, "change_reason", "\"客戶改需求\"", 1L),
                (2L, "quote_amount", "1200", 1L),
                (3L, "change_reason", "\"只有原因\"", 1L),
            }));
            Assert.That((detached.Kind, detached.FromState, detached.BlueprintVersion), Is.EqualTo((ActivityKind.Migrated, (string?)null, (long?)2)));
            Assert.That(
                ActivityLog.Shapes(detached),
                Is.EqualTo(new (string, string?, string?)[] { ("change_reason", null, null), ("quote_amount", "1200", null) }));
            Assert.That((await ActivityLog.LastAsync(_host, scenario.Admin, requirement.Key)).Kind, Is.EqualTo(ActivityKind.Created));
            Assert.That((await ActivityLog.LastAsync(_host, scenario.Admin, empty.Key)).Kind, Is.EqualTo(ActivityKind.Created));
        }
    }

    [Test]
    public async Task ApplyAsync_FieldTakenOffATypeWhileItsTypeChanges_ArchivesTheOriginalValueInsteadOfConvertingIt()
    {
        // Arrange：摘要改成數字、轉不過去就擋下，同時從任務的欄位清單拿掉。任務上的值不用轉換（也就不會擋下），原樣封存。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await TaskWithAsync(scenario, ("summary", "範例摘要"));
        var proposed = await ChangeSetDrafts.ProposeAsync(
            scenario,
            "WEB",
            b => b
                .Field("summary", f => f with { Type = FieldType.Number })
                .ItemType("task", t => t with { Fields = [.. t.Fields.Where(f => f != "summary")] }),
            BlueprintEdits.Converts("summary", ConversionFailurePolicy.Abort));
        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        await ChangeSetDrafts.ApplyAsync(scenario, "WEB", proposed.ChangeSet.Number);
        var readBack = (await _host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None)).Value;
        var detached = await ActivityLog.LastAsync(_host, scenario.Admin, item.Key);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                proposed.Impacts.Select(i => (i.Impact, i.Affected, i.BlocksApply)),
                Is.EqualTo(new[]
                {
                    ((DataImpact)new FieldConversionImpact("summary", FieldType.Text, FieldType.Number, ConversionFailurePolicy.Abort), 0L, false),
                    (new FieldDetachImpact("task", "summary"), 1L, false),
                }));
            Assert.That(proposed.CanApply, Is.True);
            Assert.That(readBack.Fields, Is.Empty);
            Assert.That(await ArchivedValues.ListAsync(_host, scenario), Is.EqualTo(new[] { (1L, "summary", "\"範例摘要\"", 1L) }));
            Assert.That(ActivityLog.Shapes(detached), Is.EqualTo(new (string, string?, string?)[] { ("summary", "\"範例摘要\"", null) }));
        }
    }

    [Test]
    public async Task ApplyAsync_StateChangedToDoneCategory_FollowsTheBlueprintAndLeavesMyWork()
    {
        // Arrange：進行中改成完成類。WEB-1 在進行中、WEB-2 在待辦，都指派給我。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var doing = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "做到一半", AssigneeId = scenario.Admin.PersonId });
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        doing = (await _host.Items.TransitionAsync(scenario.Admin, doing.Key, "doing", CancellationToken.None)).Value;
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        var todo = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "還沒開始", AssigneeId = scenario.Admin.PersonId });
        var before = await _host.Items.ListAssignedToMeAsync(scenario.Admin, CancellationToken.None);
        var proposed = await ChangeSetDrafts.ProposeAsync(
            scenario, "WEB", b => b.State("task_flow", "doing", s => s with { Category = StateCategory.Done }));
        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        await ChangeSetDrafts.ApplyAsync(scenario, "WEB", proposed.ChangeSet.Number);
        var after = await _host.Items.ListAssignedToMeAsync(scenario.Admin, CancellationToken.None);
        var readBack = (await _host.Items.GetAsync(scenario.Admin, doing.Key, CancellationToken.None)).Value;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(proposed.Impacts, Is.Empty, "只改類別不用搬資料");
            Assert.That(before.Select(i => i.Key), Is.EqualTo(new[] { doing.Key, todo.Key }));
            Assert.That(after.Select(i => i.Key), Is.EqualTo(new[] { todo.Key }));
            Assert.That((readBack.State, readBack.StateCategory, readBack.UpdatedAt), Is.EqualTo(("doing", StateCategory.Done, doing.UpdatedAt)));
            Assert.That((await ActivityLog.LastAsync(_host, scenario.Admin, doing.Key)).Kind, Is.EqualTo(ActivityKind.Transitioned), "只有類別變了不留活動");
        }
    }

    [Test]
    public async Task ApplyAsync_TypeSwitchedToAnotherWorkflow_MovesItsItemsAndTakesTheNewCategories()
    {
        // Arrange：任務改用交付流程。任務流程的狀態只有 todo、doing、done 這三個在交付流程以外還有項目，其餘的沒有項目也要寫明搬去哪裡。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var todo = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "待辦的任務" });
        var done = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "完成的任務" });
        await _host.Items.TransitionAsync(scenario.Admin, done.Key, "done", CancellationToken.None);
        var requirement = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "需求" });
        var moves = BlueprintEdits.Moves(
            ("delivery", "todo", "confirming"), ("delivery", "doing", "developing"), ("delivery", "done", "closed"),
            ("delivery", "internal_review", "internal_qa"), ("delivery", "waiting_client", "client_qa"), ("delivery", "to_release", "client_qa"),
            ("delivery", "on_hold", "confirming"), ("delivery", "canceled", "closed"));
        var proposed = await ChangeSetDrafts.ProposeAsync(
            scenario, "WEB", b => b.ItemType("task", t => t with { Workflow = "delivery", Fields = ["client_signoff_date"] }), moves);

        // Act
        await ChangeSetDrafts.ApplyAsync(scenario, "WEB", proposed.ChangeSet.Number);
        var items = await ReadAsync(scenario, todo.Key, done.Key, requirement.Key);

        // Assert
        Assert.That(proposed.Impacts.Select(i => i.Affected), Is.EqualTo(new[] { 1L, 0L, 0L, 0L, 0L, 1L, 0L, 0L, 0L }));
        Assert.That(
            items.Select(i => (i.State, i.StateCategory)),
            Is.EqualTo(new[] { ("confirming", StateCategory.NotStarted), ("closed", StateCategory.Done), ("confirming", StateCategory.NotStarted) }));
    }

    [Test]
    public async Task ApplyAsync_OfficialSplitAcceptanceExample_MovesItemsFromQaToInternalAcceptance()
    {
        // Arrange：官方範例的基準是「官網改版」的第 12 版，專案先照那份藍圖存到第 12 版，範例的變更集原樣送出。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var baseline = BlueprintValidator.CreateDefault().ValidateBlueprint(
            RepositoryFiles.ReadAllText("schemas/blueprint/v0/examples/website-redesign.blueprint.yaml"), DocumentFormat.Yaml).Blueprint!;
        for (var version = 2; version <= 12; version++)
        {
            await StoredBlueprints.PublishAsync(_host, scenario, "WEB", baseline);
        }

        var inQa = await scenario.CreateTaskAsync("WEB", "等驗收");
        await MoveThroughAsync(scenario.Admin, inQa.Key, "doing", "qa");
        var inProgress = await scenario.CreateTaskAsync("WEB", "做到一半");
        await MoveThroughAsync(scenario.Admin, inProgress.Key, "doing");
        var example = RepositoryFiles.ReadAllText("schemas/blueprint/v0/examples/split-acceptance.changeset.yaml");
        var proposed = await _host.Changes.ProposeAsync(scenario.Admin, "WEB", example, DocumentFormat.Yaml, CancellationToken.None);
        Assert.That(proposed.Succeeded, Is.True, () => ChangeSetDrafts.Describe(proposed.Failure!));
        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        var applied = await ChangeSetDrafts.ApplyAsync(scenario, "WEB", proposed.Value.ChangeSet.Number);
        var items = await ReadAsync(scenario, inQa.Key, inProgress.Key);
        var moved = await ActivityLog.LastAsync(_host, scenario.Admin, inQa.Key);
        var project = (await _host.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None)).Value;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(proposed.Value.ChangeSet.BaseVersion, Is.EqualTo(12));
            Assert.That(
                proposed.Value.Impacts.Select(i => (i.Impact, i.Affected)),
                Is.EqualTo(new[] { ((DataImpact)new StateMoveImpact("main", "qa", "internal_qa"), 1L) }));
            Assert.That(
                proposed.Value.Changes.Where(c => c.Resource == ResourceKind.State).Select(c => (c.Kind, c.Key)),
                Is.EqualTo(new[] { (ChangeKind.Added, "internal_qa"), (ChangeKind.Added, "client_qa"), (ChangeKind.Removed, "qa") }));
            Assert.That(applied.ChangeSet.AppliedVersion, Is.EqualTo(13));
            Assert.That(project.Project.BlueprintVersion, Is.EqualTo(13));
            Assert.That(items.Select(i => (i.State, i.StateCategory)), Is.EqualTo(new[] { ("internal_qa", StateCategory.InProgress), ("doing", StateCategory.InProgress) }));
            Assert.That((moved.FromState, moved.ToState, moved.BlueprintVersion), Is.EqualTo(("qa", "internal_qa", (long?)13)));
        }
    }

    /// <summary>交付流程拿掉客戶驗收：內部驗收直接可以結案，條件和原本客戶驗收結案的一樣。</summary>
    private static ProjectBlueprint MergeAcceptance(ProjectBlueprint blueprint) =>
        blueprint.WithoutState("delivery", "client_qa").Workflow("delivery", w => w with
        {
            Transitions =
            [
                .. w.Transitions!,
                new WorkflowTransition { From = "internal_qa", To = "closed", AllowedRoles = ["project_manager"], RequireFields = ["client_signoff_date"] },
            ],
        });

    /// <summary>交付流程的項目，從需求確認一路走到 <paramref name="target"/>。</summary>
    private async Task<WorkItem> DeliveryItemAsync(Scenario scenario, Actor manager, string type, string title, string target)
    {
        string[] path = ["quoting", "designing", "developing", "internal_qa", "client_qa"];
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = type, Title = title });
        await MoveThroughAsync(manager, item.Key, [.. path.Take(Array.IndexOf(path, target) + 1)]);
        return (await _host.Items.GetAsync(manager, item.Key, CancellationToken.None)).Value;
    }

    private async Task MoveThroughAsync(Actor actor, string itemKey, params string[] states)
    {
        foreach (var state in states)
        {
            // 同一個時刻的活動依編號排，順序不固定；每一步走一秒，最後一筆才是最後做的。
            _host.Clock.Advance(TimeSpan.FromSeconds(1));
            var result = await _host.Items.TransitionAsync(actor, itemKey, state, CancellationToken.None);
            Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        }
    }

    private Task<WorkItem> TaskWithAsync(Scenario scenario, params (string Key, object? Value)[] fields) =>
        ItemWithAsync(scenario, "task", fields);

    private static Task<WorkItem> ItemWithAsync(Scenario scenario, string type, params (string Key, object? Value)[] fields) =>
        scenario.CreateItemAsync("WEB", new NewWorkItem { Type = type, Title = "範例項目", Fields = Scenario.Fields(fields) });

    private async Task<WorkItem[]> ReadAsync(Scenario scenario, params string[] keys)
    {
        var items = new List<WorkItem>();
        foreach (var key in keys)
        {
            items.Add((await _host.Items.GetAsync(scenario.Admin, key, CancellationToken.None)).Value);
        }

        return [.. items];
    }
}
