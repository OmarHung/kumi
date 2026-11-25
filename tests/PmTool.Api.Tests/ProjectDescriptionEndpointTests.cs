using System.Net;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>專案說明與說明格式：API 只轉換格式與狀態碼，規則由工作引擎判斷。</summary>
[TestFixture]
public sealed class ProjectDescriptionEndpointTests
{
    [Test]
    public async Task Get_NeverWritten_WritesThreeNullFields()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync("/v1/projects/WEB/description");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var fields = data!.AsObject();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fields.Select(f => f.Key), Is.EquivalentTo(new[] { "text", "updatedAt", "updatedBy" }));
            Assert.That(fields.All(f => f.Value is null), Is.True);
        }
    }

    [Test]
    public async Task Get_Written_ReturnsTextTimeAndThePersonWithDisplayName()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Descriptions.OnGet = (_, _) => new ProjectDescription("## 網址\n- 正式：https://www.example.com", TestData.UpdatedAt, TestUsers.Member.PersonId);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync("/v1/projects/WEB/description");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["text"]?.GetValue<string>(), Is.EqualTo("## 網址\n- 正式：https://www.example.com"));
            Assert.That(data?["updatedAt"]?.GetValue<DateTimeOffset>(), Is.EqualTo(TestData.UpdatedAt));
            Assert.That(data?["updatedBy"]?["personId"]?.GetValue<Guid>(), Is.EqualTo(TestUsers.Member.PersonId));
            Assert.That(data?["updatedBy"]?["displayName"]?.GetValue<string>(), Is.EqualTo("Member Example"));
        }
    }

    [Test]
    public async Task Get_UpdatedBySomeoneWhoLeft_UsesThePlaceholderName()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Descriptions.OnGet = (_, _) => new ProjectDescription("內容", TestData.UpdatedAt, Guid.NewGuid());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync("/v1/projects/WEB/description");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?["updatedBy"]?["displayName"]?.GetValue<string>(), Is.EqualTo("（已不在組織）"));
    }

    [Test]
    public async Task Get_ProjectNotVisible_Returns404()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Descriptions.OnGet = (_, key) => new Failure(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, $"找不到專案 {key}。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/projects/NOPE/description");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, WorkErrorCodes.ProjectNotFound);
    }

    [Test]
    public async Task Put_Text_PassesItToTheModuleAndReturnsTheDescription()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync("/v1/projects/WEB/description", ApiRequests.Json(new { text = "## 主機\n- 主機：example-host" }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["text"]?.GetValue<string>(), Is.EqualTo("## 主機\n- 主機：example-host"));
            Assert.That(data?["updatedBy"]?["displayName"]?.GetValue<string>(), Is.EqualTo("Owner Example"));
            Assert.That(
                factory.Modules.Descriptions.SetCalls,
                Is.EqualTo(new[] { (TestUsers.Owner.ToActor(), "WEB", (string?)"## 主機\n- 主機：example-host") }));
        }
    }

    [Test]
    public async Task Put_NullText_PassesNullToTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync("/v1/projects/WEB/description", ApiRequests.Json(new { text = (string?)null }));
        await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(factory.Modules.Descriptions.SetCalls.Single().Text, Is.Null);
    }

    [Test]
    public async Task Put_WithoutText_Returns400AndLeavesTheDescriptionAlone()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync("/v1/projects/WEB/description", ApiRequests.Json(new { }));
        await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);

        // Assert
        Assert.That(factory.Modules.Descriptions.SetCalls, Is.Empty);
    }

    [TestCase(WorkErrorCodes.ProjectDescriptionTooLong)]
    [TestCase(WorkErrorCodes.ProjectDescriptionInvalid)]
    [TestCase(WorkErrorCodes.ProjectDescriptionSecret)]
    public async Task Put_InvalidText_Returns422WithTheTextPath(string code)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Descriptions.OnSet = (_, _, _) =>
            new Failure(FailureKind.Invalid, code, "失敗") { Details = [new FailureDetail(code, "text", "失敗")] };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PutAsync("/v1/projects/WEB/description", ApiRequests.Json(new { text = "x" }));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, code);

        // Assert
        Assert.That(details[0]?["path"]?.GetValue<string>(), Is.EqualTo("text"));
    }

    [TestCase(FailureKind.Forbidden, WorkErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    [TestCase(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, HttpStatusCode.NotFound)]
    public async Task Put_ModuleFails_MapsTheFailureToTheStatusCode(FailureKind kind, string code, HttpStatusCode expected)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Descriptions.OnSet = (_, _, _) => new Failure(kind, code, "失敗");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PutAsync("/v1/projects/WEB/description", ApiRequests.Json(new { text = "x" }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, expected, code);
    }

    [Test]
    public async Task ListTemplates_WritesTheTemplatesWithTheirFlags()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/description-templates");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var template = data!.AsArray().Single()!.AsObject();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(template.Select(f => f.Key), Is.EquivalentTo(new[] { "id", "name", "body", "isBuiltIn", "isDefault" }));
            Assert.That(template["id"]?.GetValue<string>(), Is.EqualTo("web_project"));
            Assert.That(template["isBuiltIn"]?.GetValue<bool>(), Is.True);
            Assert.That(template["isDefault"]?.GetValue<bool>(), Is.True);
        }
    }

    [Test]
    public async Task ListTemplates_Guest_OnlyGetsTheBuiltInTemplates()
    {
        // Arrange：組織自己寫的說明格式可能放著內部的主機與流程。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Descriptions.Templates =
        [
            new DescriptionTemplate("web_project", "網站案子", "## 基本資料", IsBuiltIn: true, IsDefault: false),
            new DescriptionTemplate("00000000-0000-0000-0000-0000000000b2", "內部維運", "## 主機", IsBuiltIn: false, IsDefault: true),
        ];
        using var guest = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Guest);
        using var member = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var guestResponse = await guest.GetAsync("/v1/description-templates");
        using var memberResponse = await member.GetAsync("/v1/description-templates");
        var guestData = await ApiRequests.AssertSuccessEnvelopeAsync(guestResponse, HttpStatusCode.OK);
        var memberData = await ApiRequests.AssertSuccessEnvelopeAsync(memberResponse, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(guestData!.AsArray().Select(t => t!["id"]!.GetValue<string>()), Is.EqualTo(new[] { "web_project" }));
            Assert.That(memberData!.AsArray(), Has.Count.EqualTo(2));
        }
    }

    private const string TemplateId = "00000000-0000-0000-0000-0000000000b2";

    [Test]
    public async Task PostTemplate_Accepted_Returns201WithLocationAndPassesTheInput()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(
            "/v1/description-templates", ApiRequests.Json(new { name = "維運", body = "## 主機", isDefault = true }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        var call = factory.Modules.Descriptions.WriteTemplateCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Headers.Location?.ToString(), Is.EqualTo($"/v1/description-templates/{data?["id"]?.GetValue<string>()}"));
            Assert.That(data!.AsObject().Select(f => f.Key), Is.EquivalentTo(new[] { "id", "name", "body", "isBuiltIn", "isDefault" }));
            Assert.That(data["isBuiltIn"]?.GetValue<bool>(), Is.False);
            Assert.That(call.TemplateId, Is.Null);
            Assert.That(call.Input, Is.EqualTo(new DescriptionTemplateInput("維運", "## 主機", IsDefault: true)));
        }
    }

    [Test]
    public async Task PostTemplate_WithoutIsDefault_MeansNotDefault()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync("/v1/description-templates", ApiRequests.Json(new { name = "維運", body = "x" }));
        await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        Assert.That(factory.Modules.Descriptions.WriteTemplateCalls.Single().Input.IsDefault, Is.False);
    }

    [Test]
    public async Task PostTemplate_WithoutName_Returns400()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync("/v1/description-templates", ApiRequests.Json(new { body = "x" }));

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);
        Assert.That(factory.Modules.Descriptions.WriteTemplateCalls, Is.Empty);
    }

    [TestCase(FailureKind.Forbidden, WorkErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    [TestCase(FailureKind.Conflict, WorkErrorCodes.DescriptionTemplateNameTaken, HttpStatusCode.Conflict)]
    [TestCase(FailureKind.Conflict, WorkErrorCodes.DescriptionTemplateLimitReached, HttpStatusCode.Conflict)]
    public async Task PostTemplate_ModuleFails_MapsTheFailureToTheStatusCode(FailureKind kind, string code, HttpStatusCode expected)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Descriptions.OnWriteTemplate = (_, _, _) => new Failure(kind, code, "失敗");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/description-templates", ApiRequests.Json(new { name = "a", body = "b" }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, expected, code);
    }

    [Test]
    public async Task PostTemplate_Invalid_Returns422WithEveryPath()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Descriptions.OnWriteTemplate = (_, _, _) => new Failure(
            FailureKind.Invalid, WorkErrorCodes.DescriptionTemplateNameRequired, "失敗")
        {
            Details =
            [
                new FailureDetail(WorkErrorCodes.DescriptionTemplateNameRequired, "name", "請填名稱"),
                new FailureDetail(WorkErrorCodes.DescriptionTemplateBodyRequired, "body", "請填內容"),
            ],
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/description-templates", ApiRequests.Json(new { name = "", body = "" }));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(
            response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.DescriptionTemplateNameRequired);

        // Assert
        Assert.That(details.Select(d => d?["path"]?.GetValue<string>()), Is.EqualTo(new[] { "name", "body" }));
    }

    [Test]
    public async Task PutTemplate_Accepted_Returns200AndPassesTheIdAndInput()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync(
            $"/v1/description-templates/{TemplateId}", ApiRequests.Json(new { name = "維運", body = "新", isDefault = false }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["id"]?.GetValue<string>(), Is.EqualTo(TemplateId));
            Assert.That(
                factory.Modules.Descriptions.WriteTemplateCalls.Single().TemplateId, Is.EqualTo(Guid.Parse(TemplateId)));
        }
    }

    [TestCase("web_project")]
    [TestCase("亂打")]
    public async Task PutAndDeleteTemplate_IdIsNotAGuid_AsksTheModuleWithAnEmptyGuid(string id)
    {
        // Arrange：模組回找不到，端點照轉成有信封的 404。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Descriptions.OnWriteTemplate = (_, _, _) =>
            new Failure(FailureKind.NotFound, WorkErrorCodes.DescriptionTemplateNotFound, "找不到");
        factory.Modules.Descriptions.OnDeleteTemplate = (_, _) =>
            new Failure(FailureKind.NotFound, WorkErrorCodes.DescriptionTemplateNotFound, "找不到");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var put = await client.PutAsync(
            $"/v1/description-templates/{Uri.EscapeDataString(id)}", ApiRequests.Json(new { name = "a", body = "b" }));
        using var delete = await client.DeleteAsync($"/v1/description-templates/{Uri.EscapeDataString(id)}");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(put, HttpStatusCode.NotFound, WorkErrorCodes.DescriptionTemplateNotFound);
        await ApiRequests.AssertErrorEnvelopeAsync(delete, HttpStatusCode.NotFound, WorkErrorCodes.DescriptionTemplateNotFound);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.Modules.Descriptions.WriteTemplateCalls.Single().TemplateId, Is.EqualTo(Guid.Empty));
            Assert.That(factory.Modules.Descriptions.DeleteTemplateCalls.Single().TemplateId, Is.EqualTo(Guid.Empty));
        }
    }

    [Test]
    public async Task PutAndDeleteTemplate_IdIsNotAGuidAndTheModuleSaysForbidden_Returns403()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Descriptions.OnWriteTemplate = (_, _, _) => new Failure(FailureKind.Forbidden, WorkErrorCodes.Forbidden, "不行");
        factory.Modules.Descriptions.OnDeleteTemplate = (_, _) => new Failure(FailureKind.Forbidden, WorkErrorCodes.Forbidden, "不行");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var put = await client.PutAsync("/v1/description-templates/web_project", ApiRequests.Json(new { name = "a", body = "b" }));
        using var delete = await client.DeleteAsync("/v1/description-templates/web_project");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(put, HttpStatusCode.Forbidden, WorkErrorCodes.Forbidden);
        await ApiRequests.AssertErrorEnvelopeAsync(delete, HttpStatusCode.Forbidden, WorkErrorCodes.Forbidden);
    }

    [Test]
    public async Task PutTemplate_NotFound_Returns404()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Descriptions.OnWriteTemplate = (_, _, _) =>
            new Failure(FailureKind.NotFound, WorkErrorCodes.DescriptionTemplateNotFound, "找不到");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PutAsync(
            $"/v1/description-templates/{TemplateId}", ApiRequests.Json(new { name = "a", body = "b" }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, WorkErrorCodes.DescriptionTemplateNotFound);
    }

    [Test]
    public async Task DeleteTemplate_Accepted_ReturnsTheDeletedTemplate()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.DeleteAsync($"/v1/description-templates/{TemplateId}");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["id"]?.GetValue<string>(), Is.EqualTo(TemplateId));
            Assert.That(data?["name"]?.GetValue<string>(), Is.EqualTo("舊格式"));
            Assert.That(factory.Modules.Descriptions.DeleteTemplateCalls.Single().TemplateId, Is.EqualTo(Guid.Parse(TemplateId)));
        }
    }

    [TestCase(FailureKind.Forbidden, WorkErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    [TestCase(FailureKind.NotFound, WorkErrorCodes.DescriptionTemplateNotFound, HttpStatusCode.NotFound)]
    public async Task DeleteTemplate_ModuleFails_MapsTheFailureToTheStatusCode(FailureKind kind, string code, HttpStatusCode expected)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Descriptions.OnDeleteTemplate = (_, _) => new Failure(kind, code, "失敗");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.DeleteAsync($"/v1/description-templates/{TemplateId}");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, expected, code);
    }
}
