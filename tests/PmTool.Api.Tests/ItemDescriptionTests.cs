using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>
/// 項目的說明：新增與修改都收；單一項目的回應一定有這個屬性（沒有值是 null），
/// 清單與「我的工作」的項目沒有這個屬性，一頁兩百個項目不必帶著長文字。
/// </summary>
[TestFixture]
public sealed class ItemDescriptionTests
{
    private const string Description = "第一段\n\n第二段";

    private static readonly object[] SingleItemRequests =
    [
        new object[] { "GET", "/v1/items/WEB-1", null!, HttpStatusCode.OK },
        new object[] { "POST", "/v1/projects/WEB/items", """{"type":"task","title":"寫首頁"}""", HttpStatusCode.Created },
        new object[] { "PATCH", "/v1/items/WEB-1", """{"title":"新標題"}""", HttpStatusCode.OK },
        new object[] { "POST", "/v1/items/WEB-1/transitions", """{"to":"doing"}""", HttpStatusCode.OK },
    ];

    [TestCaseSource(nameof(SingleItemRequests))]
    public async Task SingleItemResponse_ItemHasADescription_IncludesIt(
        string method, string route, string? body, HttpStatusCode expectedStatus)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        ReturnFromEverySingleItemCall(factory, TestData.Item() with { Description = Description });
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.SendAsync(Request(method, route, body));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, expectedStatus);

        // Assert
        Assert.That(data?["description"]?.GetValue<string>(), Is.EqualTo(Description));
    }

    [TestCaseSource(nameof(SingleItemRequests))]
    public async Task SingleItemResponse_ItemHasNoDescription_WritesNull(
        string method, string route, string? body, HttpStatusCode expectedStatus)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        ReturnFromEverySingleItemCall(factory, TestData.Item());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.SendAsync(Request(method, route, body));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, expectedStatus);

        // Assert
        Assert.That(data?.AsObject().ContainsKey("description") == true && data["description"] is null, Is.True, data?.ToJsonString());
    }

    [TestCaseSource(nameof(SingleItemRequests))]
    public async Task SingleItemResponse_IncludesTheDescriptionRevision(
        string method, string route, string? body, HttpStatusCode expectedStatus)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        ReturnFromEverySingleItemCall(factory, TestData.Item() with { Description = Description });
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.SendAsync(Request(method, route, body));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, expectedStatus);

        // Assert
        Assert.That(data?["descriptionRevision"]?.GetValue<string>(), Is.EqualTo(DescriptionRevisions.Of(Description)));
    }

    [Test]
    public async Task ListItems_ItemsHaveNoDescriptionRevision()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Items.OnList = (_, _, _) => new WorkItemPage([TestData.Item() with { Description = Description }], null);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/projects/WEB/items");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?["items"]?[0]?.AsObject().ContainsKey("descriptionRevision"), Is.False, data?.ToJsonString());
    }

    [Test]
    public async Task Patch_ExpectedDescriptionRevision_IsPassedToTheModule()
    {
        // Act
        var patch = await PatchAsync("""{"description":"新的","expectedDescriptionRevision":"0123456789abcdef"}""");

        // Assert
        Assert.That(patch.ExpectedDescriptionRevision, Is.EqualTo("0123456789abcdef"));
    }

    [Test]
    public async Task Patch_NoExpectedDescriptionRevision_IsNull()
    {
        // Act
        var patch = await PatchAsync("""{"description":"新的"}""");

        // Assert
        Assert.That(patch.ExpectedDescriptionRevision, Is.Null);
    }

    [Test]
    public async Task Patch_ExpectedDescriptionRevisionNotAString_Returns400()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PatchAsync("/v1/items/WEB-1", ApiRequests.Document("""{"expectedDescriptionRevision":5}"""));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{ApiErrorCodes.BadRequest}|expectedDescriptionRevision" }));
        Assert.That(factory.Modules.Items.UpdateCalls, Is.Empty);
    }

    [Test]
    public async Task Patch_DescriptionConflict_Returns409()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Items.OnUpdate = (_, _, _) =>
            new Failure(FailureKind.Conflict, WorkErrorCodes.ItemDescriptionConflict, "這段說明在你編輯時被別人改過了。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PatchAsync("/v1/items/WEB-1", ApiRequests.Document("""{"description":"新的","expectedDescriptionRevision":"0123456789abcdef"}"""));

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.Conflict, WorkErrorCodes.ItemDescriptionConflict);
    }

    [Test]
    public async Task ListItems_EvenIfTheModuleGivesADescription_ItemsHaveNoDescriptionProperty()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Items.OnList = (_, _, _) => new WorkItemPage([TestData.Item() with { Description = Description }], null);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/projects/WEB/items");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var item = data?["items"]?[0]?.AsObject();
        Assert.That(item?.ContainsKey("key"), Is.True, data?.ToJsonString());
        Assert.That(item?.ContainsKey("description"), Is.False, data?.ToJsonString());
    }

    [Test]
    public async Task MyWork_EvenIfTheModuleGivesADescription_ItemsHaveNoDescriptionProperty()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Items.OnMyWork = actor => [TestData.Item("WEB-1", actor.PersonId) with { Description = Description }];
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/my-work");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var item = data?["items"]?[0]?.AsObject();
        Assert.That(item?.ContainsKey("key"), Is.True, data?.ToJsonString());
        Assert.That(item?.ContainsKey("description"), Is.False, data?.ToJsonString());
    }

    [TestCase("""{"type":"task","title":"x","description":"第一段\n\n第二段"}""", Description, TestName = "CreateItem_Description_IsPassedToTheModule")]
    [TestCase("""{"type":"task","title":"x","description":null}""", null, TestName = "CreateItem_DescriptionNull_IsPassedAsNoDescription")]
    [TestCase("""{"type":"task","title":"x"}""", null, TestName = "CreateItem_NoDescription_IsPassedAsNoDescription")]
    public async Task CreateItem_Description(string body, string? expected)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/projects/WEB/items", ApiRequests.Document(body));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
        Assert.That(factory.Modules.Items.CreateCalls.Single().Item.Description, Is.EqualTo(expected));
    }

    [Test]
    public async Task Patch_DescriptionAbsent_LeavesItUnset()
    {
        // Act
        var patch = await PatchAsync("""{"title":"新標題"}""");

        // Assert
        Assert.That(patch.Description.HasValue, Is.False);
    }

    [Test]
    public async Task Patch_DescriptionNull_IsARequestToClear()
    {
        // Act
        var patch = await PatchAsync("""{"description":null}""");

        // Assert
        Assert.That(patch.Description, Is.EqualTo(new Optional<string?>(null)));
    }

    [TestCase("""{"description":"第一段\n\n第二段"}""", Description, TestName = "Patch_DescriptionValue_IsPassedThrough")]
    [TestCase("""{"description":""}""", "", TestName = "Patch_DescriptionEmpty_IsPassedThroughForTheModuleToClear")]
    public async Task Patch_DescriptionGiven(string body, string expected)
    {
        // Act
        var patch = await PatchAsync(body);

        // Assert
        Assert.That(patch.Description, Is.EqualTo(new Optional<string?>(expected)));
    }

    [TestCase("POST", "/v1/projects/WEB/items", """{"type":"task","title":"x","description":5}""", TestName = "CreateItem_DescriptionNotAString_Returns400")]
    [TestCase("POST", "/v1/projects/WEB/items", """{"type":"task","title":"x","description":["a"]}""", TestName = "CreateItem_DescriptionAnArray_Returns400")]
    [TestCase("PATCH", "/v1/items/WEB-1", """{"description":5}""", TestName = "Patch_DescriptionNotAString_Returns400")]
    [TestCase("PATCH", "/v1/items/WEB-1", """{"description":{"text":"a"}}""", TestName = "Patch_DescriptionAnObject_Returns400")]
    public async Task Description_NotAString(string method, string route, string body)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.SendAsync(Request(method, route, body));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{ApiErrorCodes.BadRequest}|description" }));
        Assert.That(factory.Modules.Items.CreateCalls, Is.Empty);
        Assert.That(factory.Modules.Items.UpdateCalls, Is.Empty);
    }

    [TestCase("POST", "/v1/projects/WEB/items", """{"type":"task","title":"x","description":"{0}"}""", HttpStatusCode.Created)]
    [TestCase("PATCH", "/v1/items/WEB-1", """{"description":"{0}"}""", HttpStatusCode.OK)]
    public async Task Description_AtTheModuleLimitInChinese_ReachesTheModule(
        string method, string route, string template, HttpStatusCode expectedStatus)
    {
        // Arrange：一個字 3 個位元組，五萬字是 150,000 位元組，要放得進 JSON 內文的上限。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var description = new string('說', WorkRules.MaxDescriptionLength);

        // Act
        using var response = await client.SendAsync(Request(method, route, template.Replace("{0}", description, StringComparison.Ordinal)));
        var received = method == "POST"
            ? factory.Modules.Items.CreateCalls.SingleOrDefault().Item?.Description
            : factory.Modules.Items.UpdateCalls.SingleOrDefault().Patch?.Description.Value;

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatus), await response.Content.ReadAsStringAsync());
        Assert.That(received, Is.EqualTo(description));
    }

    [Test]
    public async Task Description_ModuleRejectsTheLength_Returns422WithTheModuleCode()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Items.OnUpdate = (_, _, _) => new Failure(FailureKind.Invalid, WorkErrorCodes.ItemDescriptionTooLong, "說明太長。")
        {
            Details = [new FailureDetail(WorkErrorCodes.ItemDescriptionTooLong, "description", "說明太長。")],
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PatchAsync("/v1/items/WEB-1", ApiRequests.Document("""{"description":"太長"}"""));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(
            response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.ItemDescriptionTooLong);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{WorkErrorCodes.ItemDescriptionTooLong}|description" }));
    }

    private static void ReturnFromEverySingleItemCall(ApiFactory factory, WorkItem item)
    {
        factory.Modules.Items.OnGet = (_, _) => item;
        factory.Modules.Items.OnCreate = (_, _, _) => item;
        factory.Modules.Items.OnUpdate = (_, _, _) => item;
        factory.Modules.Items.OnTransition = (_, _, _) => item;
    }

    private static HttpRequestMessage Request(string method, string route, string? body) =>
        new(new HttpMethod(method), route) { Content = body is null ? null : ApiRequests.Document(body) };

    private static async Task<WorkItemPatch> PatchAsync(string body)
    {
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        using var response = await client.PatchAsync("/v1/items/WEB-1", ApiRequests.Document(body));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return factory.Modules.Items.UpdateCalls.Single().Patch;
    }
}
