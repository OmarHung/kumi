using System.Net;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 「簡單任務看板」拿掉「進行中」狀態（項目搬到待辦）與「備註」欄位：送出時看得到差異與受影響的項目數，
/// 套用之後藍圖是第 2 版、項目照計畫搬好，每個被動到的項目留一筆 migrated 的活動。
/// </summary>
[TestFixture]
public sealed class ChangeSetApplyTests : RealStackTestBase
{
    private const string Summary = "拿掉進行中與備註";

    [Test]
    public async Task Propose_RemovingAStateAndAField_PreviewsTheChangesAndTheAffectedItems()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await CreateTasksAsync(owner, "PLAN");
        var document = await RemoveDoingAndNotesAsync(owner, "PLAN");

        // Act
        var proposed = await ChangeSetSteps.ProposedAsync(owner.Client, "PLAN", document);
        var fetched = await ChangeSetSteps.GetAsync(owner.Client, "PLAN", 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ChangeSetSteps.Describe(proposed["changeSet"]), Is.EqualTo("#1 pending base=1 applied=null by=Owner Example decidedBy=null"));
            Assert.That(proposed["changeSet"]?["summary"]?.GetValue<string>(), Is.EqualTo(Summary));
            Assert.That(proposed["changeSet"]?["proposedBy"]?["personId"]?.GetValue<Guid>(), Is.EqualTo(owner.PersonId));
            Assert.That(proposed["changeSet"]?["proposedAt"]?.GetValue<DateTimeOffset>(), Is.Not.Null);
            Assert.That(RealStackSteps.IsWrittenAsNull(proposed["changeSet"], "decidedAt"), Is.True, "還沒決定時寫出 null");
            Assert.That(ChangeSetSteps.DescribeChanges(proposed["changes"]), Is.EqualTo(new[]
            {
                "removed|field|notes||",
                "removed|state|doing|task_flow|",
                "modified|item_type|task||fields",
            }));
            Assert.That(JsonNode.DeepEquals(proposed["impacts"], ExpectedImpacts(movedItems: 2, droppedValues: 2)), Is.True, proposed["impacts"]?.ToJsonString());
            Assert.That(proposed["blockers"]?.AsArray(), Is.Empty);
            Assert.That(proposed["canApply"]?.GetValue<bool>(), Is.True);
            Assert.That(ChangeSetSteps.StateKeysOf(proposed["blueprint"], "task_flow"), Is.EqualTo(new[] { "todo", "done" }), "blueprint 是想要的藍圖");
            Assert.That(RealStackSteps.KeysOf(proposed["blueprint"]?["fields"]), Does.Not.Contain("notes"));
            Assert.That(JsonNode.DeepEquals(fetched, proposed), Is.True, "讀回來的和送出時回的一樣");
        }
    }

    [Test]
    public async Task Apply_PendingChangeSet_SavesVersionTwoOfTheBlueprintAndCannotBeAppliedAgain()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await CreateTasksAsync(owner, "SAVE");
        await ChangeSetSteps.ProposedAsync(owner.Client, "SAVE", await RemoveDoingAndNotesAsync(owner, "SAVE"));

        // Act
        var applied = await ChangeSetSteps.AppliedAsync(owner.Client, "SAVE", 1);
        using var again = await ChangeSetSteps.ApplyAsync(owner.Client, "SAVE", 1);
        var project = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/SAVE");
        using var toRemovedState = await RealStackSteps.TransitionAsync(owner.Client, "SAVE-2", "doing");
        using var toRemovedField = await owner.Client.PatchAsync("/v1/items/SAVE-2", ApiRequests.Json(new { fields = new { notes = "還想寫備註" } }));

        // Assert：之後的修改照新的藍圖檢查。
        await ApiRequests.AssertFailureEnvelopeAsync(again, HttpStatusCode.Conflict, WorkErrorCodes.ChangeSetNotPending);
        await ApiRequests.AssertFailureEnvelopeAsync(toRemovedState, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.TransitionNotAllowed);
        await ApiRequests.AssertFailureEnvelopeAsync(toRemovedField, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.ItemUnknownField);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ChangeSetSteps.Describe(applied["changeSet"]), Is.EqualTo("#1 applied base=1 applied=2 by=Owner Example decidedBy=Owner Example"));
            Assert.That(applied["changeSet"]?["decidedBy"]?["personId"]?.GetValue<Guid>(), Is.EqualTo(owner.PersonId));
            Assert.That(applied["changeSet"]?["decidedAt"]?.GetValue<DateTimeOffset>(), Is.Not.Null);
            Assert.That(applied["canApply"]?.GetValue<bool>(), Is.False);
            Assert.That(JsonNode.DeepEquals(applied["impacts"], ExpectedImpacts(movedItems: 0, droppedValues: 0)), Is.True, "已經決定的變更集數量一律是 0");
            Assert.That(project["project"]?["blueprintVersion"]?.GetValue<long>(), Is.EqualTo(2));
            Assert.That(ChangeSetSteps.StateKeysOf(project["blueprint"], "task_flow"), Is.EqualTo(new[] { "todo", "done" }));
            Assert.That(RealStackSteps.KeysOf(project["blueprint"]?["fields"]), Does.Not.Contain("notes").And.Contain("summary"));
            Assert.That(ChangeSetSteps.FieldsOfType(project["blueprint"], "task"), Does.Not.Contain("notes").And.Contain("summary"));
        }
    }

    [Test]
    public async Task Apply_PendingChangeSet_MovesTheItemsAndRecordsAMigratedActivityOnEachTouchedItem()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await CreateTasksAsync(owner, "MOVE");
        await ChangeSetSteps.ProposedAsync(owner.Client, "MOVE", await RemoveDoingAndNotesAsync(owner, "MOVE"));
        var untouchedBefore = await RealStackSteps.GetAsync(owner.Client, "/v1/items/MOVE-4");

        // Act
        await ChangeSetSteps.AppliedAsync(owner.Client, "MOVE", 1);
        var items = new List<JsonNode>();
        var activities = new List<JsonArray>();
        for (var number = 1; number <= 4; number++)
        {
            // 一個接一個讀：每一步的檢查用的範圍是跟著非同步流程走的，同時發出去會互相打架。
            items.Add(await RealStackSteps.GetAsync(owner.Client, $"/v1/items/MOVE-{number}"));
            activities.Add((await RealStackSteps.ActivityAsync(owner.Client, $"MOVE-{number}"))["items"]!.AsArray());
        }

        // Assert
        var notes = new JsonArray(new JsonObject { ["field"] = "notes", ["from"] = null, ["to"] = null });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items.Select(i => $"{i["key"]}|{i["state"]}|{i["stateCategory"]}"), Is.EqualTo(new[]
            {
                "MOVE-1|todo|not_started", "MOVE-2|todo|not_started", "MOVE-3|todo|not_started", "MOVE-4|done|done",
            }));
            Assert.That(items.Select(i => i["fields"]?.AsObject().ContainsKey("notes")), Is.All.False, "備註的值從每個項目拿掉");
            Assert.That(items[2]["fields"]?["summary"]?.GetValue<string>(), Is.EqualTo("設定頁的版面"), "其他欄位不動");
            Assert.That(JsonNode.DeepEquals(items[3], untouchedBefore), Is.True, "沒被動到的項目一模一樣");
            Assert.That(activities.Select(a => RealStackSteps.KindsOf(a).Last()), Is.EqualTo(new[] { "migrated", "migrated", "migrated", "transitioned" }));
            AssertMigrated(activities[0][^1], owner.PersonId, "doing", "todo", notes);
            AssertMigrated(activities[1][^1], owner.PersonId, null, null, notes);
            AssertMigrated(activities[2][^1], owner.PersonId, "doing", "todo", []);
        }
    }

    [Test]
    public async Task Apply_StateMovedToAnotherCategory_UpdatesTheItemsCategoryWithoutRecordingActivity()
    {
        // Arrange：「進行中」改成完成類別，停在那裡的項目就不再是「我的工作」。
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "CATEGORY", "simple_tasks");
        await RealStackSteps.CreateItemAsync(owner.Client, "CATEGORY", new { type = "task", title = "寫首頁", assigneeId = owner.PersonId });
        await RealStackSteps.TransitionThroughAsync(owner.Client, "CATEGORY-1", "doing");
        var blueprint = ChangeSetSteps.WithStateCategory(
            await ChangeSetSteps.BlueprintOfAsync(owner.Client, "CATEGORY"), "task_flow", "doing", "done");

        // Act
        var proposed = await ChangeSetSteps.ProposedAsync(owner.Client, "CATEGORY", ChangeSetSteps.Json(1, "進行中算完成", blueprint));
        await ChangeSetSteps.AppliedAsync(owner.Client, "CATEGORY", 1);
        var item = await RealStackSteps.GetAsync(owner.Client, "/v1/items/CATEGORY-1");
        var activity = await RealStackSteps.ActivityAsync(owner.Client, "CATEGORY-1");
        var myWork = await RealStackSteps.GetAsync(owner.Client, "/v1/my-work");

        // Assert：同一個資料庫裡別的測試也可能有我的項目，「我的工作」只看這個專案的。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ChangeSetSteps.DescribeChanges(proposed["changes"]), Is.EqualTo(new[] { "modified|state|doing|task_flow|category" }));
            Assert.That(proposed["impacts"]?.AsArray(), Is.Empty);
            Assert.That($"{item["state"]}|{item["stateCategory"]}", Is.EqualTo("doing|done"));
            Assert.That(RealStackSteps.KindsOf(activity["items"]), Is.EqualTo(new[] { "created", "transitioned" }), "只有類別變了不留活動");
            Assert.That(RealStackSteps.KeysOf(myWork["items"]).Where(key => key.StartsWith("CATEGORY-", StringComparison.Ordinal)), Is.Empty);
        }
    }

    [Test]
    public async Task Apply_FieldTakenOffTheTypeOnly_ArchivesTheValuesSoTheyNoLongerShowOnTheItems()
    {
        // Arrange：摘要只從任務的欄位清單拿掉，欄位本身還在藍圖裡。第 3 個項目有摘要。
        using var owner = await SignInAsOwnerAsync();
        await CreateTasksAsync(owner, "DETACH");
        var current = await ChangeSetSteps.BlueprintOfAsync(owner.Client, "DETACH");
        var wanted = current.DeepClone().AsObject();
        wanted["itemTypes"]![0]!["fields"]!.AsArray().RemoveAll(f => f?.GetValue<string>() == "summary");

        // Act
        var proposed = await ChangeSetSteps.ProposedAsync(owner.Client, "DETACH", ChangeSetSteps.Json(1, "任務不再有摘要", wanted));
        await ChangeSetSteps.AppliedAsync(owner.Client, "DETACH", 1);
        var item = await RealStackSteps.GetAsync(owner.Client, "/v1/items/DETACH-3");
        var activity = (await RealStackSteps.ActivityAsync(owner.Client, "DETACH-3"))["items"]!.AsArray();
        var project = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/DETACH");

        // Assert
        var expected = new JsonArray(
            new JsonObject { ["kind"] = "field_detach", ["affected"] = 1, ["blocksApply"] = false, ["itemType"] = "task", ["field"] = "summary" });
        var changes = new JsonArray(new JsonObject { ["field"] = "summary", ["from"] = "設定頁的版面", ["to"] = null });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(proposed["impacts"], expected), Is.True, proposed["impacts"]?.ToJsonString());
            Assert.That(item["fields"]?.AsObject().ContainsKey("summary"), Is.False, "值不再留在項目上");
            Assert.That(RealStackSteps.KeysOf(project["blueprint"]?["fields"]), Does.Contain("summary"), "欄位本身還在");
            AssertMigrated(activity[^1], owner.PersonId, null, null, changes);
        }
    }

    [Test]
    public async Task List_ChangeSets_AreNewestFirstWithTheProposerAndTheDecider()
    {
        // Arrange：第 2 份由另一位專案管理員提出。
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "ORDER", "simple_tasks");
        var person = await RealStackSteps.CreateMemberAsync(owner.Client, "Planner Example", "planner@example.com");
        await RealStackSteps.SetMemberAsync(owner.Client, "ORDER", person["personId"]!.GetValue<Guid>(), "admin");
        using var planner = await SignInAsAsync("planner@example.com", RealStackSteps.MemberPassword);
        var blueprint = await ChangeSetSteps.BlueprintOfAsync(owner.Client, "ORDER");

        // Act
        await ChangeSetSteps.ProposedAsync(owner.Client, "ORDER", ChangeSetSteps.Json(
            1, "加上審查中", ChangeSetSteps.WithState(blueprint, "task_flow", "review", "審查中", "in_progress")));
        using var rejected = await ChangeSetSteps.RejectAsync(owner.Client, "ORDER", 1);
        await ChangeSetSteps.ProposedAsync(planner.Client, "ORDER", ChangeSetSteps.Json(
            1, "加上工時", ChangeSetSteps.WithField(blueprint, "task", "effort_hours", "工時", "number")));
        await ChangeSetSteps.ProposedAsync(owner.Client, "ORDER", ChangeSetSteps.Json(
            1, "加上卡關", ChangeSetSteps.WithState(blueprint, "task_flow", "stuck", "卡關", "in_progress")));
        var list = (await RealStackSteps.GetAsync(planner.Client, ChangeSetSteps.RouteOf("ORDER"))).AsArray();

        // Assert
        await ApiRequests.AssertSuccessEnvelopeAsync(rejected, HttpStatusCode.OK);
        var proposedAt = list.Select(c => c?["proposedAt"]?.GetValue<DateTimeOffset>()).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(list.Select(ChangeSetSteps.Describe), Is.EqualTo(new[]
            {
                "#3 pending base=1 applied=null by=Owner Example decidedBy=null",
                "#2 pending base=1 applied=null by=Planner Example decidedBy=null",
                "#1 rejected base=1 applied=null by=Owner Example decidedBy=Owner Example",
            }));
            Assert.That(list.Select(c => c?["summary"]?.GetValue<string>()), Is.EqualTo(new[] { "加上卡關", "加上工時", "加上審查中" }));
            Assert.That(list[1]?["proposedBy"]?["personId"]?.GetValue<Guid>(), Is.EqualTo(planner.PersonId));
            Assert.That(proposedAt, Is.Ordered.Descending);
        }
    }

    /// <summary>
    /// 四個項目：1 有備註、停在進行中；2 有備註、停在待辦；3 沒有備註、停在進行中；4 沒有備註、已經完成。
    /// 所以要搬的狀態有 2 個項目（1、3），有備註的值也有 2 個（1、2），4 不會被動到。
    /// </summary>
    private static async Task CreateTasksAsync(SignedInClient owner, string key)
    {
        await RealStackSteps.CreateProjectAsync(owner.Client, key, "simple_tasks");
        await RealStackSteps.CreateItemAsync(owner.Client, key, new { type = "task", title = "寫首頁", fields = new { notes = "首頁的版面要再討論。\n下週定案。" } });
        await RealStackSteps.CreateItemAsync(owner.Client, key, new { type = "task", title = "寫登入頁", fields = new { notes = "登入頁的文案。" } });
        await RealStackSteps.CreateItemAsync(owner.Client, key, new { type = "task", title = "寫設定頁", fields = new { summary = "設定頁的版面" } });
        await RealStackSteps.CreateItemAsync(owner.Client, key, new { type = "task", title = "寫說明頁" });
        await RealStackSteps.TransitionThroughAsync(owner.Client, $"{key}-1", "doing");
        await RealStackSteps.TransitionThroughAsync(owner.Client, $"{key}-3", "doing");
        await RealStackSteps.TransitionThroughAsync(owner.Client, $"{key}-4", "done");
    }

    private static async Task<HttpContent> RemoveDoingAndNotesAsync(SignedInClient owner, string key)
    {
        var current = await ChangeSetSteps.BlueprintOfAsync(owner.Client, key);
        var wanted = ChangeSetSteps.WithoutField(ChangeSetSteps.WithoutState(current, "task_flow", "doing"), "notes");
        var migrations = new JsonObject
        {
            ["moveStates"] = new JsonArray(ChangeSetSteps.MoveState("task_flow", "doing", "todo")),
            ["dropFields"] = new JsonArray("notes"),
        };
        return ChangeSetSteps.Json(1, Summary, wanted, migrations);
    }

    private static JsonArray ExpectedImpacts(long movedItems, long droppedValues) => new(
        new JsonObject
        {
            ["kind"] = "state_move",
            ["affected"] = movedItems,
            ["blocksApply"] = false,
            ["workflow"] = "task_flow",
            ["from"] = "doing",
            ["to"] = "todo",
        },
        new JsonObject { ["kind"] = "field_drop", ["affected"] = droppedValues, ["blocksApply"] = false, ["field"] = "notes" });

    /// <summary>長文字的前後值只記「有改」，兩邊都是 null，和修改項目時的紀錄一樣。</summary>
    private static void AssertMigrated(JsonNode? activity, Guid actor, string? fromState, string? toState, JsonArray changes)
    {
        Assert.That(activity?["kind"]?.GetValue<string>(), Is.EqualTo("migrated"));
        Assert.That(activity?["blueprintVersion"]?.GetValue<long>(), Is.EqualTo(2));
        Assert.That(activity?["actor"]?["personId"]?.GetValue<Guid>(), Is.EqualTo(actor));
        Assert.That(activity?["fromState"]?.GetValue<string>(), Is.EqualTo(fromState));
        Assert.That(activity?["toState"]?.GetValue<string>(), Is.EqualTo(toState));
        Assert.That(JsonNode.DeepEquals(activity?["changes"], changes), Is.True, activity?["changes"]?.ToJsonString());
    }
}
