using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>POST /v1/items/{itemKey}/comments：內文的規則和其他 JSON 端點一樣，內容的問題由工作引擎判斷。</summary>
[TestFixture]
public sealed class ItemCommentTests
{
    private const string Route = "/v1/items/WEB-7/comments";

    [Test]
    public async Task AddComment_Accepted_PassesTheBodyAsIsAndReturns201WithTheActivity()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Json(new { body = "  第一行\n\t第二行  " }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        var expected = new JsonObject
        {
            ["id"] = TestData.ActivityId(1),
            ["kind"] = "commented",
            ["actor"] = new JsonObject { ["personId"] = TestUsers.Member.PersonId, ["displayName"] = TestUsers.Member.DisplayName },
            ["at"] = "2026-11-14T07:01:00Z",
            ["changes"] = new JsonArray(),
            ["fromState"] = null,
            ["toState"] = null,
            ["comment"] = "  第一行\n\t第二行  ",
            ["replyToId"] = null,
            ["importedFrom"] = null,
            ["blueprintVersion"] = null,
            ["editedAt"] = null,
            ["deletedAt"] = null,
            ["deletedBy"] = null,
        };
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
            Assert.That(response.Headers.Location, Is.Null, "一則留言沒有自己的網址");
            Assert.That(factory.Modules.Activity.CommentCalls, Is.EqualTo(new[]
            {
                (TestUsers.Member.ToActor(), "WEB-7", "  第一行\n\t第二行  "),
            }));
            Assert.That(factory.Modules.People.FindManyCalls.Single().PersonIds, Is.EqualTo(new[] { TestUsers.Member.PersonId }));
        }
    }

    [Test]
    public async Task AddComment_WithReplyTo_PassesItToTheModuleAndReturnsReplyToId()
    {
        // Arrange
        var top = TestData.ActivityId(9);
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Activity.OnAddComment = (actor, key, body) =>
            TestData.Activity(ActivityKind.Commented, actor.PersonId, itemKey: key) with { Comment = body, ReplyToId = top };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Json(new { body = "回覆", replyTo = top }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["replyToId"]?.GetValue<Guid>(), Is.EqualTo(top));
            Assert.That(factory.Modules.Activity.ReplyToCalls, Is.EqualTo(new Guid?[] { top }));
        }
    }

    [Test]
    public async Task AddComment_WithoutReplyTo_PassesNull()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Json(new { body = "留言", replyTo = (Guid?)null }));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(factory.Modules.Activity.ReplyToCalls, Is.EqualTo(new Guid?[] { null }));
    }

    [Test]
    public async Task AddComment_ReplyToNotFound_Returns404WithCommentNotFound()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Activity.OnAddComment = (_, _, _) =>
            new Failure(FailureKind.NotFound, WorkErrorCodes.CommentNotFound, "找不到要回覆的留言。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Json(new { body = "回覆", replyTo = Guid.NewGuid() }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, WorkErrorCodes.CommentNotFound);
    }

    [Test]
    public async Task AddComment_ReplyToNotAGuid_Returns400WithoutCallingTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Document("""{"body":"回覆","replyTo":"不是編號"}"""));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);

        // Assert
        Assert.That(details.Select(d => d?["path"]?.GetValue<string>()), Is.All.EqualTo("replyTo"));
        Assert.That(factory.Modules.Activity.CommentCalls, Is.Empty);
    }

    [Test]
    public async Task AddComment_AuthorNoLongerInTheOrganization_UsesTheFallbackName()
    {
        // Arrange
        var departed = Guid.Parse("0190a000-0000-7000-8000-0000000000d9");
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Activity.OnAddComment = (_, key, body) =>
            TestData.Activity(ActivityKind.Commented, departed, itemKey: key) with { Comment = body };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Json(new { body = "收到" }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        Assert.That(data?["actor"]?["displayName"]?.GetValue<string>(), Is.EqualTo("（已不在組織）"));
    }

    [Test]
    public async Task AddComment_AtTheModuleLimitInChinese_ReachesTheModule()
    {
        // Arrange：一個字 3 個位元組，照網頁的 JSON.stringify 不跳脫中文的寫法組內文。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var body = new string('留', WorkRules.MaxCommentLength);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Document($$"""{"body":"{{body}}"}"""));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(factory.Modules.Activity.CommentCalls.Single().Body, Is.EqualTo(body));
    }

    [Test]
    public async Task AddComment_EmptyBody_IsLeftToTheModuleAndItsRejectionIs422()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Activity.OnAddComment = (_, _, _) =>
            new Failure(FailureKind.Invalid, WorkErrorCodes.CommentBodyRequired, "留言不能是空的。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Json(new { body = string.Empty }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.CommentBodyRequired);
        Assert.That(factory.Modules.Activity.CommentCalls.Single().Body, Is.Empty);
    }

    [Test]
    public async Task AddComment_ControlCharacter_IsLeftToTheModuleAndItsRejectionIs422()
    {
        // Arrange：NUL 由工作引擎擋（PostgreSQL 不收），API 不能在半路把它拿掉或換掉。
        var withNul = "a" + (char)0x0000 + "b";
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Activity.OnAddComment = (_, _, _) =>
            new Failure(FailureKind.Invalid, WorkErrorCodes.CommentBodyInvalid, "留言不能有控制字元。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Json(new { body = withNul }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.CommentBodyInvalid);
        Assert.That(factory.Modules.Activity.CommentCalls.Single().Body, Is.EqualTo(withNul));
    }

    [Test]
    public async Task AddComment_TooLong_Returns422WithTheModuleCode()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Activity.OnAddComment = (_, _, _) =>
            new Failure(FailureKind.Invalid, WorkErrorCodes.CommentBodyTooLong, "留言太長。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(
            Route, ApiRequests.Json(new { body = new string('x', WorkRules.MaxCommentLength + 1) }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.CommentBodyTooLong);
    }

    [TestCase(FailureKind.Forbidden, WorkErrorCodes.Forbidden, HttpStatusCode.Forbidden, TestName = "AddComment_WithoutCommentPermission_Returns403")]
    [TestCase(FailureKind.NotFound, WorkErrorCodes.ItemNotFound, HttpStatusCode.NotFound, TestName = "AddComment_ItemNotVisible_Returns404")]
    public async Task AddComment_ModuleRejects(FailureKind kind, string code, HttpStatusCode expectedStatus)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Activity.OnAddComment = (_, _, _) => new Failure(kind, code, "不行。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Json(new { body = "收到" }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, expectedStatus, code);
        Assert.That(factory.Modules.People.FindManyCalls, Is.Empty);
    }

    // 和其他端點的必填屬性一樣：缺少是根（""），null 或型別不對指到那個屬性。
    [TestCase("{}", "", TestName = "AddComment_BodyMissing_Returns400")]
    [TestCase("""{"body":null}""", "body", TestName = "AddComment_BodyNull_Returns400")]
    [TestCase("""{"body":5}""", "body", TestName = "AddComment_BodyNotAString_Returns400")]
    [TestCase("""{"body":["a"]}""", "body", TestName = "AddComment_BodyAnArray_Returns400")]
    [TestCase("""{"body":"收到","pinned":true}""", "pinned", TestName = "AddComment_UnknownProperty_Returns400")]
    [TestCase("""{"Body":"收到"}""", "Body", TestName = "AddComment_PropertyNameInAnotherCase_Returns400")]
    [TestCase("null", "", TestName = "AddComment_JsonNull_Returns400")]
    public async Task AddComment_BadBody(string body, string expectedPath)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Document(body));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);

        // Assert
        Assert.That(details.Select(d => d?["path"]?.GetValue<string>()), Is.All.EqualTo(expectedPath));
        Assert.That(factory.Modules.Activity.CommentCalls, Is.Empty);
    }

    [TestCase("text/plain", ApiErrorCodes.UnsupportedMediaType)]
    [TestCase(null, ApiErrorCodes.UnsupportedMediaType)]
    [TestCase("application/json; charset=utf-16", ApiErrorCodes.UnsupportedCharset)]
    public async Task AddComment_NotJson_Returns415(string? contentType, string expectedCode)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Document("""{"body":"收到"}""", contentType));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnsupportedMediaType, expectedCode);
        Assert.That(factory.Modules.Activity.CommentCalls, Is.Empty);
    }

    [Test]
    public async Task AddComment_BodyOverTheJsonLimit_Returns413WithoutCallingTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var body = $$"""{"body":"{{new string('x', ApiLimits.MaxJsonBodyBytes)}}"}""";

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Bytes(Encoding.UTF8.GetBytes(body)));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.RequestEntityTooLarge, ApiErrorCodes.PayloadTooLarge);
        Assert.That(factory.Modules.Activity.CommentCalls, Is.Empty);
    }

    [Test]
    public async Task AddComment_SignedInWithoutCsrfHeader_Returns403WithoutCallingTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        client.DefaultRequestHeaders.Remove(ApiRequests.CsrfHeader);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Json(new { body = "收到" }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.CsrfRequired);
        Assert.That(factory.Modules.Activity.CommentCalls, Is.Empty);
    }
}
