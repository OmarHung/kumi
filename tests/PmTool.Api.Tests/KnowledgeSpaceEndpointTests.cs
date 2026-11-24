using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Knowledge;

namespace PmTool.Api.Tests;

/// <summary>知識庫空間的端點，對假的空間服務：形狀（web/src/api/knowledgeTypes.ts）、請求怎麼交給服務、失敗的對應。</summary>
[TestFixture]
public sealed class KnowledgeSpaceEndpointTests
{
    private const string Route = "/v1/knowledge/spaces";

    private static string SpaceJson(string key = "HR", string project = "null", string archivedAt = "null", bool canEdit = true) =>
        $$$"""
        {"id":"{{{KnowledgeSamples.SpaceId}}}","key":"{{{key}}}","name":"人資","description":"人資的文件","project":{{{project}}},
         "createdAt":"2026-11-20T03:00:00Z","createdBy":{"personId":"{{{TestUsers.Owner.PersonId}}}","displayName":"Owner Example"},
         "archivedAt":{{{archivedAt}}},"myPermissions":{"canEdit":{{{canEdit.ToString().ToLowerInvariant()}}},"canComment":{{{canEdit.ToString().ToLowerInvariant()}}},"canManage":true}}
        """;

    [Test]
    public async Task List_ReturnsTheContractShapeWithTheCreatorName()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse($"[{SpaceJson()}]")), Is.True, data?.ToJsonString());
    }

    [Test]
    public async Task Get_ProjectSpace_ReturnsTheProjectWithoutItsId()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.KnowledgeSpaces.OnGet = (_, _) => KnowledgeSamples.ProjectSpace();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Route}/web");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expected = JsonNode.Parse(SpaceJson("WEB", """{"key":"WEB","name":"Example 網站","isArchived":false}"""));
        Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
    }

    [Test]
    public async Task Get_CreatorWhoLeft_UsesThePlaceholderName()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.KnowledgeSpaces.OnGet = (_, key) => KnowledgeSamples.Space(key, createdBy: Guid.NewGuid());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Route}/HR");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?["createdBy"]?["displayName"]?.GetValue<string>(), Is.EqualTo("（已不在組織）"));
    }

    [Test]
    public async Task Create_Returns201WithLocationAndPassesTheRequestToTheService()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(
            Route, ApiRequests.Json(new { key = "WEB", name = "網站", description = "說明", projectKey = "WEB" }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Headers.Location?.ToString(), Is.EqualTo("/v1/knowledge/spaces/WEB"));
            Assert.That(factory.Modules.KnowledgeSpaces.CreateCalls.Single(), Is.EqualTo((TestUsers.Owner.ToActor(), new NewSpace("WEB", "網站", "說明", "WEB"))));
            Assert.That(data?["key"]?.GetValue<string>(), Is.EqualTo("WEB"));
        }
    }

    [Test]
    public async Task Create_WithoutTheOptionalFields_PassesNulls()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Json(new { key = "HR", name = "人資" }));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(factory.Modules.KnowledgeSpaces.CreateCalls.Single().Space, Is.EqualTo(new NewSpace("HR", "人資", null, null)));
    }

    [TestCase("""{"key":"HR"}""")]
    [TestCase("""{"key":"HR","name":"人資","extra":1}""")]
    [TestCase("""{"key":1,"name":"人資"}""")]
    public async Task Create_ABodyThatDoesNotFit_Returns400WithoutCallingTheService(string json)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Document(json));

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);
        Assert.That(factory.Modules.KnowledgeSpaces.CreateCalls, Is.Empty);
    }

    [Test]
    public async Task Create_ServiceValidationFailure_Returns422WithTheDetails()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.KnowledgeSpaces.OnCreate = (_, _) => new Failure(FailureKind.Invalid, KnowledgeErrorCodes.SpaceKeyInvalid, "代號不合")
        {
            Details = [new FailureDetail(KnowledgeErrorCodes.SpaceKeyInvalid, "key", "代號不合")],
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Json(new { key = "1", name = "人資" }));

        // Assert
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, KnowledgeErrorCodes.SpaceKeyInvalid);
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{KnowledgeErrorCodes.SpaceKeyInvalid}|key" }));
    }

    [TestCase(FailureKind.Forbidden, KnowledgeErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    [TestCase(FailureKind.Conflict, KnowledgeErrorCodes.SpaceKeyTaken, HttpStatusCode.Conflict)]
    [TestCase(FailureKind.Conflict, KnowledgeErrorCodes.SpaceProjectTaken, HttpStatusCode.Conflict)]
    [TestCase(FailureKind.Conflict, KnowledgeErrorCodes.SpaceLimitReached, HttpStatusCode.Conflict)]
    [TestCase(FailureKind.Conflict, KnowledgeErrorCodes.SpaceArchived, HttpStatusCode.Conflict)]
    public async Task Create_ServiceFailure_IsMappedToTheStatusAndCode(FailureKind kind, string code, HttpStatusCode status)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.KnowledgeSpaces.OnCreate = (_, _) => new Failure(kind, code, "失敗");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Json(new { key = "HR", name = "人資" }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, status, code);
    }

    [Test]
    public async Task Get_UnknownSpace_Returns404()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.KnowledgeSpaces.OnGet = (_, _) => KnowledgeSamples.NotFound(KnowledgeErrorCodes.SpaceNotFound);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Route}/NOPE");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, KnowledgeErrorCodes.SpaceNotFound);
    }

    [Test]
    public async Task Update_PassesTheSettingsAndReturnsTheSpace()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync($"{Route}/HR", ApiRequests.Json(new { name = "新名稱", description = (string?)null }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.Modules.KnowledgeSpaces.UpdateCalls.Single(), Is.EqualTo((TestUsers.Owner.ToActor(), "HR", new SpaceSettings("新名稱", null))));
            Assert.That(data?["name"]?.GetValue<string>(), Is.EqualTo("新名稱"));
            Assert.That(data?["description"], Is.Null);
        }
    }

    [TestCase("archive", true)]
    [TestCase("unarchive", false)]
    public async Task ArchiveAndUnarchive_CallTheServiceWithTheFlag(string action, bool archived)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync($"{Route}/HR/{action}", null);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(factory.Modules.KnowledgeSpaces.ArchiveCalls.Single(), Is.EqualTo((TestUsers.Owner.ToActor(), "HR", archived)));
        Assert.That(data?["archivedAt"] is not null, Is.EqualTo(archived));
    }

    [Test]
    public async Task EveryEndpoint_WithoutSigningIn_Returns401()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();

        // Act
        using var list = await client.GetAsync(Route);
        using var create = await client.PostAsync(Route, ApiRequests.Json(new { key = "HR", name = "人資" }));
        using var get = await client.GetAsync($"{Route}/HR");
        using var archive = await client.PostAsync($"{Route}/HR/archive", null);

        // Assert
        Assert.That(new[] { list, create, get, archive }.Select(r => r.StatusCode), Is.All.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(factory.Modules.KnowledgeSpaces.CreateCalls, Is.Empty);
    }
}
