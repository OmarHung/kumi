using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>留言的編輯、刪除、復原與版本端點：形狀、轉交給工作引擎的參數，以及失敗的對應。</summary>
[TestFixture]
public sealed class CommentEditEndpointTests
{
    private static readonly Guid CommentId = TestData.ActivityId(5);
    private static readonly string Base = $"/v1/items/WEB-7/comments/{CommentId:D}";

    [Test]
    public async Task EditComment_Accepted_PassesTheBodyAndReturns200WithTheActivityShape()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Activity.OnEditComment = (actor, key, id, body) =>
            TestData.Activity(ActivityKind.Commented, actor.PersonId, itemKey: key) with { Id = id, Comment = body, EditedAt = TestData.FirstActivityAt.AddHours(1) };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PatchAsync(Base, ApiRequests.Json(new { body = "  新的內容 " }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["comment"]?.GetValue<string>(), Is.EqualTo("  新的內容 "));
            Assert.That(data?["editedAt"]?.GetValue<DateTimeOffset>(), Is.EqualTo(TestData.FirstActivityAt.AddHours(1)));
            Assert.That(data?["deletedAt"], Is.Null);
            Assert.That(data?["deletedBy"], Is.Null);
            Assert.That(factory.Modules.Activity.CommentEditCalls, Is.EqualTo(new[]
            {
                new CommentCall("edit", TestUsers.Member.ToActor(), "WEB-7", CommentId, "  新的內容 "),
            }));
        }
    }

    [Test]
    public async Task DeleteComment_Accepted_Returns200WithThePlaceholderAndWhoDeletedIt()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Activity.OnDeleteComment = (actor, key, id) =>
            TestData.Activity(ActivityKind.Commented, TestUsers.Owner.PersonId, itemKey: key) with
            {
                Id = id,
                DeletedAt = TestData.FirstActivityAt.AddHours(2),
                DeletedBy = actor.PersonId,
            };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.DeleteAsync(Base);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expectedDeletedBy = new JsonObject { ["personId"] = TestUsers.Member.PersonId, ["displayName"] = TestUsers.Member.DisplayName };
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["comment"], Is.Null);
            Assert.That(data?["deletedAt"]?.GetValue<DateTimeOffset>(), Is.EqualTo(TestData.FirstActivityAt.AddHours(2)));
            Assert.That(JsonNode.DeepEquals(data?["deletedBy"], expectedDeletedBy), Is.True, data?["deletedBy"]?.ToJsonString());
            Assert.That(data?["actor"]?["displayName"]?.GetValue<string>(), Is.EqualTo(TestUsers.Owner.DisplayName));
            Assert.That(factory.Modules.Activity.CommentEditCalls.Single().Action, Is.EqualTo("delete"));
        }
    }

    [Test]
    public async Task RestoreComment_Accepted_Returns200WithTheCommentBack()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync($"{Base}/restore", content: null);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["comment"]?.GetValue<string>(), Is.EqualTo("復原的內容"));
            Assert.That(data?["deletedAt"], Is.Null);
            Assert.That(factory.Modules.Activity.CommentEditCalls.Single(), Is.EqualTo(new CommentCall("restore", TestUsers.Member.ToActor(), "WEB-7", CommentId, null)));
        }
    }

    [Test]
    public async Task ListRevisions_Returns200WithTheVersionsNewestFirst()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Base}/revisions");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expected = new JsonObject
        {
            ["items"] = new JsonArray(
                new JsonObject { ["number"] = 2, ["body"] = "新的", ["at"] = "2026-11-14T07:02:00Z", ["isCurrent"] = true },
                new JsonObject { ["number"] = 1, ["body"] = "舊的", ["at"] = "2026-11-14T07:00:00Z", ["isCurrent"] = false }),
        };
        Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
        Assert.That(factory.Modules.Activity.CommentEditCalls.Single().Action, Is.EqualTo("revisions"));
    }

    [TestCase("PATCH")]
    [TestCase("DELETE")]
    [TestCase("POST")]
    [TestCase("GET")]
    public async Task CommentRoutes_CommentIdThatIsNotAGuid_Returns404WithoutCallingTheModule(string method)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var route = method switch
        {
            "POST" => "/v1/items/WEB-7/comments/not-a-guid/restore",
            "GET" => "/v1/items/WEB-7/comments/not-a-guid/revisions",
            _ => "/v1/items/WEB-7/comments/not-a-guid",
        };
        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (method == "PATCH")
        {
            request.Content = ApiRequests.Json(new { body = "x" });
        }

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, WorkErrorCodes.CommentNotFound);
        Assert.That(factory.Modules.Activity.CommentEditCalls, Is.Empty);
    }

    [TestCase(FailureKind.Forbidden, WorkErrorCodes.CommentNotAuthor, HttpStatusCode.Forbidden)]
    [TestCase(FailureKind.Forbidden, WorkErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    [TestCase(FailureKind.NotFound, WorkErrorCodes.ItemNotFound, HttpStatusCode.NotFound)]
    [TestCase(FailureKind.NotFound, WorkErrorCodes.CommentNotFound, HttpStatusCode.NotFound)]
    [TestCase(FailureKind.Conflict, WorkErrorCodes.ProjectArchived, HttpStatusCode.Conflict)]
    [TestCase(FailureKind.Conflict, WorkErrorCodes.CommentDeleted, HttpStatusCode.Conflict)]
    [TestCase(FailureKind.Conflict, WorkErrorCodes.CommentTooManyEdits, HttpStatusCode.Conflict)]
    public async Task CommentRoutes_ModuleRejects_MapsTheFailureOnEveryRoute(FailureKind kind, string code, HttpStatusCode expectedStatus)
    {
        // Arrange
        var failure = new Failure(kind, code, "不行。");
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Activity.OnEditComment = (_, _, _, _) => failure;
        factory.Modules.Activity.OnDeleteComment = (_, _, _) => failure;
        factory.Modules.Activity.OnRestoreComment = (_, _, _) => failure;
        factory.Modules.Activity.OnListCommentRevisions = (_, _, _) => failure;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        var responses = new[]
        {
            await client.PatchAsync(Base, ApiRequests.Json(new { body = "x" })),
            await client.DeleteAsync(Base),
            await client.PostAsync($"{Base}/restore", content: null),
            await client.GetAsync($"{Base}/revisions"),
        };

        // Assert
        foreach (var response in responses)
        {
            using (response)
            {
                await ApiRequests.AssertErrorEnvelopeAsync(response, expectedStatus, code);
            }
        }

        Assert.That(factory.Modules.People.FindManyCalls, Is.Empty);
    }

    [TestCase(WorkErrorCodes.CommentBodyRequired)]
    [TestCase(WorkErrorCodes.CommentBodyTooLong)]
    [TestCase(WorkErrorCodes.CommentBodyInvalid)]
    public async Task EditComment_InvalidBody_Returns422WithTheCommentCode(string code)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Activity.OnEditComment = (_, _, _, _) => BodyFailures.InvalidBody(code);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PatchAsync(Base, ApiRequests.Json(new { body = "x" }));

        // Assert
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, code);
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{code}|body" }));
    }

    [TestCase("{}")]
    [TestCase("""{"body":null}""")]
    [TestCase("""{"body":5}""")]
    [TestCase("""{"body":"收到","pinned":true}""")]
    public async Task EditComment_BadBody_Returns400WithoutCallingTheModule(string body)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PatchAsync(Base, ApiRequests.Document(body));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(factory.Modules.Activity.CommentEditCalls, Is.Empty);
    }
}

/// <summary>測試裡造出「內文的問題」這種失敗，和工作引擎的 <c>CommentRules</c> 同一個形狀（Details 的路徑是 body）。</summary>
file static class BodyFailures
{
    public static Failure InvalidBody(string code) =>
        new(FailureKind.Invalid, code, "內文不對。") { Details = [new FailureDetail(code, "body", "內文不對。")] };
}
