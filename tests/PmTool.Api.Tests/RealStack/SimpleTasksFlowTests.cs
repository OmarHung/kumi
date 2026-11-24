using System.Net;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 在「簡單任務看板」的專案裡新增、修改、改狀態、查詢項目。自訂欄位只用形狀規則穩定的型別，
/// 數字與金額欄位的規則另外有人在改，這裡不碰。
/// </summary>
[TestFixture]
public sealed class SimpleTasksFlowTests : RealStackTestBase
{
    [Test]
    public async Task Item_WithCustomFields_IsCreatedWithTheValuesAndTheFirstState()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "CREATE", "simple_tasks");

        // Act
        var item = await RealStackSteps.CreateItemAsync(owner.Client, "CREATE", NewTask(owner.PersonId));
        var fetched = await RealStackSteps.GetAsync(owner.Client, "/v1/items/CREATE-1");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(item["key"]?.GetValue<string>(), Is.EqualTo("CREATE-1"));
            Assert.That(item["state"]?.GetValue<string>(), Is.EqualTo("todo"), "從流程的第一個狀態開始");
            Assert.That(item["stateCategory"]?.GetValue<string>(), Is.EqualTo("not_started"));
            Assert.That(item["assignee"]?["displayName"]?.GetValue<string>(), Is.EqualTo(RealStackFactory.OwnerName));
            Assert.That(item["priority"]?.GetValue<string>(), Is.EqualTo("high"));
            Assert.That(item["dueDate"]?.GetValue<string>(), Is.EqualTo("2026-12-01"));
            Assert.That(JsonNode.DeepEquals(item["fields"], ExpectedFields(owner.PersonId)), Is.True, item["fields"]?.ToJsonString());
            Assert.That(JsonNode.DeepEquals(fetched, item), Is.True, "讀回來和新增時回的一樣");
        }
    }

    [Test]
    public async Task Item_PatchedAndMoved_ChangesAndClearsValuesAndFollowsTheStates()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "EDIT", "simple_tasks");
        await RealStackSteps.CreateItemAsync(owner.Client, "EDIT", NewTask(owner.PersonId));
        var patch = new
        {
            title = "寫首頁（第二版）",
            priority = (string?)null,
            assigneeId = (Guid?)null,
            fields = new Dictionary<string, object?> { ["category"] = "bug", ["tags"] = null, ["blocked"] = null },
        };

        // Act
        var patched = await RealStackSteps.PatchItemAsync(owner.Client, "EDIT-1", patch);
        var doing = await RealStackSteps.TransitionThroughAsync(owner.Client, "EDIT-1", "doing");
        using var again = await RealStackSteps.TransitionAsync(owner.Client, "EDIT-1", "doing");
        var done = await RealStackSteps.TransitionThroughAsync(owner.Client, "EDIT-1", "done");

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(again, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.TransitionNotAllowed);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patched["title"]?.GetValue<string>(), Is.EqualTo("寫首頁（第二版）"));
            Assert.That(patched["priority"]?.GetValue<string>(), Is.EqualTo("none"), "priority 給 null 當成 none");
            Assert.That(patched.AsObject().ContainsKey("assignee") && patched["assignee"] is null, Is.True, "負責人清掉了");
            Assert.That(patched["dueDate"]?.GetValue<string>(), Is.EqualTo("2026-12-01"), "沒給的屬性不動");
            Assert.That(patched["fields"]?["category"]?.GetValue<string>(), Is.EqualTo("bug"));
            Assert.That(patched["fields"]?.AsObject().Select(f => f.Key), Is.EquivalentTo(new[]
            {
                "category", "review_date", "reference_url", "reviewer", "summary",
            }), "給 null 的欄位清掉，沒提到的欄位不動");
            Assert.That(doing["stateCategory"]?.GetValue<string>(), Is.EqualTo("in_progress"));
            Assert.That(done["stateCategory"]?.GetValue<string>(), Is.EqualTo("done"));
        }
    }

    [Test]
    public async Task Items_InTheList_CanBeFilteredSortedAndPaged()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await CreateListProjectAsync(owner, "LIST");

        // Act
        var mine = await ListAsync(owner, "LIST", "assignee=me&sort=number:asc");
        var unassigned = await ListAsync(owner, "LIST", "assignee=unassigned");
        var done = await ListAsync(owner, "LIST", "stateCategory=done");
        var todoByDueDate = await ListAsync(owner, "LIST", "state=todo&type=task&sort=due_date:asc");
        var byDueDateDescending = await ListAsync(owner, "LIST", "sort=due_date:desc");
        var search = await ListAsync(owner, "LIST", $"q={Uri.EscapeDataString("登入")}");
        var firstPage = await ListAsync(owner, "LIST", "limit=2&sort=number:asc");
        var cursor = firstPage["nextCursor"]?.GetValue<string>() ?? string.Empty;
        var secondPage = await ListAsync(owner, "LIST", $"limit=2&sort=number:asc&cursor={Uri.EscapeDataString(cursor)}");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(RealStackSteps.KeysOf(mine["items"]), Is.EqualTo(new[] { "LIST-1", "LIST-3" }));
            Assert.That(RealStackSteps.KeysOf(unassigned["items"]), Is.EqualTo(new[] { "LIST-2" }));
            Assert.That(RealStackSteps.KeysOf(done["items"]), Is.EqualTo(new[] { "LIST-3" }));
            Assert.That(RealStackSteps.KeysOf(todoByDueDate["items"]), Is.EqualTo(new[] { "LIST-2", "LIST-1" }));
            Assert.That(RealStackSteps.KeysOf(byDueDateDescending["items"]), Is.EqualTo(new[] { "LIST-1", "LIST-3", "LIST-2" }));
            Assert.That(RealStackSteps.KeysOf(search["items"]), Is.EqualTo(new[] { "LIST-2" }));
            Assert.That(RealStackSteps.KeysOf(firstPage["items"]), Is.EqualTo(new[] { "LIST-1", "LIST-2" }));
            Assert.That(cursor, Is.Not.Empty);
            Assert.That(RealStackSteps.KeysOf(secondPage["items"]), Is.EqualTo(new[] { "LIST-3" }));
            Assert.That(secondPage["nextCursor"], Is.Null);
        }
    }

    [Test]
    public async Task MyWork_ShowsOnlyOpenItemsAssignedToMe()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await CreateListProjectAsync(owner, "MINE");

        // Act
        var myWork = await RealStackSteps.GetAsync(owner.Client, "/v1/my-work");

        // Assert：MINE-2 沒有負責人，MINE-3 已經完成。同一個資料庫裡別的測試也可能有我的項目，只看這個專案的。
        Assert.That(
            RealStackSteps.KeysOf(myWork["items"]).Where(key => key.StartsWith("MINE-", StringComparison.Ordinal)),
            Is.EqualTo(new[] { "MINE-1" }));
    }

    [Test]
    public async Task LongTextField_FiftyThousandChineseCharacters_IsStoredAndReturned()
    {
        // Arrange：長文字欄位的上限是 WorkRules.MaxLongTextFieldLength 個字；中文一個字 3 個位元組，超過以前的內文上限。
        // 內文照原樣組，和網頁的 JSON.stringify 一樣不把中文跳脫成 \uXXXX。
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "LONG", "simple_tasks");
        var created = new string('長', WorkRules.MaxLongTextFieldLength);
        var changed = new string('改', WorkRules.MaxLongTextFieldLength);

        // Act
        await RealStackSteps.CreateItemAsync(owner.Client, "LONG", ApiRequests.Document(
            $$$"""{"type":"task","title":"長文字","fields":{"notes":"{{{created}}}"}}"""));
        var stored = await RealStackSteps.GetAsync(owner.Client, "/v1/items/LONG-1");
        var patched = await RealStackSteps.PatchItemAsync(owner.Client, "LONG-1", ApiRequests.Document(
            $$$"""{"fields":{"notes":"{{{changed}}}"}}"""));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored["fields"]?["notes"]?.GetValue<string>(), Is.EqualTo(created));
            Assert.That(patched["fields"]?["notes"]?.GetValue<string>(), Is.EqualTo(changed));
        }
    }

    private static object NewTask(Guid me) => new
    {
        type = "task",
        title = "寫首頁",
        assigneeId = me,
        priority = "high",
        dueDate = "2026-12-01",
        fields = ExpectedFields(me),
    };

    private static JsonObject ExpectedFields(Guid me) => new()
    {
        ["category"] = "feature",
        ["tags"] = new JsonArray("frontend", "docs"),
        ["review_date"] = "2026-11-20",
        ["blocked"] = true,
        ["reference_url"] = "https://example.com/spec",
        ["reviewer"] = me.ToString("D"),
        ["summary"] = "首頁的版面與文案",
    };

    /// <summary>三個項目：1 是我的、2 沒有負責人、3 是我的但已經完成。到期日刻意和編號的順序不同。</summary>
    private static async Task CreateListProjectAsync(SignedInClient owner, string key)
    {
        await RealStackSteps.CreateProjectAsync(owner.Client, key, "simple_tasks");
        await RealStackSteps.CreateItemAsync(owner.Client, key, new { type = "task", title = "首頁", assigneeId = owner.PersonId, dueDate = "2026-12-03" });
        await RealStackSteps.CreateItemAsync(owner.Client, key, new { type = "task", title = "登入頁", dueDate = "2026-12-01" });
        await RealStackSteps.CreateItemAsync(owner.Client, key, new { type = "task", title = "設定頁", assigneeId = owner.PersonId, dueDate = "2026-12-02" });
        await RealStackSteps.TransitionThroughAsync(owner.Client, $"{key}-3", "done");
    }

    private static Task<JsonNode> ListAsync(SignedInClient owner, string projectKey, string query) =>
        RealStackSteps.GetAsync(owner.Client, $"/v1/projects/{projectKey}/items?{query}");
}
