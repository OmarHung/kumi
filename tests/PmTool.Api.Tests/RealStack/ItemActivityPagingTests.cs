using System.Buffers.Text;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>活動紀錄一頁一頁讀：照游標一路讀到沒有下一頁，不漏不重、由舊到新；被動過的游標不接受。</summary>
[TestFixture]
public sealed class ItemActivityPagingTests : RealStackTestBase
{
    // 翻頁的保險：游標一直不結束時，測試失敗而不是卡住。
    private const int MaxPages = 10;

    [Test]
    public async Task Activity_ReadPageByPage_ReturnsEveryEntryOnceFromOldestToNewest()
    {
        // Arrange：建立一筆加上四則留言，每頁兩筆要讀三頁。
        using var owner = await SignInAsOwnerAsync();
        await CreateTaskAsync(owner, "PAGE");
        foreach (var body in new[] { "第一則", "第二則", "第三則", "第四則" })
        {
            await RealStackSteps.CommentAsync(owner.Client, "PAGE-1", body);
        }

        // Act
        var pages = await ReadAllPagesAsync(owner, "PAGE-1", limit: 2);

        // Assert
        var entries = pages.SelectMany(page => page["items"]!.AsArray()).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(pages.Select(page => page["items"]!.AsArray().Count), Is.EqualTo(new[] { 2, 2, 1 }));
            Assert.That(entries.Select(e => e?["kind"]?.GetValue<string>()), Is.EqualTo(new[]
            {
                "created", "commented", "commented", "commented", "commented",
            }));
            Assert.That(entries.Skip(1).Select(e => e?["comment"]?.GetValue<string>()), Is.EqualTo(new[] { "第一則", "第二則", "第三則", "第四則" }));
            Assert.That(entries.Select(e => e?["id"]?.GetValue<Guid>()).Distinct().Count(), Is.EqualTo(5), "沒有重複");
            Assert.That(pages[^1]["nextCursor"], Is.Null);
        }
    }

    [Test]
    public async Task Activity_KindCommented_ReturnsOnlyCommentsAndRepliesFromTheRealDatabase()
    {
        // Arrange：建立、留言、回覆、改標題、改狀態，種類穿插。
        using var owner = await SignInAsOwnerAsync();
        await CreateTaskAsync(owner, "KIND");
        var top = await RealStackSteps.CommentAsync(owner.Client, "KIND-1", "第一則");
        using var reply = await owner.Client.PostAsync(
            "/v1/items/KIND-1/comments", ApiRequests.Json(new { body = "回覆第一則", replyTo = top["id"]!.GetValue<Guid>() }));
        await ApiRequests.AssertSuccessEnvelopeAsync(reply, HttpStatusCode.Created);
        await RealStackSteps.PatchItemAsync(owner.Client, "KIND-1", new { title = "改過的標題" });
        await RealStackSteps.TransitionThroughAsync(owner.Client, "KIND-1", "doing");

        // Act
        var page = await RealStackSteps.ActivityAsync(owner.Client, "KIND-1", "?kind=commented");

        // Assert
        var entries = page["items"]!.AsArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(RealStackSteps.KindsOf(entries), Is.EqualTo(new[] { "commented", "commented" }));
            Assert.That(entries.Select(e => e?["comment"]?.GetValue<string>()), Is.EqualTo(new[] { "第一則", "回覆第一則" }));
            Assert.That(entries[0]?["replyToId"], Is.Null);
            Assert.That(entries[1]?["replyToId"]?.GetValue<Guid>(), Is.EqualTo(top["id"]!.GetValue<Guid>()));
            Assert.That(page["nextCursor"], Is.Null);
        }
    }

    [Test]
    public async Task Activity_KindCommentedReadPageByPage_ReturnsOnlyCommentsWithoutRepeats()
    {
        // Arrange：三則留言之間夾著修改與改狀態，每頁一筆。
        using var owner = await SignInAsOwnerAsync();
        await CreateTaskAsync(owner, "KPAGE");
        await RealStackSteps.CommentAsync(owner.Client, "KPAGE-1", "第一則");
        await RealStackSteps.PatchItemAsync(owner.Client, "KPAGE-1", new { title = "改過的標題" });
        await RealStackSteps.CommentAsync(owner.Client, "KPAGE-1", "第二則");
        await RealStackSteps.TransitionThroughAsync(owner.Client, "KPAGE-1", "doing");
        await RealStackSteps.CommentAsync(owner.Client, "KPAGE-1", "第三則");

        // Act
        var pages = await ReadAllPagesAsync(owner, "KPAGE-1", limit: 1, "&kind=commented");

        // Assert
        var entries = pages.SelectMany(page => page["items"]!.AsArray()).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(pages, Has.Count.EqualTo(3));
            Assert.That(entries.Select(e => e?["kind"]?.GetValue<string>()), Is.All.EqualTo("commented"));
            Assert.That(entries.Select(e => e?["comment"]?.GetValue<string>()), Is.EqualTo(new[] { "第一則", "第二則", "第三則" }));
            Assert.That(entries.Select(e => e?["id"]?.GetValue<Guid>()).Distinct().Count(), Is.EqualTo(3), "沒有重複");
            Assert.That(pages[^1]["nextCursor"], Is.Null);
        }
    }

    [Test]
    public async Task Activity_WithATamperedCursor_IsRejectedAsAnInvalidQuery()
    {
        // Arrange：兩個項目，項目清單才有下一頁的游標。
        using var owner = await SignInAsOwnerAsync();
        await CreateTaskAsync(owner, "TAMPER");
        await RealStackSteps.CreateItemAsync(owner.Client, "TAMPER", new { type = "task", title = "寫登入頁" });
        await RealStackSteps.CommentAsync(owner.Client, "TAMPER-1", "第一則");
        var firstPage = await RealStackSteps.ActivityAsync(owner.Client, "TAMPER-1", "?limit=1");
        var itemPage = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/TAMPER/items?limit=1");

        // Act
        using var tampered = await owner.Client.GetAsync(PageRoute("TAMPER-1", Tamper(CursorOf(firstPage))));
        using var fromItemList = await owner.Client.GetAsync(PageRoute("TAMPER-1", CursorOf(itemPage)));
        using var untouched = await owner.Client.GetAsync(PageRoute("TAMPER-1", CursorOf(firstPage)));

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(tampered, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.QueryInvalid);
        await ApiRequests.AssertFailureEnvelopeAsync(fromItemList, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.QueryInvalid);
        var next = await ApiRequests.AssertSuccessEnvelopeAsync(untouched, HttpStatusCode.OK);
        Assert.That(next?["items"]?[0]?["comment"]?.GetValue<string>(), Is.EqualTo("第一則"), "沒動過的游標照常接著讀");
    }

    private static async Task CreateTaskAsync(SignedInClient owner, string projectKey)
    {
        await RealStackSteps.CreateProjectAsync(owner.Client, projectKey, "simple_tasks");
        await RealStackSteps.CreateItemAsync(owner.Client, projectKey, new { type = "task", title = "寫首頁" });
    }

    private static async Task<IReadOnlyList<JsonNode>> ReadAllPagesAsync(SignedInClient client, string itemKey, int limit, string extraQuery = "")
    {
        var pages = new List<JsonNode>();
        string? cursor = null;
        do
        {
            var query = cursor is null ? $"?limit={limit}{extraQuery}" : $"?limit={limit}{extraQuery}&cursor={Uri.EscapeDataString(cursor)}";
            var page = await RealStackSteps.ActivityAsync(client.Client, itemKey, query);
            pages.Add(page);
            cursor = page["nextCursor"]?.GetValue<string>();
        }
        while (cursor is not null && pages.Count < MaxPages);

        Assert.That(cursor, Is.Null, $"讀了 {MaxPages} 頁還沒結束");
        return pages;
    }

    private static string PageRoute(string itemKey, string cursor) =>
        $"/v1/items/{itemKey}/activity?limit=1&cursor={Uri.EscapeDataString(cursor)}";

    private static string CursorOf(JsonNode page) =>
        page["nextCursor"]?.GetValue<string>() ?? throw new AssertionException("這一頁應該還有下一頁");

    /// <summary>
    /// 解開游標、把最後一個字換成不是十六進位的字再包回去：外表仍是合法的 base64url，只有內容被動過，
    /// 這樣才測得到解碼之後的檢查，而不只是格式。
    /// </summary>
    private static string Tamper(string cursor)
    {
        var text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(text[..^1] + "z"));
    }
}
