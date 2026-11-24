using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 歷史匯入的主線（真的資料庫、真的儲存空間），照命令列的做法用權杖：設定成員、項目、附件、留言（引用附件）、查詢、重跑全部略過，
/// 時間軸帶 importedFrom 與原作者，匯入的歷史不產生任何通知；權杖的範圍各管各的。
/// </summary>
[TestFixture]
public sealed class HistoryImportFlowTests : RealStackTestBase
{
    private const string Project = "HIS";

    [Test]
    public async Task ImportFlow_MembersItemsAttachmentsCommentsLookupAndRerun_KeepsHistoryAndIsIdempotent()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, Project, "simple_tasks");
        var person = await RealStackSteps.CreateMemberAsync(owner.Client, "Author Example", RealStackSteps.UniqueEmail("history-author"));
        var authorId = person["personId"]!.GetValue<Guid>();
        var token = await CreateTokenAsync(owner, "items:read", "items:write", "blueprint:write");
        using var cli = Factory.CreateClient();
        await Factory.RunNotificationsOnceAsync();
        var unreadBefore = await UnreadAsync(owner);

        // Act：成員（blueprint:write）→ 項目 → 附件 → 留言（引用附件）
        using var member = await SendAsync(cli, HttpMethod.Put, $"/v1/projects/{Project}/members/{authorId}", token, ApiRequests.Json(new { role = "editor" }));
        using var items = await SendAsync(cli, HttpMethod.Post, $"/v1/projects/{Project}/history/items", token, ApiRequests.Json(ItemsBody("HIS", authorId)));
        var itemsData = await ApiRequests.AssertSuccessEnvelopeAsync(items, HttpStatusCode.OK);
        using var attachment = await SendAsync(cli, HttpMethod.Post, $"/v1/items/{Project}-1/history/attachments", token, Upload(authorId, "HIS-a-1", "截圖.png", "圖片內容"));
        var attachmentData = await ApiRequests.AssertSuccessEnvelopeAsync(attachment, HttpStatusCode.Created);
        var attachmentId = attachmentData!["id"]!.GetValue<Guid>();
        using var comments = await SendAsync(cli, HttpMethod.Post, $"/v1/projects/{Project}/history/comments", token, ApiRequests.Json(CommentsBody("HIS", authorId, attachmentId)));
        var commentsData = await ApiRequests.AssertSuccessEnvelopeAsync(comments, HttpStatusCode.OK);
        using var lookup = await SendAsync(cli, HttpMethod.Post, "/v1/history/lookup", token, ApiRequests.Json(new { source = "monday", kind = "comment", externalIds = new[] { "HIS-u-1", "nope" } }));
        var lookupData = await ApiRequests.AssertSuccessEnvelopeAsync(lookup, HttpStatusCode.OK);
        var activity = await RealStackSteps.ActivityAsync(owner.Client, $"{Project}-1");
        var item = await RealStackSteps.GetAsync(owner.Client, $"/v1/items/{Project}-1");
        var handled = await Factory.RunNotificationsOnceAsync();

        // Assert
        var entries = activity["items"]!.AsArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(member.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(itemsData!["created"]!.AsArray().Select(c => c!["itemKey"]!.GetValue<string>()), Is.EqualTo(new[] { "HIS-1", "HIS-2" }));
            Assert.That(attachmentData["uploadedBy"]!["displayName"]!.GetValue<string>(), Is.EqualTo("Author Example"));
            Assert.That(commentsData!["created"]!.AsArray(), Has.Count.EqualTo(2));
            Assert.That(lookupData!.AsArray().Select(r => r!["externalId"]!.GetValue<string>()), Is.EqualTo(new[] { "HIS-u-1" }));
            Assert.That(lookupData[0]!["itemKey"]!.GetValue<string>(), Is.EqualTo("HIS-1"));
            Assert.That(RealStackSteps.KindsOf(entries), Is.EqualTo(new[] { "created", "attachment_added", "commented", "commented" }));
            Assert.That(entries.Select(e => e!["importedFrom"]!.GetValue<string>()), Is.All.EqualTo("monday"));
            Assert.That(entries.Select(e => e!["actor"]!["displayName"]!.GetValue<string>()), Is.All.EqualTo("Author Example"));
            Assert.That(entries[0]!["at"]!.GetValue<DateTimeOffset>(), Is.EqualTo(new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero)));
            Assert.That(entries[2]!["comment"]!.GetValue<string>(), Does.Contain($"attachment:{attachmentId}"));
            Assert.That(entries[3]!["replyToId"]!.GetValue<Guid>(), Is.EqualTo(entries[2]!["id"]!.GetValue<Guid>()));
            Assert.That(item["state"]!.GetValue<string>(), Is.EqualTo("done"));
            Assert.That(handled, Is.GreaterThanOrEqualTo(5), "歷史事件（兩個項目、一個附件、兩則留言）都交給通知處理了");
            Assert.That(await UnreadAsync(owner), Is.EqualTo(unreadBefore), "歷史不產生通知");
        }
    }

    [Test]
    public async Task Rerun_SameItemsCommentsAndAttachment_SkipsEverythingAndKeepsTheSameIds()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "HIR", "simple_tasks");
        var authorId = (await RealStackSteps.CreateMemberAsync(owner.Client, "Author Example", RealStackSteps.UniqueEmail("history-rerun")))["personId"]!.GetValue<Guid>();
        await RealStackSteps.SetMemberAsync(owner.Client, "HIR", authorId, "editor");
        var token = await CreateTokenAsync(owner, "items:read", "items:write");
        using var cli = Factory.CreateClient();
        using var firstItems = await SendAsync(cli, HttpMethod.Post, "/v1/projects/HIR/history/items", token, ApiRequests.Json(ItemsBody("HIR", authorId)));
        await ApiRequests.AssertSuccessEnvelopeAsync(firstItems, HttpStatusCode.OK);
        using var firstAttachment = await SendAsync(cli, HttpMethod.Post, "/v1/items/HIR-1/history/attachments", token, Upload(authorId, "HIR-a-1", "截圖.png", "圖片內容"));
        var attachmentId = (await ApiRequests.AssertSuccessEnvelopeAsync(firstAttachment, HttpStatusCode.Created))!["id"]!.GetValue<Guid>();
        using var firstComments = await SendAsync(cli, HttpMethod.Post, "/v1/projects/HIR/history/comments", token, ApiRequests.Json(CommentsBody("HIR", authorId, attachmentId)));
        var created = (await ApiRequests.AssertSuccessEnvelopeAsync(firstComments, HttpStatusCode.OK))!["created"]!.ToJsonString();
        var filesBefore = StoredFiles().Length;

        // Act
        using var items = await SendAsync(cli, HttpMethod.Post, "/v1/projects/HIR/history/items", token, ApiRequests.Json(ItemsBody("HIR", authorId)));
        var itemsData = await ApiRequests.AssertSuccessEnvelopeAsync(items, HttpStatusCode.OK);
        using var attachment = await SendAsync(cli, HttpMethod.Post, "/v1/items/HIR-1/history/attachments", token, Upload(authorId, "HIR-a-1", "截圖.png", "圖片內容"));
        var attachmentData = await ApiRequests.AssertSuccessEnvelopeAsync(attachment, HttpStatusCode.OK);
        using var comments = await SendAsync(cli, HttpMethod.Post, "/v1/projects/HIR/history/comments", token, ApiRequests.Json(CommentsBody("HIR", authorId, attachmentId)));
        var commentsData = await ApiRequests.AssertSuccessEnvelopeAsync(comments, HttpStatusCode.OK);
        var listed = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/HIR/items");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(itemsData!["created"]!.AsArray(), Is.Empty);
            Assert.That(itemsData["skipped"]!.AsArray().Select(c => c!["itemKey"]!.GetValue<string>()), Is.EqualTo(new[] { "HIR-1", "HIR-2" }));
            Assert.That(attachmentData!["id"]!.GetValue<Guid>(), Is.EqualTo(attachmentId));
            Assert.That(commentsData!["created"]!.AsArray(), Is.Empty);
            Assert.That(commentsData["skipped"]!.ToJsonString(), Is.EqualTo(created));
            Assert.That(RealStackSteps.KeysOf(listed["items"]), Is.EquivalentTo(new[] { "HIR-1", "HIR-2" }));
            Assert.That(StoredFiles(), Has.Length.EqualTo(filesBefore), "重跑不重傳檔案");
        }
    }

    [Test]
    public async Task Scopes_WriteScopeImportsAndReadScopeOnlyLooksUp_AndAnEditorCannotImport()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "HIP", "simple_tasks");
        using var editor = await AddMemberAsync(owner, "HIP", "editor", "Editor Example", RealStackSteps.UniqueEmail("history-editor"));
        var read = await CreateTokenAsync(owner, "items:read");
        var write = await CreateTokenAsync(owner, "items:write");
        using var cli = Factory.CreateClient();
        var body = ItemsBody("HIP", editor.PersonId);

        // Act
        using var importByRead = await SendAsync(cli, HttpMethod.Post, "/v1/projects/HIP/history/items", read, ApiRequests.Json(body));
        using var lookupByWrite = await SendAsync(cli, HttpMethod.Post, "/v1/history/lookup", write, ApiRequests.Json(new { source = "monday", kind = "item", externalIds = new[] { "HIP-m-1" } }));
        using var importByEditor = await editor.Client.PostAsync("/v1/projects/HIP/history/items", ApiRequests.Json(body));
        using var importByWrite = await SendAsync(cli, HttpMethod.Post, "/v1/projects/HIP/history/items", write, ApiRequests.Json(body));
        using var lookupByRead = await SendAsync(cli, HttpMethod.Post, "/v1/history/lookup", read, ApiRequests.Json(new { source = "monday", kind = "item", externalIds = new[] { "HIP-m-1" } }));
        var lookupData = await ApiRequests.AssertSuccessEnvelopeAsync(lookupByRead, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(importByRead.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(lookupByWrite.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(importByWrite.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(lookupData!.AsArray(), Has.Count.EqualTo(1));
        }

        await ApiRequests.AssertErrorEnvelopeAsync(importByEditor, HttpStatusCode.Forbidden, WorkErrorCodes.Forbidden);
    }

    [Test]
    public async Task ArchivedProject_RefusesMoreHistory()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "HIA", "simple_tasks");
        var authorId = (await RealStackSteps.CreateMemberAsync(owner.Client, "Author Example", RealStackSteps.UniqueEmail("history-archive")))["personId"]!.GetValue<Guid>();
        await RealStackSteps.SetMemberAsync(owner.Client, "HIA", authorId, "editor");
        var token = await CreateTokenAsync(owner, "items:read", "items:write", "blueprint:write");
        using var cli = Factory.CreateClient();
        using var imported = await SendAsync(cli, HttpMethod.Post, "/v1/projects/HIA/history/items", token, ApiRequests.Json(ItemsBody("HIA", authorId)));
        await ApiRequests.AssertSuccessEnvelopeAsync(imported, HttpStatusCode.OK);
        using var archived = await SendAsync(cli, HttpMethod.Post, "/v1/projects/HIA/archive", token);
        await ApiRequests.AssertSuccessEnvelopeAsync(archived, HttpStatusCode.OK);

        // Act
        using var again = await SendAsync(cli, HttpMethod.Post, "/v1/projects/HIA/history/items", token, ApiRequests.Json(ItemsBody("HIA", authorId)));

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(again, HttpStatusCode.Conflict, WorkErrorCodes.ProjectArchived);
    }

    // 外部編號在整個組織裡唯一，同一個資料庫裡各個測試的專案要用各自的前綴，不然會被當成已經匯入過。
    private static ItemsRequestBody ItemsBody(string prefix, Guid authorId) => new("monday", false,
    [
        new ItemRow($"{prefix}-m-1", "task", "舊的任務", authorId, "2026-03-05T00:00:00Z", "done"),
        new ItemRow($"{prefix}-m-2", "task", "另一個", authorId, "2026-03-06T00:00:00Z", null),
    ]);

    private static object CommentsBody(string prefix, Guid authorId, Guid attachmentId) => new
    {
        source = "monday",
        comments = new object[]
        {
            new { externalId = $"{prefix}-u-1", itemExternalId = $"{prefix}-m-1", authorId, at = "2026-03-07T01:00:00Z", body = $"請看 ![截圖.png](attachment:{attachmentId})" },
            new { externalId = $"{prefix}-r-1", itemExternalId = $"{prefix}-m-1", authorId, at = "2026-03-07T02:00:00Z", body = "收到", replyToExternalId = $"{prefix}-u-1" },
        },
    };

    private async Task<int> UnreadAsync(SignedInClient who) =>
        (await RealStackSteps.GetAsync(who.Client, "/v1/notifications/unread-count"))["count"]!.GetValue<int>();

    private string[] StoredFiles() => [.. Directory.EnumerateFiles(Factory.AttachmentDirectory, "*", SearchOption.AllDirectories)];

    private async Task<string> CreateTokenAsync(SignedInClient owner, params string[] scopes)
    {
        using var response = await owner.Client.PostAsync(
            "/v1/account/tokens", ApiRequests.Json(new { name = "歷史匯入測試", scopes, expiresInDays = 30 }));
        var created = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);
        return created!["secret"]!.GetValue<string>();
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string route, string secret, HttpContent? content = null)
    {
        var request = AccessTokenAuthenticationTests.Bearer(method, route, secret);
        request.Content = content;
        return client.SendAsync(request);
    }

    private static MultipartFormDataContent Upload(Guid uploadedBy, string externalId, string fileName, string text)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent("monday"), "source");
        content.Add(new StringContent(externalId), "externalId");
        content.Add(new StringContent(uploadedBy.ToString("D")), "uploadedBy");
        content.Add(new StringContent("2026-03-05T10:00:00+08:00"), "uploadedAt");
        var part = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(text));
        part.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        content.Add(part, "file", fileName);
        return content;
    }

    private sealed record ItemRow(string externalId, string type, string title, Guid createdBy, string createdAt, string? state);

    private sealed record ItemsRequestBody(string source, bool dryRun, IReadOnlyList<ItemRow> rows);
}
