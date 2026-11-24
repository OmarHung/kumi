using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Blueprint;
using PmTool.Blueprint.Planning;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>預覽變更集：內文的規則和送出一樣，回應是沒有編號的計畫，不存任何東西。</summary>
[TestFixture]
public sealed class ChangeSetPreviewEndpointTests
{
    private const string Route = "/v1/projects/WEB/changesets/preview";

    [TestCase("application/json", DocumentFormat.Json)]
    [TestCase("application/yaml", DocumentFormat.Yaml)]
    [TestCase("text/yaml; charset=utf-8", DocumentFormat.Yaml)]
    public async Task Preview_SupportedContentType_PassesTheDocumentAsIsWithItsFormat(string contentType, DocumentFormat format)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        const string document = "# project: WEB\nbaseVersion: 1\n";

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Document(document, contentType));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var call = factory.Modules.Changes.PreviewCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That((call.ProjectKey, call.Document, call.Format), Is.EqualTo(("WEB", document, format)));
            Assert.That(factory.Modules.Changes.TotalCalls, Is.EqualTo(1), "只走預覽，不送出");
        }
    }

    [TestCase("text/plain", ApiErrorCodes.UnsupportedMediaType)]
    [TestCase(null, ApiErrorCodes.UnsupportedMediaType)]
    [TestCase("application/json; charset=utf-16", ApiErrorCodes.UnsupportedCharset)]
    public async Task Preview_UnsupportedContentTypeOrCharset_Returns415WithoutReachingTheModule(string? contentType, string expectedCode)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Document("{}", contentType));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnsupportedMediaType, expectedCode);
        Assert.That(factory.Modules.Changes.TotalCalls, Is.Zero);
    }

    [Test]
    public async Task Preview_WithoutSession_Returns401WithoutReachingTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Document("{}"));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
        Assert.That(factory.Modules.Changes.TotalCalls, Is.Zero);
    }

    [Test]
    public async Task Preview_Succeeds_HasTheContractShapeWithoutAChangeSetNumber()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Changes.OnPreview = _ => new ChangeSetPreview(
            1,
            2,
            TestData.Blueprint(),
            [new BlueprintChange { Kind = ChangeKind.Added, Resource = ResourceKind.Field, Key = "budget" }],
            [new PlannedImpact(new StateMoveImpact("main", "todo", "doing"), 4, false)],
            [new FailureDetail(IssueCodes.PlanBaseVersionMismatch, "/baseVersion", "版本不對。")],
            CanApply: false);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Document("{}"));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expected = new JsonObject
        {
            ["baseVersion"] = 1,
            ["currentVersion"] = 2,
            ["blueprint"] = JsonNode.Parse(BlueprintSerializer.ToJson(TestData.Blueprint())),
            ["changes"] = new JsonArray(new JsonObject
            {
                ["kind"] = "added", ["resource"] = "field", ["key"] = "budget", ["parentKey"] = null, ["changedProperties"] = new JsonArray(),
            }),
            ["impacts"] = new JsonArray(new JsonObject
            {
                ["kind"] = "state_move", ["affected"] = 4, ["blocksApply"] = false,
                ["workflow"] = "main", ["from"] = "todo", ["to"] = "doing",
            }),
            ["blockers"] = new JsonArray(new JsonObject
            {
                ["code"] = "plan.base_version_mismatch", ["path"] = "/baseVersion", ["message"] = "版本不對。",
            }),
            ["canApply"] = false,
        };
        Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
    }

    [TestCase(FailureKind.Invalid, WorkErrorCodes.ChangeSetInvalid, HttpStatusCode.UnprocessableEntity)]
    [TestCase(FailureKind.Forbidden, WorkErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    [TestCase(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, HttpStatusCode.NotFound)]
    public async Task Preview_ModuleFails_ReturnsTheMatchingStatusWithDetails(FailureKind kind, string code, HttpStatusCode expected)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Changes.OnPreview = _ => new Failure(kind, code, "模組說的原因。")
        {
            Details = [new FailureDetail(IssueCodes.PlanStateMoveRequired, "/migrations/moveStates", "要指定搬去哪裡。")],
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Document("{}"));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, expected, code);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { "plan.state_move_required|/migrations/moveStates" }));
    }
}
