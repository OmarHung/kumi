using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;
using PmTool.Knowledge;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 知識庫的端到端（組織空間）：真的身分模組、真的工作引擎、真的資料庫與儲存空間。
/// 組織空間 → 新增頁面 → 設限制 → 一般成員看不到 → 組織管理員看得到；附件跟著頁面的限制；修改的衝突與垃圾桶。
/// 整個類別只登入三次（擁有者、名單內的成員、名單外的成員）：登入有限流。
/// </summary>
[TestFixture]
public sealed class KnowledgeOrgSpaceFlowTests : RealStackTestBase
{
    private const string SpaceKey = "KB";

    private SignedInClient _owner = null!;
    private SignedInClient _insider = null!;
    private SignedInClient _outsider = null!;

    [OneTimeSetUp]
    public async Task SetUpAsync()
    {
        _owner = await SignInAsOwnerAsync();
        _insider = await SignInMemberAsync("insider");
        _outsider = await SignInMemberAsync("outsider");
        using var created = await _owner.Client.PostAsync(
            "/v1/knowledge/spaces", ApiRequests.Json(new { key = SpaceKey, name = "知識庫", description = "範例" }));
        await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created);
    }

    [OneTimeTearDown]
    public void DisposeClients()
    {
        _owner?.Dispose();
        _insider?.Dispose();
        _outsider?.Dispose();
    }

    private async Task<SignedInClient> SignInMemberAsync(string name)
    {
        var email = RealStackSteps.UniqueEmail(name);
        await RealStackSteps.CreateMemberAsync(_owner.Client, $"{name} Example", email);
        return await SignInAsAsync(email, RealStackSteps.MemberPassword);
    }

    private static async Task<JsonNode> CreatePageAsync(HttpClient client, string title, int? parent = null, string? body = null)
    {
        using var response = await client.PostAsync(
            $"/v1/knowledge/spaces/{SpaceKey}/pages", ApiRequests.Json(new { parentNumber = parent, title, body }));
        return (await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created))!;
    }

    private static string[] TitlesOf(JsonNode tree) => [.. tree["pages"]!.AsArray().Select(p => p!["title"]!.GetValue<string>())];

    private static MultipartFormDataContent Upload(string fileName, byte[] bytes, string contentType)
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return new MultipartFormDataContent { { part, "file", fileName } };
    }

    [Test]
    public async Task OrganizationSpace_MembersCanReadAndWrite_AndTheSpaceListShowsIt()
    {
        // Arrange
        var page = await CreatePageAsync(_insider.Client, "歡迎", body: "## 嗨");

        // Act
        var list = await RealStackSteps.GetAsync(_outsider.Client, "/v1/knowledge/spaces");
        var read = await RealStackSteps.GetAsync(_outsider.Client, $"/v1/knowledge/spaces/{SpaceKey.ToLowerInvariant()}/pages/{page["number"]}");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(list.AsArray().Select(s => s!["key"]!.GetValue<string>()), Does.Contain(SpaceKey));
            Assert.That(read["body"]?.GetValue<string>(), Is.EqualTo("## 嗨"));
            Assert.That(read["createdBy"]?["personId"]?.GetValue<Guid>(), Is.EqualTo(_insider.PersonId));
            Assert.That(read["createdBy"]?["displayName"]?.GetValue<string>(), Is.EqualTo("insider Example"));
            Assert.That(read["myPermissions"]?["canEdit"]?.GetValue<bool>(), Is.True);
            Assert.That(read["space"]?["project"], Is.Null);
        }
    }

    [Test]
    public async Task RestrictedPage_IsHiddenFromMembersOutsideTheListAndVisibleToTheListAndToAdmins()
    {
        // Arrange
        var secret = await CreatePageAsync(_owner.Client, "機密");
        var child = await CreatePageAsync(_owner.Client, "機密的子頁面", parent: secret["number"]!.GetValue<int>());
        var number = secret["number"]!.GetValue<int>();
        var route = $"/v1/knowledge/spaces/{SpaceKey}/pages/{number}";

        // Act
        using var set = await _owner.Client.PutAsync($"{route}/restriction", ApiRequests.Json(new { people = new[] { _insider.PersonId } }));
        var restriction = await ApiRequests.AssertSuccessEnvelopeAsync(set, HttpStatusCode.OK);
        var outsiderTree = await RealStackSteps.GetAsync(_outsider.Client, $"/v1/knowledge/spaces/{SpaceKey}/tree");
        var insiderTree = await RealStackSteps.GetAsync(_insider.Client, $"/v1/knowledge/spaces/{SpaceKey}/tree");
        var ownerTree = await RealStackSteps.GetAsync(_owner.Client, $"/v1/knowledge/spaces/{SpaceKey}/tree");
        using var outsiderPage = await _outsider.Client.GetAsync(route);
        using var outsiderChild = await _outsider.Client.GetAsync($"/v1/knowledge/spaces/{SpaceKey}/pages/{child["number"]}");
        using var outsiderWrite = await _outsider.Client.PatchAsync(route, ApiRequests.Json(new { title = "改", expectedRevision = 1 }));
        var insiderPage = await RealStackSteps.GetAsync(_insider.Client, route);
        var insiderChild = await RealStackSteps.GetAsync(_insider.Client, $"/v1/knowledge/spaces/{SpaceKey}/pages/{child["number"]}");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(restriction?["people"]?.AsArray().Select(p => p!["personId"]!.GetValue<Guid>()), Does.Contain(_insider.PersonId));
            Assert.That(restriction?["people"]?.AsArray().Select(p => p!["personId"]!.GetValue<Guid>()), Does.Contain(_owner.PersonId));
            Assert.That(TitlesOf(outsiderTree), Does.Not.Contain("機密").And.Not.Contain("機密的子頁面"));
            Assert.That(TitlesOf(insiderTree), Does.Contain("機密").And.Contain("機密的子頁面"));
            Assert.That(TitlesOf(ownerTree), Does.Contain("機密").And.Contain("機密的子頁面"));
            Assert.That(insiderPage["isRestricted"]?.GetValue<bool>(), Is.True);
            Assert.That(insiderChild["restrictedAncestors"]?[0]?["number"]?.GetValue<int>(), Is.EqualTo(number));
        }

        await ApiRequests.AssertErrorEnvelopeAsync(outsiderPage, HttpStatusCode.NotFound, KnowledgeErrorCodes.PageNotFound);
        await ApiRequests.AssertErrorEnvelopeAsync(outsiderChild, HttpStatusCode.NotFound, KnowledgeErrorCodes.PageNotFound);
        await ApiRequests.AssertErrorEnvelopeAsync(outsiderWrite, HttpStatusCode.NotFound, KnowledgeErrorCodes.PageNotFound);
    }

    [Test]
    public async Task AttachmentsOnARestrictedPage_AreNotFoundForPeopleOutsideTheListAndDownloadableByTheRest()
    {
        // Arrange
        var page = await CreatePageAsync(_owner.Client, "附件頁");
        var route = $"/v1/knowledge/spaces/{SpaceKey}/pages/{page["number"]}";
        using var set = await _owner.Client.PutAsync($"{route}/restriction", ApiRequests.Json(new { people = new[] { _insider.PersonId } }));
        await ApiRequests.AssertSuccessEnvelopeAsync(set, HttpStatusCode.OK);
        var bytes = Enumerable.Range(0, 300_000).Select(i => (byte)(i % 251)).ToArray();

        // Act
        using var uploaded = await _insider.Client.PostAsync($"{route}/attachments", Upload("截圖 (1).png", bytes, "image/png"));
        var attachment = await ApiRequests.AssertSuccessEnvelopeAsync(uploaded, HttpStatusCode.Created);
        var id = attachment!["id"]!.GetValue<Guid>();
        using var outsiderUpload = await _outsider.Client.PostAsync($"{route}/attachments", Upload("x.txt", [1], "text/plain"));
        using var outsiderList = await _outsider.Client.GetAsync($"{route}/attachments");
        using var outsiderDownload = await _outsider.Client.GetAsync($"/v1/knowledge/attachments/{id}/content");
        var insiderList = await RealStackSteps.GetAsync(_insider.Client, $"{route}/attachments");
        using var insiderDownload = await _insider.Client.GetAsync($"/v1/knowledge/attachments/{id}/content");
        using var ownerDownload = await _owner.Client.GetAsync($"/v1/knowledge/attachments/{id}/content");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(attachment["fileName"]?.GetValue<string>(), Is.EqualTo("截圖 (1).png"));
            Assert.That(attachment["contentType"]?.GetValue<string>(), Is.EqualTo("image/png"));
            Assert.That(attachment["sizeBytes"]?.GetValue<long>(), Is.EqualTo(bytes.Length));
            Assert.That(attachment["pageId"]?.GetValue<Guid>(), Is.EqualTo(page["id"]?.GetValue<Guid>()));
            Assert.That(insiderList.AsArray(), Has.Count.EqualTo(1));
            Assert.That(insiderDownload.Content.Headers.ContentType?.MediaType, Is.EqualTo("image/png"));
            Assert.That(insiderDownload.Content.Headers.ContentDisposition?.DispositionType, Is.EqualTo("inline"));
            Assert.That(await insiderDownload.Content.ReadAsByteArrayAsync(), Is.EqualTo(bytes));
            Assert.That(ownerDownload.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        await ApiRequests.AssertErrorEnvelopeAsync(outsiderUpload, HttpStatusCode.NotFound, KnowledgeErrorCodes.PageNotFound);
        await ApiRequests.AssertErrorEnvelopeAsync(outsiderList, HttpStatusCode.NotFound, KnowledgeErrorCodes.PageNotFound);
        await ApiRequests.AssertErrorEnvelopeAsync(outsiderDownload, HttpStatusCode.NotFound, KnowledgeErrorCodes.AttachmentNotFound);
    }

    [Test]
    public async Task Download_ANonImageAttachment_IsServedAsAnAttachment()
    {
        // Arrange
        var page = await CreatePageAsync(_owner.Client, "文件頁");
        var route = $"/v1/knowledge/spaces/{SpaceKey}/pages/{page["number"]}";

        // Act
        using var uploaded = await _owner.Client.PostAsync($"{route}/attachments", Upload("說明.html", "<script>1</script>"u8.ToArray(), "text/html"));
        var attachment = await ApiRequests.AssertSuccessEnvelopeAsync(uploaded, HttpStatusCode.Created);
        using var download = await _outsider.Client.GetAsync($"/v1/knowledge/attachments/{attachment!["id"]}/content");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(download.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/octet-stream"));
            Assert.That(download.Content.Headers.ContentDisposition?.DispositionType, Is.EqualTo("attachment"));
            Assert.That(download.Headers.GetValues("X-Content-Type-Options"), Is.EqualTo(new[] { "nosniff" }));
        }
    }

    [Test]
    public async Task Update_TwoEditorsWithTheSameRevision_TheSecondGetsAConflictAndNothingChanges()
    {
        // Arrange
        var page = await CreatePageAsync(_owner.Client, "衝突", body: "原本");
        var route = $"/v1/knowledge/spaces/{SpaceKey}/pages/{page["number"]}";

        // Act
        using var first = await _insider.Client.PatchAsync(route, ApiRequests.Json(new { body = "甲的版本", expectedRevision = 1 }));
        var firstData = await ApiRequests.AssertSuccessEnvelopeAsync(first, HttpStatusCode.OK);
        using var second = await _outsider.Client.PatchAsync(route, ApiRequests.Json(new { body = "乙的版本", expectedRevision = 1 }));
        using var missing = await _outsider.Client.PatchAsync(route, ApiRequests.Json(new { body = "乙的版本" }));
        var current = await RealStackSteps.GetAsync(_owner.Client, route);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(second, HttpStatusCode.Conflict, KnowledgeErrorCodes.PageConflict);
        await ApiRequests.AssertFailureEnvelopeAsync(missing, HttpStatusCode.UnprocessableEntity, "knowledge.page_revision_required");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstData?["revision"]?.GetValue<long>(), Is.EqualTo(2));
            Assert.That(current["body"]?.GetValue<string>(), Is.EqualTo("甲的版本"));
            Assert.That(current["updatedBy"]?["displayName"]?.GetValue<string>(), Is.EqualTo("insider Example"));
        }
    }

    [Test]
    public async Task DeleteAndRestore_ABranchGoesToTheTrashAndComesBackToItsPlace()
    {
        // Arrange
        var parent = await CreatePageAsync(_owner.Client, "要刪的上層");
        var child = await CreatePageAsync(_owner.Client, "要刪的子頁", parent: parent["number"]!.GetValue<int>());
        var parentRoute = $"/v1/knowledge/spaces/{SpaceKey}/pages/{parent["number"]}";

        // Act
        using var deleted = await _insider.Client.DeleteAsync(parentRoute);
        var deletion = await ApiRequests.AssertSuccessEnvelopeAsync(deleted, HttpStatusCode.OK);
        var trash = await RealStackSteps.GetAsync(_outsider.Client, $"/v1/knowledge/spaces/{SpaceKey}/trash");
        using var readWhileDeleted = await _outsider.Client.GetAsync(parentRoute);
        using var restored = await _outsider.Client.PostAsync($"{parentRoute}/restore", null);
        await ApiRequests.AssertSuccessEnvelopeAsync(restored, HttpStatusCode.OK);
        var tree = await RealStackSteps.GetAsync(_owner.Client, $"/v1/knowledge/spaces/{SpaceKey}/tree");
        var childAfter = await RealStackSteps.GetAsync(_owner.Client, $"/v1/knowledge/spaces/{SpaceKey}/pages/{child["number"]}");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deletion?["pageCount"]?.GetValue<int>(), Is.EqualTo(2));
            Assert.That(trash.AsArray().Select(e => e!["page"]!["number"]!.GetValue<int>()), Does.Contain(parent["number"]!.GetValue<int>()));
            Assert.That(TitlesOf(tree), Does.Contain("要刪的上層").And.Contain("要刪的子頁"));
            Assert.That(childAfter["ancestors"]?[0]?["number"]?.GetValue<int>(), Is.EqualTo(parent["number"]!.GetValue<int>()));
        }

        await ApiRequests.AssertErrorEnvelopeAsync(readWhileDeleted, HttpStatusCode.NotFound, KnowledgeErrorCodes.PageNotFound);
    }

    [Test]
    public async Task SpaceManagement_OnlyAnOrganizationAdminCanCreateRenameAndArchive()
    {
        // Arrange、Act
        using var memberCreate = await _insider.Client.PostAsync("/v1/knowledge/spaces", ApiRequests.Json(new { key = "NOPE", name = "不行" }));
        using var memberRename = await _insider.Client.PutAsync($"/v1/knowledge/spaces/{SpaceKey}", ApiRequests.Json(new { name = "改名" }));
        using var memberArchive = await _insider.Client.PostAsync($"/v1/knowledge/spaces/{SpaceKey}/archive", null);
        using var duplicate = await _owner.Client.PostAsync("/v1/knowledge/spaces", ApiRequests.Json(new { key = SpaceKey.ToLowerInvariant(), name = "重複" }));
        using var badKey = await _owner.Client.PostAsync("/v1/knowledge/spaces", ApiRequests.Json(new { key = "1x", name = "代號不對" }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(memberCreate, HttpStatusCode.Forbidden, KnowledgeErrorCodes.Forbidden);
        await ApiRequests.AssertErrorEnvelopeAsync(memberRename, HttpStatusCode.Forbidden, KnowledgeErrorCodes.Forbidden);
        await ApiRequests.AssertErrorEnvelopeAsync(memberArchive, HttpStatusCode.Forbidden, KnowledgeErrorCodes.Forbidden);
        await ApiRequests.AssertErrorEnvelopeAsync(duplicate, HttpStatusCode.Conflict, KnowledgeErrorCodes.SpaceKeyTaken);
        var details = await ApiRequests.AssertFailureEnvelopeAsync(badKey, HttpStatusCode.UnprocessableEntity, KnowledgeErrorCodes.SpaceKeyInvalid);
        Assert.That(details[0]?["path"]?.GetValue<string>(), Is.EqualTo("key"));
    }

    [Test]
    public async Task APersonalAccessToken_CannotCallAnyKnowledgeEndpoint()
    {
        // Arrange
        using var created = await _owner.Client.PostAsync(
            "/v1/account/tokens",
            ApiRequests.Json(new { name = "web", scopes = new[] { "items:read", "items:write", "blueprint:read", "blueprint:write" }, expiresInDays = 30 }));
        var token = (await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created))!["secret"]!.GetValue<string>();
        using var bearer = Factory.CreateClient();

        // Act
        using var spaces = await bearer.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, "/v1/knowledge/spaces", token));
        using var tree = await bearer.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, $"/v1/knowledge/spaces/{SpaceKey}/tree", token));
        using var download = await bearer.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, $"/v1/knowledge/attachments/{Guid.NewGuid()}/content", token));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(spaces, HttpStatusCode.Forbidden, ApiErrorCodes.InsufficientScope);
        await ApiRequests.AssertErrorEnvelopeAsync(tree, HttpStatusCode.Forbidden, ApiErrorCodes.InsufficientScope);
        await ApiRequests.AssertErrorEnvelopeAsync(download, HttpStatusCode.Forbidden, ApiErrorCodes.InsufficientScope);
    }
}
