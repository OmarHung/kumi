using System.Net;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 「簡單任務看板」的項目頁：說明只出現在單一項目的回應裡；新增、修改、改狀態、留言各留下一筆活動，
/// 由舊到新排列。修改只記值真的有變的欄位。
/// </summary>
[TestFixture]
public sealed class ItemActivityFlowTests : RealStackTestBase
{
    // 合約寫的上限，故意寫死：常數被改了，這裡要跟著失敗，提醒網頁與文件一起改。
    private const int MaxDescriptionLength = 50_000;
    private const int MaxLongTextFieldLength = 50_000;

    [Test]
    public async Task Description_IsReturnedOnlyByTheSingleItemResponses()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "DESC", "simple_tasks");

        // Act
        var created = await RealStackSteps.CreateItemAsync(owner.Client, "DESC", new
        {
            type = "task",
            title = "寫首頁",
            assigneeId = owner.PersonId,
            description = "首頁的版面\n與文案",
        });
        var fetched = await RealStackSteps.GetAsync(owner.Client, "/v1/items/DESC-1");
        var list = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/DESC/items");
        var myWork = await RealStackSteps.GetAsync(owner.Client, "/v1/my-work");

        // Assert：同一個資料庫裡別的測試也可能有我的項目，「我的工作」只看這個專案的。
        var listed = list["items"]!.AsArray();
        var mine = myWork["items"]!.AsArray().Where(i => i?["key"]?.GetValue<string>() == "DESC-1").ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created["description"]?.GetValue<string>(), Is.EqualTo("首頁的版面\n與文案"));
            Assert.That(fetched["description"]?.GetValue<string>(), Is.EqualTo("首頁的版面\n與文案"));
            Assert.That(RealStackSteps.KeysOf(listed), Is.EqualTo(new[] { "DESC-1" }));
            Assert.That(listed[0]?.AsObject().ContainsKey("description"), Is.False, "清單的項目沒有說明");
            Assert.That(mine, Has.Length.EqualTo(1));
            Assert.That(mine[0]?.AsObject().ContainsKey("description"), Is.False, "我的工作的項目沒有說明");
        }
    }

    [Test]
    public async Task Patch_RecordsOnlyTheChangedFieldsInTheContractShapes()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "PATCH", "simple_tasks");
        var created = await RealStackSteps.CreateItemAsync(owner.Client, "PATCH", NewTask());

        // Act
        var patched = await RealStackSteps.PatchItemAsync(owner.Client, "PATCH-1", Edits(owner.PersonId));
        var activity = await RealStackSteps.ActivityAsync(owner.Client, "PATCH-1");

        // Assert
        var entries = activity["items"]!.AsArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created["description"]?.GetValue<string>(), Is.EqualTo("第一版說明"));
            Assert.That(patched["description"]?.GetValue<string>(), Is.EqualTo("第二版說明"));
            Assert.That(RealStackSteps.KindsOf(entries), Is.EqualTo(new[] { "created", "updated" }));
            Assert.That(JsonNode.DeepEquals(entries[1]?["changes"], ExpectedChanges(owner.PersonId)), Is.True, entries[1]?["changes"]?.ToJsonString());
            Assert.That(entries[0]?["changes"]?.AsArray(), Is.Empty, "只有 updated 有 changes");
            Assert.That(entries.Select(e => e?["actor"]?["personId"]?.GetValue<Guid>()), Is.All.EqualTo(owner.PersonId));
            Assert.That(activity["nextCursor"], Is.Null);
            Assert.That(RealStackSteps.IsWrittenAsNull(activity, "nextCursor"), Is.True, "沒有下一頁時寫出 null");
        }
    }

    [Test]
    public async Task Patch_WithEveryValueUnchanged_RecordsNothingAndKeepsUpdatedAt()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "SAME", "simple_tasks");
        var created = await RealStackSteps.CreateItemAsync(owner.Client, "SAME", NewTask());

        // Act
        var patched = await RealStackSteps.PatchItemAsync(owner.Client, "SAME-1", SameAsNewTask());
        var activity = await RealStackSteps.ActivityAsync(owner.Client, "SAME-1");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(RealStackSteps.KindsOf(activity["items"]), Is.EqualTo(new[] { "created" }));
            Assert.That(patched["updatedAt"]?.GetValue<string>(), Is.EqualTo(created["updatedAt"]?.GetValue<string>()));
            Assert.That(JsonNode.DeepEquals(patched, created), Is.True, "回來的項目和改之前一模一樣");
        }
    }

    [Test]
    public async Task TransitionAndComment_AreRecordedAfterTheEditInOrder()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "MOVE", "simple_tasks");
        await RealStackSteps.CreateItemAsync(owner.Client, "MOVE", NewTask());
        await RealStackSteps.PatchItemAsync(owner.Client, "MOVE-1", new { title = "寫首頁（第二版）" });

        // Act
        var moved = await RealStackSteps.TransitionThroughAsync(owner.Client, "MOVE-1", "doing");
        var comment = await RealStackSteps.CommentAsync(owner.Client, "MOVE-1", " \n先做版面。\r\n文案下週給。\t\n ");
        var item = await RealStackSteps.GetAsync(owner.Client, "/v1/items/MOVE-1");
        var activity = await RealStackSteps.ActivityAsync(owner.Client, "MOVE-1");

        // Assert
        var entries = activity["items"]!.AsArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(RealStackSteps.KindsOf(entries), Is.EqualTo(new[] { "created", "updated", "transitioned", "commented" }));
            Assert.That(entries[2]?["fromState"]?.GetValue<string>(), Is.EqualTo("todo"));
            Assert.That(entries[2]?["toState"]?.GetValue<string>(), Is.EqualTo("doing"));
            Assert.That(entries[2]?["changes"]?.AsArray(), Is.Empty);
            Assert.That(RealStackSteps.IsWrittenAsNull(entries[2], "comment"), Is.True);
            Assert.That(comment["comment"]?.GetValue<string>(), Is.EqualTo("先做版面。\n文案下週給。"), "去掉頭尾空白，換行統一成 \\n 後保留");
            Assert.That(comment["actor"]?["personId"]?.GetValue<Guid>(), Is.EqualTo(owner.PersonId));
            Assert.That(comment["actor"]?["displayName"]?.GetValue<string>(), Is.EqualTo(RealStackFactory.OwnerName));
            Assert.That(RealStackSteps.IsWrittenAsNull(comment, "fromState"), Is.True);
            Assert.That(JsonNode.DeepEquals(entries[3], comment), Is.True, "列出來的和留言時回的一樣");
            Assert.That(item["updatedAt"]?.GetValue<string>(), Is.EqualTo(moved["updatedAt"]?.GetValue<string>()), "留言不算改了項目");
        }
    }

    [Test]
    public async Task DescriptionAndLongText_BothFilledToTheLimit_AreCreatedInOneRequest()
    {
        // Arrange：內文照原樣組，和網頁的 JSON.stringify 一樣不把中文跳脫成 \uXXXX；兩個加起來約 300,000 位元組。
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "FULL", "simple_tasks");
        var description = new string('說', MaxDescriptionLength);
        var notes = new string('備', MaxLongTextFieldLength);

        // Act
        var created = await RealStackSteps.CreateItemAsync(owner.Client, "FULL", ApiRequests.Document(
            $$$"""{"type":"task","title":"填滿上限","description":"{{{description}}}","fields":{"notes":"{{{notes}}}"}}"""));
        var stored = await RealStackSteps.GetAsync(owner.Client, "/v1/items/FULL-1");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created["key"]?.GetValue<string>(), Is.EqualTo("FULL-1"));
            Assert.That(stored["description"]?.GetValue<string>(), Is.EqualTo(description));
            Assert.That(stored["fields"]?["notes"]?.GetValue<string>(), Is.EqualTo(notes));
        }
    }

    [Test]
    public async Task Description_WithNulOrOverTheLimit_IsRejectedBeforeReachingTheDatabase()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "BAD", "simple_tasks");
        await RealStackSteps.CreateItemAsync(owner.Client, "BAD", NewTask());

        // Act：NUL 送到 PostgreSQL 會是 22021，沒擋就是 500。
        using var withNul = await owner.Client.PatchAsync(
            "/v1/items/BAD-1", ApiRequests.Json(new { description = $"前{(char)0x0000}後" }));
        using var tooLong = await owner.Client.PostAsync("/v1/projects/BAD/items", ApiRequests.Json(new
        {
            type = "task",
            title = "說明太長",
            description = new string('說', MaxDescriptionLength + 1),
        }));
        var stored = await RealStackSteps.GetAsync(owner.Client, "/v1/items/BAD-1");
        var list = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/BAD/items");
        var activity = await RealStackSteps.ActivityAsync(owner.Client, "BAD-1");

        // Assert
        var nulDetails = await ApiRequests.AssertFailureEnvelopeAsync(
            withNul, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.ItemInvalidValue);
        var tooLongDetails = await ApiRequests.AssertFailureEnvelopeAsync(
            tooLong, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.ItemDescriptionTooLong);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ApiRequests.Describe(nulDetails), Is.EqualTo(new[] { "item.invalid_value|description" }));
            Assert.That(ApiRequests.Describe(tooLongDetails), Is.EqualTo(new[] { "item.description_too_long|description" }));
            Assert.That(stored["description"]?.GetValue<string>(), Is.EqualTo("第一版說明"), "被擋下來的修改沒有套用");
            Assert.That(RealStackSteps.KeysOf(list["items"]), Is.EqualTo(new[] { "BAD-1" }), "被擋下來的新增沒有建立項目");
            Assert.That(RealStackSteps.KindsOf(activity["items"]), Is.EqualTo(new[] { "created" }));
        }
    }

    private static object NewTask() => new
    {
        type = "task",
        title = "寫首頁",
        description = "第一版說明",
        priority = "high",
        dueDate = "2026-12-01",
        fields = new Dictionary<string, object>
        {
            ["category"] = "feature",
            ["tags"] = new[] { "frontend", "docs" },
            ["summary"] = "首頁",
            ["notes"] = "第一版備註",
        },
    };

    /// <summary>
    /// 和 <see cref="NewTask"/> 的值都一樣：本來就空的負責人、開始日、檢查日再清一次，多選換個順序，
    /// 說明多一個結尾的換行（存之前會去掉頭尾空白）。
    /// </summary>
    private static object SameAsNewTask() => new
    {
        title = "寫首頁",
        description = "第一版說明\n",
        assigneeId = (Guid?)null,
        priority = "high",
        startDate = (string?)null,
        dueDate = "2026-12-01",
        fields = new Dictionary<string, object?>
        {
            ["category"] = "feature",
            ["tags"] = new[] { "docs", "frontend" },
            ["review_date"] = null,
            ["summary"] = "首頁",
            ["notes"] = "第一版備註",
        },
    };

    /// <summary>標題與摘要給一樣的值，它們不該出現在活動裡。</summary>
    private static object Edits(Guid me) => new
    {
        title = "寫首頁",
        description = "第二版說明",
        assigneeId = me,
        priority = "urgent",
        dueDate = "2026-12-15",
        fields = new Dictionary<string, object>
        {
            ["category"] = "bug",
            ["review_date"] = "2026-11-20",
            ["summary"] = "首頁",
            ["notes"] = "第二版備註",
        },
    };

    /// <summary>核心欄位在前，自訂欄位照類型裡的順序。說明與長文字只記有改，前後值都是 null。</summary>
    private static JsonArray ExpectedChanges(Guid me) => new(
        Change("description", null, null),
        Change("assignee", null, me.ToString("D")),
        Change("priority", "high", "urgent"),
        Change("due_date", "2026-12-01", "2026-12-15"),
        Change("category", "feature", "bug"),
        Change("review_date", null, "2026-11-20"),
        Change("notes", null, null));

    private static JsonObject Change(string field, string? from, string? to) =>
        new() { ["field"] = field, ["from"] = from, ["to"] = to };
}
