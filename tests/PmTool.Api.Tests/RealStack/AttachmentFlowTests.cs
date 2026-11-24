using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 附件的端到端：真的工作引擎、真的資料庫、真的（local）儲存空間。上傳、列出、下載、刪除，
/// 權限與封存的專案、超過上限、權杖的範圍，以及附件的事件不會通知任何人。
/// 整個類別只登入三次（擁有者、編輯者、檢視者）：登入有限流。
/// </summary>
[TestFixture]
public sealed class AttachmentFlowTests : RealStackTestBase
{
    private const string Project = "ATT";

    private SignedInClient _owner = null!;
    private SignedInClient _editor = null!;
    private SignedInClient _viewer = null!;

    [OneTimeSetUp]
    public async Task SignInEveryoneAsync()
    {
        _owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(_owner.Client, Project, "simple_tasks");
        _editor = await AddMemberAsync(_owner, Project, "editor", "Editor Example", RealStackSteps.UniqueEmail("editor"));
        _viewer = await AddMemberAsync(_owner, Project, "viewer", "Viewer Example", RealStackSteps.UniqueEmail("viewer"));
    }

    [OneTimeTearDown]
    public void DisposeClients()
    {
        _owner?.Dispose();
        _editor?.Dispose();
        _viewer?.Dispose();
    }

    [Test]
    public async Task UploadListDownloadDelete_ARoundTripWithAChineseNameAndTheRealHeaders()
    {
        // Arrange
        var itemKey = await NewItemAsync();
        var bytes = Enumerable.Range(0, 600_000).Select(i => (byte)(i % 253)).ToArray();

        // Act
        using var uploaded = await _editor.Client.PostAsync($"/v1/items/{itemKey}/attachments", Upload("報價單 (v2).pdf", bytes, "application/pdf; charset=binary"));
        var attachment = await ApiRequests.AssertSuccessEnvelopeAsync(uploaded, HttpStatusCode.Created);
        var id = attachment!["id"]!.GetValue<Guid>();
        var listed = await RealStackSteps.GetAsync(_viewer.Client, $"/v1/items/{itemKey}/attachments");
        using var download = await _viewer.Client.GetAsync($"/v1/attachments/{id}/content");
        var downloaded = await download.Content.ReadAsByteArrayAsync();
        using var deleted = await _editor.Client.DeleteAsync($"/v1/attachments/{id}");
        var afterDelete = await RealStackSteps.GetAsync(_viewer.Client, $"/v1/items/{itemKey}/attachments");
        using var gone = await _viewer.Client.GetAsync($"/v1/attachments/{id}/content");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(attachment["itemKey"]?.GetValue<string>(), Is.EqualTo(itemKey));
            Assert.That(attachment["fileName"]?.GetValue<string>(), Is.EqualTo("報價單 (v2).pdf"));
            Assert.That(attachment["contentType"]?.GetValue<string>(), Is.EqualTo("application/pdf"));
            Assert.That(attachment["sizeBytes"]?.GetValue<long>(), Is.EqualTo(bytes.Length));
            Assert.That(attachment["sha256"]?.GetValue<string>(), Is.EqualTo(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))));
            Assert.That(attachment["uploadedBy"]?["displayName"]?.GetValue<string>(), Is.EqualTo("Editor Example"));
            Assert.That(listed.AsArray().Select(a => a!["id"]!.GetValue<Guid>()), Is.EqualTo(new[] { id }));
            Assert.That(download.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(downloaded, Is.EqualTo(bytes));
            Assert.That(download.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/octet-stream"), "PDF 也一律下載");
            Assert.That(download.Content.Headers.NonValidated["Content-Disposition"].ToString(), Does.StartWith("attachment; filename=").And.Contain("filename*=UTF-8''%E5%A0%B1%E5%83%B9"));
            Assert.That(download.Headers.NonValidated["X-Content-Type-Options"].ToString(), Is.EqualTo("nosniff"));
            Assert.That(download.Headers.NonValidated["Content-Security-Policy"].ToString(), Is.EqualTo("default-src 'none'; sandbox"));
            Assert.That(download.Content.Headers.ContentLength, Is.EqualTo(bytes.Length));
            Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(afterDelete.AsArray(), Is.Empty);
        }

        await ApiRequests.AssertErrorEnvelopeAsync(gone, HttpStatusCode.NotFound, WorkErrorCodes.AttachmentNotFound);
        Assert.That(StoredFiles(), Is.Empty.Or.None.Matches<string>(path => path.EndsWith(id.ToString("D"), StringComparison.Ordinal)));
    }

    [Test]
    public async Task Download_APngIsServedInline()
    {
        // Arrange
        var itemKey = await NewItemAsync();
        using var uploaded = await _editor.Client.PostAsync($"/v1/items/{itemKey}/attachments", Upload("截圖.png", [0x89, 0x50, 0x4E, 0x47], "image/png"));
        var id = (await ApiRequests.AssertSuccessEnvelopeAsync(uploaded, HttpStatusCode.Created))!["id"]!.GetValue<Guid>();

        // Act
        using var download = await _viewer.Client.GetAsync($"/v1/attachments/{id}/content");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(download.Content.Headers.ContentType?.MediaType, Is.EqualTo("image/png"));
            Assert.That(download.Content.Headers.NonValidated["Content-Disposition"].ToString(), Does.StartWith("inline;"));
        }
    }

    [Test]
    public async Task Upload_ActivityAndEvents_AreRecordedAndNobodyIsNotified()
    {
        // Arrange：擁有者建立項目（自己是關注的人），編輯者上傳再刪除。
        var itemKey = await NewItemAsync();
        await Factory.RunNotificationsOnceAsync();
        var unreadBefore = await UnreadAsync(_owner);

        // Act
        using var uploaded = await _editor.Client.PostAsync($"/v1/items/{itemKey}/attachments", Upload("規格.pdf", [1, 2, 3], "application/pdf"));
        var id = (await ApiRequests.AssertSuccessEnvelopeAsync(uploaded, HttpStatusCode.Created))!["id"]!.GetValue<Guid>();
        using var deleted = await _editor.Client.DeleteAsync($"/v1/attachments/{id}");
        var handled = await Factory.RunNotificationsOnceAsync();
        var activity = await RealStackSteps.ActivityAsync(_owner.Client, itemKey);

        // Assert
        var entries = activity["items"]!.AsArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(RealStackSteps.KindsOf(entries), Is.EqualTo(new[] { "created", "attachment_added", "attachment_removed" }));
            Assert.That(entries[1]!["changes"]![0]!["field"]!.GetValue<string>(), Is.EqualTo("attachment"));
            Assert.That(entries[1]!["changes"]![0]!["from"], Is.Null);
            Assert.That(entries[1]!["changes"]![0]!["to"]!.GetValue<string>(), Is.EqualTo("規格.pdf"));
            Assert.That(entries[2]!["changes"]![0]!["from"]!.GetValue<string>(), Is.EqualTo("規格.pdf"));
            Assert.That(entries[1]!["actor"]!["displayName"]!.GetValue<string>(), Is.EqualTo("Editor Example"));
            Assert.That(handled, Is.EqualTo(2), "兩個附件事件都交給通知處理了");
            Assert.That(await UnreadAsync(_owner), Is.EqualTo(unreadBefore));
        }
    }

    [Test]
    public async Task Upload_OverTheFileLimit_Returns413AndLeavesNothingBehind()
    {
        // Arrange
        var itemKey = await NewItemAsync();
        var filesBefore = StoredFiles().Length;
        using var content = new MultipartFormDataContent();
        content.Add(new ZeroContent(AttachmentRules.MaxBytes + 1), "file", "太大.zip");

        // Act
        using var response = await _editor.Client.PostAsync($"/v1/items/{itemKey}/attachments", content);
        var listed = await RealStackSteps.GetAsync(_editor.Client, $"/v1/items/{itemKey}/attachments");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.RequestEntityTooLarge, WorkErrorCodes.AttachmentTooLarge);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(listed.AsArray(), Is.Empty);
            Assert.That(StoredFiles(), Has.Length.EqualTo(filesBefore));
        }
    }

    [Test]
    public async Task Upload_ABadFileName_Returns422WithTheFieldPath()
    {
        // Arrange
        var itemKey = await NewItemAsync();
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent([1]), "file", "..");

        // Act
        using var response = await _editor.Client.PostAsync($"/v1/items/{itemKey}/attachments", content);

        // Assert
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.AttachmentNameInvalid);
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{WorkErrorCodes.AttachmentNameInvalid}|fileName" }));
    }

    [Test]
    public async Task Viewer_CanSeeAndDownloadButCannotUploadOrDelete()
    {
        // Arrange
        var itemKey = await NewItemAsync();
        using var uploaded = await _editor.Client.PostAsync($"/v1/items/{itemKey}/attachments", Upload("a.txt", [1], "text/plain"));
        var id = (await ApiRequests.AssertSuccessEnvelopeAsync(uploaded, HttpStatusCode.Created))!["id"]!.GetValue<Guid>();
        var filesBefore = StoredFiles().Length;

        // Act
        using var upload = await _viewer.Client.PostAsync($"/v1/items/{itemKey}/attachments", Upload("b.txt", [2], "text/plain"));
        using var delete = await _viewer.Client.DeleteAsync($"/v1/attachments/{id}");
        using var download = await _viewer.Client.GetAsync($"/v1/attachments/{id}/content");

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(upload, HttpStatusCode.Forbidden, WorkErrorCodes.Forbidden);
        await ApiRequests.AssertFailureEnvelopeAsync(delete, HttpStatusCode.Forbidden, WorkErrorCodes.Forbidden);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(download.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(StoredFiles(), Has.Length.EqualTo(filesBefore), "被擋下的上傳沒有留下檔案");
        }
    }

    [Test]
    public async Task NonMember_GetsNotFoundForTheListTheUploadTheDownloadAndTheDelete()
    {
        // Arrange
        var itemKey = await NewItemAsync();
        using var uploaded = await _editor.Client.PostAsync($"/v1/items/{itemKey}/attachments", Upload("a.txt", [1], "text/plain"));
        var id = (await ApiRequests.AssertSuccessEnvelopeAsync(uploaded, HttpStatusCode.Created))!["id"]!.GetValue<Guid>();
        var outsiderEmail = RealStackSteps.UniqueEmail("outsider");
        await RealStackSteps.CreateMemberAsync(_owner.Client, "Outsider Example", outsiderEmail);
        using var outsider = await SignInAsAsync(outsiderEmail, RealStackSteps.MemberPassword);

        // Act
        using var list = await outsider.Client.GetAsync($"/v1/items/{itemKey}/attachments");
        using var upload = await outsider.Client.PostAsync($"/v1/items/{itemKey}/attachments", Upload("b.txt", [2], "text/plain"));
        using var download = await outsider.Client.GetAsync($"/v1/attachments/{id}/content");
        using var delete = await outsider.Client.DeleteAsync($"/v1/attachments/{id}");

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(list, HttpStatusCode.NotFound, WorkErrorCodes.ItemNotFound);
        await ApiRequests.AssertFailureEnvelopeAsync(upload, HttpStatusCode.NotFound, WorkErrorCodes.ItemNotFound);
        await ApiRequests.AssertFailureEnvelopeAsync(download, HttpStatusCode.NotFound, WorkErrorCodes.AttachmentNotFound);
        await ApiRequests.AssertFailureEnvelopeAsync(delete, HttpStatusCode.NotFound, WorkErrorCodes.AttachmentNotFound);
    }

    [Test]
    public async Task ArchivedProject_StillListsAndDownloadsButRefusesUploadAndDelete()
    {
        // Arrange：另開一個專案，封存不影響其他測試。
        await RealStackSteps.CreateProjectAsync(_owner.Client, "ARCH", "simple_tasks");
        var item = await RealStackSteps.CreateItemAsync(_owner.Client, "ARCH", new { type = "task", title = "封存前" });
        var itemKey = item["key"]!.GetValue<string>();
        using var uploaded = await _owner.Client.PostAsync($"/v1/items/{itemKey}/attachments", Upload("a.txt", [1], "text/plain"));
        var id = (await ApiRequests.AssertSuccessEnvelopeAsync(uploaded, HttpStatusCode.Created))!["id"]!.GetValue<Guid>();
        using var archive = await _owner.Client.PostAsync("/v1/projects/ARCH/archive", null);
        Assert.That(archive.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Act
        var listed = await RealStackSteps.GetAsync(_owner.Client, $"/v1/items/{itemKey}/attachments");
        using var download = await _owner.Client.GetAsync($"/v1/attachments/{id}/content");
        using var upload = await _owner.Client.PostAsync($"/v1/items/{itemKey}/attachments", Upload("b.txt", [2], "text/plain"));
        using var delete = await _owner.Client.DeleteAsync($"/v1/attachments/{id}");

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(upload, HttpStatusCode.Conflict, WorkErrorCodes.ProjectArchived);
        await ApiRequests.AssertFailureEnvelopeAsync(delete, HttpStatusCode.Conflict, WorkErrorCodes.ProjectArchived);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(listed.AsArray(), Has.Count.EqualTo(1));
            Assert.That(download.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }

    [Test]
    public async Task Token_ReadScopeListsAndDownloadsButOnlyWriteScopeUploadsAndDeletes()
    {
        // Arrange
        var itemKey = await NewItemAsync();
        var read = await CreateTokenAsync("items:read");
        var write = await CreateTokenAsync("items:write");
        using var cli = Factory.CreateClient();
        using var uploadedByWrite = await SendAsync(cli, HttpMethod.Post, $"/v1/items/{itemKey}/attachments", write, Upload("cli.txt", "從 CLI 上傳"u8.ToArray(), "text/plain"));
        var id = (await ApiRequests.AssertSuccessEnvelopeAsync(uploadedByWrite, HttpStatusCode.Created))!["id"]!.GetValue<Guid>();

        // Act
        using var uploadByRead = await SendAsync(cli, HttpMethod.Post, $"/v1/items/{itemKey}/attachments", read, Upload("x.txt", [1], "text/plain"));
        using var listByRead = await SendAsync(cli, HttpMethod.Get, $"/v1/items/{itemKey}/attachments", read);
        using var downloadByRead = await SendAsync(cli, HttpMethod.Get, $"/v1/attachments/{id}/content", read);
        using var deleteByRead = await SendAsync(cli, HttpMethod.Delete, $"/v1/attachments/{id}", read);
        using var deleteByWrite = await SendAsync(cli, HttpMethod.Delete, $"/v1/attachments/{id}", write);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(uploadByRead.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(listByRead.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await downloadByRead.Content.ReadAsStringAsync(), Is.EqualTo("從 CLI 上傳"));
            Assert.That(deleteByRead.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(deleteByWrite.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }

    private async Task<string> NewItemAsync()
    {
        var item = await RealStackSteps.CreateItemAsync(_owner.Client, Project, new { type = "task", title = "有附件的項目" });
        return item["key"]!.GetValue<string>();
    }

    private async Task<int> UnreadAsync(SignedInClient who) =>
        (await RealStackSteps.GetAsync(who.Client, "/v1/notifications/unread-count"))["count"]!.GetValue<int>();

    private string[] StoredFiles() => [.. Directory.EnumerateFiles(Factory.AttachmentDirectory, "*", SearchOption.AllDirectories)];

    private async Task<string> CreateTokenAsync(params string[] scopes)
    {
        using var response = await _owner.Client.PostAsync(
            "/v1/account/tokens", ApiRequests.Json(new { name = "附件測試", scopes, expiresInDays = 30 }));
        var created = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);
        return created!["secret"]!.GetValue<string>();
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string route, string secret, HttpContent? content = null)
    {
        var request = AccessTokenAuthenticationTests.Bearer(method, route, secret);
        request.Content = content;
        return client.SendAsync(request);
    }

    private static MultipartFormDataContent Upload(string fileName, byte[] bytes, string contentType)
    {
        var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        content.Add(part, "file", fileName);
        return content;
    }

    /// <summary>全是 0 的內容，不用真的配置記憶體。</summary>
    private sealed class ZeroContent(long length) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var buffer = new byte[64 * 1024];
            for (var sent = 0L; sent < length; sent += buffer.Length)
            {
                await stream.WriteAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length - sent)));
            }
        }

        protected override bool TryComputeLength(out long computed)
        {
            computed = length;
            return true;
        }
    }
}
