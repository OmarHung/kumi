using System.Net;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>專案說明的主線（真的資料庫）：寫、讀、清掉、權限與私鑰。</summary>
[TestFixture]
public sealed class ProjectDescriptionFlowTests : RealStackTestBase
{
    [Test]
    public async Task WriteReadAndClear_FullFlow_RecordsTheWriterByName()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "DSC", "simple_tasks");

        // Act
        var empty = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/DSC/description");
        using var put = await owner.Client.PutAsync("/v1/projects/DSC/description", ApiRequests.Json(new { text = "## 網址\n- 正式：https://www.example.com" }));
        var written = await ApiRequests.AssertSuccessEnvelopeAsync(put, HttpStatusCode.OK);
        var read = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/DSC/description");
        using var cleared = await owner.Client.PutAsync("/v1/projects/DSC/description", ApiRequests.Json(new { text = "  " }));
        var clearedData = await ApiRequests.AssertSuccessEnvelopeAsync(cleared, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(empty["text"], Is.Null);
            Assert.That(empty["updatedBy"], Is.Null);
            Assert.That(read["text"]?.GetValue<string>(), Is.EqualTo("## 網址\n- 正式：https://www.example.com"));
            Assert.That(read["updatedBy"]?["personId"]?.GetValue<Guid>(), Is.EqualTo(owner.PersonId));
            Assert.That(read["updatedBy"]?["displayName"]?.GetValue<string>(), Is.Not.Empty);
            Assert.That(read["updatedAt"]?.GetValue<DateTimeOffset>(), Is.EqualTo(written?["updatedAt"]?.GetValue<DateTimeOffset>()));
            Assert.That(clearedData?["text"], Is.Null);
            Assert.That(clearedData?["updatedAt"], Is.Not.Null);
        }
    }

    [Test]
    public async Task Put_ByAnEditor_Returns403AndReadingStillWorks()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "DSE", "simple_tasks");
        using var editor = await AddMemberAsync(owner, "DSE", "editor", "Editor Example", RealStackSteps.UniqueEmail("desc-editor"));

        // Act
        using var put = await editor.Client.PutAsync("/v1/projects/DSE/description", ApiRequests.Json(new { text = "改" }));
        using var get = await editor.Client.GetAsync("/v1/projects/DSE/description");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(put, HttpStatusCode.Forbidden, WorkErrorCodes.Forbidden);
        await ApiRequests.AssertSuccessEnvelopeAsync(get, HttpStatusCode.OK);
    }

    [Test]
    public async Task Put_PrivateKey_Returns422WithTheTextPath()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "DSK", "simple_tasks");

        // Act
        using var response = await owner.Client.PutAsync(
            "/v1/projects/DSK/description", ApiRequests.Json(new { text = "金鑰\n-----BEGIN OPENSSH PRIVATE KEY-----\nabc" }));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.ProjectDescriptionSecret);

        // Assert
        Assert.That(details[0]?["path"]?.GetValue<string>(), Is.EqualTo("text"));
    }

    [Test]
    public async Task ListTemplates_ReturnsTheBuiltInWebProject()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();

        // Act
        using var response = await owner.Client.GetAsync("/v1/description-templates");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert：這個組織還沒有自己的格式。
        var template = data!.AsArray().Single()!;
        Assert.That((template["id"]?.GetValue<string>(), template["name"]?.GetValue<string>()), Is.EqualTo(("web_project", "網站案子")));
    }

    [Test]
    public async Task Templates_CreateListUpdateAndDelete_FullFlow()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();

        // Act
        using var created = await owner.Client.PostAsync(
            "/v1/description-templates", ApiRequests.Json(new { name = "維運", body = "## 主機\n- 名稱：", isDefault = true }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created);
        var id = data!["id"]!.GetValue<string>();
        var list = await RealStackSteps.GetAsync(owner.Client, "/v1/description-templates");
        using var updated = await owner.Client.PutAsync(
            $"/v1/description-templates/{id}", ApiRequests.Json(new { name = "維運二", body = "新", isDefault = false }));
        var updatedData = await ApiRequests.AssertSuccessEnvelopeAsync(updated, HttpStatusCode.OK);
        using var deleted = await owner.Client.DeleteAsync($"/v1/description-templates/{id}");
        var deletedData = await ApiRequests.AssertSuccessEnvelopeAsync(deleted, HttpStatusCode.OK);
        var after = await RealStackSteps.GetAsync(owner.Client, "/v1/description-templates");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Headers.Location?.ToString(), Is.EqualTo($"/v1/description-templates/{id}"));
            Assert.That(list.AsArray().Select(t => t!["id"]!.GetValue<string>()), Is.EqualTo(new[] { id, "web_project" }));
            Assert.That(list.AsArray()[0]!["isDefault"]!.GetValue<bool>(), Is.True);
            Assert.That(list.AsArray()[1]!["isDefault"]!.GetValue<bool>(), Is.False);
            Assert.That(updatedData?["name"]?.GetValue<string>(), Is.EqualTo("維運二"));
            Assert.That(deletedData?["body"]?.GetValue<string>(), Is.EqualTo("新"));
            Assert.That(after.AsArray().Select(t => t!["id"]!.GetValue<string>()), Is.EqualTo(new[] { "web_project" }));
            Assert.That(after.AsArray()[0]!["isDefault"]!.GetValue<bool>(), Is.True);
        }
    }

    [Test]
    public async Task Templates_Errors_HaveTheirEnvelopes()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "DSN", "simple_tasks");
        using var editor = await AddMemberAsync(owner, "DSN", "editor", "Editor Example", RealStackSteps.UniqueEmail("tpl-editor"));
        var name = $"格式 {Guid.NewGuid():N}";
        using var first = await owner.Client.PostAsync("/v1/description-templates", ApiRequests.Json(new { name, body = "x" }));
        var firstData = await ApiRequests.AssertSuccessEnvelopeAsync(first, HttpStatusCode.Created);

        // Act
        using var forbidden = await editor.Client.PostAsync("/v1/description-templates", ApiRequests.Json(new { name = "a", body = "b" }));
        using var taken = await owner.Client.PostAsync("/v1/description-templates", ApiRequests.Json(new { name = name.ToUpperInvariant(), body = "x" }));
        using var invalid = await owner.Client.PostAsync("/v1/description-templates", ApiRequests.Json(new { name = " ", body = "-----BEGIN PRIVATE KEY-----" }));
        using var builtIn = await owner.Client.PutAsync("/v1/description-templates/web_project", ApiRequests.Json(new { name = "a", body = "b" }));
        using var builtInDelete = await owner.Client.DeleteAsync("/v1/description-templates/web_project");
        using var missing = await owner.Client.DeleteAsync($"/v1/description-templates/{Guid.NewGuid()}");

        using var editorBuiltIn = await editor.Client.PutAsync("/v1/description-templates/web_project", ApiRequests.Json(new { name = "a", body = "b" }));
        using var editorBuiltInDelete = await editor.Client.DeleteAsync("/v1/description-templates/web_project");

        // Assert：非管理員不論 id 是什麼都是 403，管理員才看得到 404。
        await ApiRequests.AssertErrorEnvelopeAsync(editorBuiltIn, HttpStatusCode.Forbidden, WorkErrorCodes.Forbidden);
        await ApiRequests.AssertErrorEnvelopeAsync(editorBuiltInDelete, HttpStatusCode.Forbidden, WorkErrorCodes.Forbidden);
        await ApiRequests.AssertErrorEnvelopeAsync(forbidden, HttpStatusCode.Forbidden, WorkErrorCodes.Forbidden);
        await ApiRequests.AssertErrorEnvelopeAsync(taken, HttpStatusCode.Conflict, WorkErrorCodes.DescriptionTemplateNameTaken);
        var details = await ApiRequests.AssertFailureEnvelopeAsync(
            invalid, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.DescriptionTemplateNameRequired);
        await ApiRequests.AssertErrorEnvelopeAsync(builtIn, HttpStatusCode.NotFound, WorkErrorCodes.DescriptionTemplateNotFound);
        await ApiRequests.AssertErrorEnvelopeAsync(builtInDelete, HttpStatusCode.NotFound, WorkErrorCodes.DescriptionTemplateNotFound);
        await ApiRequests.AssertErrorEnvelopeAsync(missing, HttpStatusCode.NotFound, WorkErrorCodes.DescriptionTemplateNotFound);
        Assert.That(
            details.Select(d => (d?["code"]?.GetValue<string>(), d?["path"]?.GetValue<string>())),
            Is.EquivalentTo(new[]
            {
                (WorkErrorCodes.DescriptionTemplateNameRequired, "name"),
                (WorkErrorCodes.ProjectDescriptionSecret, "body"),
            }));
        Assert.That(firstData?["id"], Is.Not.Null);
    }

    [Test]
    public async Task CreateProject_WithDescription_StoresItAndABadOneRefusesTheProject()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();

        // Act
        using var created = await owner.Client.PostAsync(
            "/v1/projects",
            ApiRequests.Json(new { key = "DSP", name = "Example", template = "simple_tasks", description = "## 網址\n- 正式" }));
        await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created);
        var read = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/DSP/description");
        using var refused = await owner.Client.PostAsync(
            "/v1/projects",
            ApiRequests.Json(new { key = "DSQ", name = "Example", template = "simple_tasks", description = "-----BEGIN RSA PRIVATE KEY-----" }));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(
            refused, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.ProjectDescriptionSecret);
        using var notCreated = await owner.Client.GetAsync("/v1/projects/DSQ");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read["text"]?.GetValue<string>(), Is.EqualTo("## 網址\n- 正式"));
            Assert.That(read["updatedBy"]?["personId"]?.GetValue<Guid>(), Is.EqualTo(owner.PersonId));
            Assert.That(details[0]?["path"]?.GetValue<string>(), Is.EqualTo("description"));
            Assert.That(notCreated.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }
    }
}
