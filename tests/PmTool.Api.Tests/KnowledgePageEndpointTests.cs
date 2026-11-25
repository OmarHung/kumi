using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Knowledge;

namespace PmTool.Api.Tests;

/// <summary>知識庫頁面的端點，對假的頁面服務：形狀（web/src/api/knowledgeTypes.ts）、請求怎麼交給服務、失敗的對應。</summary>
[TestFixture]
public sealed class KnowledgePageEndpointTests
{
    private const string Space = "/v1/knowledge/spaces/HR";

    private static readonly string OwnerJson = $$$"""{"personId":"{{{TestUsers.Owner.PersonId}}}","displayName":"Owner Example"}""";
    private static readonly string MemberJson = $$$"""{"personId":"{{{TestUsers.Member.PersonId}}}","displayName":"Member Example"}""";
    private static readonly string SpaceJson =
        $$$"""
        {"id":"{{{KnowledgeSamples.SpaceId}}}","key":"HR","name":"人資","description":"人資的文件","project":null,
         "createdAt":"2026-11-20T03:00:00Z","createdBy":{{{OwnerJson}}},"archivedAt":null,
         "myPermissions":{"canEdit":true,"canComment":true,"canManage":true}}
        """;
    private static readonly string ParentRef = $$$"""{"id":"{{{KnowledgeSamples.ParentId}}}","number":1,"title":"首頁"}""";

    [Test]
    public async Task Tree_ReturnsTheSpaceAndThePagesInOrderWithUpdaterNames()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Space}/tree");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expected = JsonNode.Parse(
            $$$"""
            {"space":{{{SpaceJson}}},"pages":[
              {"id":"{{{KnowledgeSamples.ParentId}}}","number":1,"parentId":null,"position":0,"title":"首頁","isRestricted":false,"updatedAt":"2026-11-20T03:00:00Z","updatedBy":{{{OwnerJson}}}},
              {"id":"{{{KnowledgeSamples.PageId}}}","number":2,"parentId":"{{{KnowledgeSamples.ParentId}}}","position":0,"title":"新進人員","isRestricted":true,"updatedAt":"2026-11-20T04:00:00Z","updatedBy":{{{MemberJson}}}}]}
            """);
        Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
    }

    [Test]
    public async Task Get_ReturnsTheContractShape()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Space}/pages/2");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expected = JsonNode.Parse(
            $$$"""
            {"id":"{{{KnowledgeSamples.PageId}}}","space":{{{SpaceJson}}},"number":2,"title":"新進人員","body":"內文","revision":3,
             "ancestors":[{{{ParentRef}}}],"isRestricted":true,"restrictedAncestors":[{{{ParentRef}}}],
             "createdAt":"2026-11-20T03:00:00Z","createdBy":{{{OwnerJson}}},"updatedAt":"2026-11-20T04:00:00Z","updatedBy":{{{MemberJson}}},
             "versionCount":4,"myPermissions":{"canEdit":true,"canComment":true,"canDelete":false}}
            """);
        Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
    }

    [TestCase("0")]
    [TestCase("-1")]
    [TestCase("abc")]
    [TestCase("1.5")]
    [TestCase("99999999999")]
    public async Task EveryPageEndpoint_WithANumberThatIsNotAPositiveInteger_Returns404WithoutCallingTheService(string number)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var page = $"{Space}/pages/{number}";

        // Act
        var responses = new[]
        {
            await client.GetAsync(page),
            await client.PatchAsync(page, ApiRequests.Json(new { title = "x", expectedRevision = 1 })),
            await client.PostAsync($"{page}/move", ApiRequests.Json(new { parentNumber = (int?)null, beforeNumber = (int?)null })),
            await client.DeleteAsync(page),
            await client.PostAsync($"{page}/restore", null),
            await client.GetAsync($"{page}/restriction"),
            await client.PutAsync($"{page}/restriction", ApiRequests.Json(new { people = (Guid[]?)null })),
            await client.GetAsync($"{page}/attachments"),
        };

        // Assert
        try
        {
            foreach (var response in responses)
            {
                await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, KnowledgeErrorCodes.PageNotFound);
            }

            Assert.That(factory.Modules.KnowledgePages.Calls, Is.Empty);
            Assert.That(factory.Modules.KnowledgePages.UpdateCalls, Is.Empty);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Test]
    public async Task Create_Returns201WithLocationAndPassesTheParentBodyAndPosition()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(
            $"{Space}/pages", ApiRequests.Json(new { parentNumber = 1, title = "新頁", body = "## 內文", beforeNumber = 5 }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Headers.Location?.ToString(), Is.EqualTo("/v1/knowledge/spaces/HR/pages/2"));
            Assert.That(factory.Modules.KnowledgePages.CreateCalls.Single().Page, Is.EqualTo(new NewPage(1, "新頁", "## 內文") { BeforeNumber = 5 }));
            Assert.That(data?["revision"]?.GetValue<long>(), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Create_TopLevelWithNullsOrOmitted_PassesNullParentAndBody()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var explicitNulls = await client.PostAsync(
            $"{Space}/pages", ApiRequests.Json(new { parentNumber = (int?)null, title = "甲", body = (string?)null, beforeNumber = (int?)null }));
        using var omitted = await client.PostAsync($"{Space}/pages", ApiRequests.Json(new { title = "乙" }));

        // Assert
        Assert.That(
            factory.Modules.KnowledgePages.CreateCalls.Select(c => c.Page).OrderBy(p => p.Title, StringComparer.Ordinal).ToArray(),
            Is.EquivalentTo(new[] { new NewPage(null, "甲", null), new NewPage(null, "乙", null) }));
    }

    [Test]
    public async Task Update_PassesOnlyTheFieldsGivenAndTheExpectedRevision()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PatchAsync($"{Space}/pages/2", ApiRequests.Json(new { body = "新內文", expectedRevision = 3 }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.Modules.KnowledgePages.UpdateCalls.Single().Changes, Is.EqualTo(new PageChanges(null, "新內文", 3)));
            Assert.That((data?["body"]?.GetValue<string>(), data?["revision"]?.GetValue<long>()), Is.EqualTo(("新內文", 4L)));
        }
    }

    [Test]
    public async Task Update_NullFieldsAreTheSameAsOmitted_AndAnEmptyBodyIsPassedAsEmpty()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var nulls = await client.PatchAsync(
            $"{Space}/pages/2", ApiRequests.Json(new { title = (string?)null, body = (string?)null, expectedRevision = 3 }));
        using var cleared = await client.PatchAsync($"{Space}/pages/2", ApiRequests.Json(new { body = "", expectedRevision = 4 }));

        // Assert
        Assert.That(
            factory.Modules.KnowledgePages.UpdateCalls.Select(c => c.Changes).OrderBy(c => c.ExpectedRevision).ToArray(),
            Is.EqualTo(new[] { new PageChanges(null, null, 3), new PageChanges(null, "", 4) }));
    }

    [Test]
    public async Task Update_WithoutExpectedRevision_Returns422NamingTheField()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PatchAsync($"{Space}/pages/2", ApiRequests.Json(new { title = "新標題" }));

        // Assert
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, "knowledge.page_revision_required");
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { "knowledge.page_revision_required|expectedRevision" }));
        Assert.That(factory.Modules.KnowledgePages.UpdateCalls, Is.Empty);
    }

    [TestCase(FailureKind.Conflict, KnowledgeErrorCodes.PageConflict, HttpStatusCode.Conflict)]
    [TestCase(FailureKind.Conflict, KnowledgeErrorCodes.SpaceArchived, HttpStatusCode.Conflict)]
    [TestCase(FailureKind.Forbidden, KnowledgeErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    [TestCase(FailureKind.NotFound, KnowledgeErrorCodes.PageNotFound, HttpStatusCode.NotFound)]
    [TestCase(FailureKind.Invalid, KnowledgeErrorCodes.PageBodySecret, HttpStatusCode.UnprocessableEntity)]
    public async Task Update_ServiceFailure_IsMappedToTheStatusAndCode(FailureKind kind, string code, HttpStatusCode status)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.KnowledgePages.OnUpdate = _ => new Failure(kind, code, "失敗");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PatchAsync($"{Space}/pages/2", ApiRequests.Json(new { body = "x", expectedRevision = 1 }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, status, code);
    }

    [Test]
    public async Task Move_PassesTheNewPositionAndReturnsTheTree()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(
            $"{Space}/pages/2/move", ApiRequests.Json(new { parentNumber = (int?)null, beforeNumber = 1 }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.Modules.KnowledgePages.MoveCalls.Single().Move, Is.EqualTo(new PageMove(null, 1)));
            Assert.That(data?["pages"]?.AsArray(), Has.Count.EqualTo(2));
            Assert.That(data?["space"]?["key"]?.GetValue<string>(), Is.EqualTo("HR"));
        }
    }

    [TestCase(FailureKind.Invalid, KnowledgeErrorCodes.PagePositionInvalid, HttpStatusCode.UnprocessableEntity)]
    [TestCase(FailureKind.Invalid, KnowledgeErrorCodes.PageTooDeep, HttpStatusCode.UnprocessableEntity)]
    public async Task Move_ServiceFailure_IsMappedToTheStatusAndCode(FailureKind kind, string code, HttpStatusCode status)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.KnowledgePages.OnMove = () => new Failure(kind, code, "失敗");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync($"{Space}/pages/2/move", ApiRequests.Json(new { parentNumber = 1, beforeNumber = (int?)null }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, status, code);
    }

    [Test]
    public async Task Delete_ReturnsTheBatchWithTheTopPageAndTheCount()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.DeleteAsync($"{Space}/pages/2");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expected = JsonNode.Parse(
            $$$"""{"deletionId":"0190a000-0000-7000-8000-0000000000e1","page":{"id":"{{{KnowledgeSamples.PageId}}}","number":2,"title":"首頁"},"pageCount":3}""");
        Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
    }

    [Test]
    public async Task Delete_HiddenDescendants_Returns409()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.KnowledgePages.OnDelete = _ => new Failure(FailureKind.Conflict, KnowledgeErrorCodes.PageHasHiddenDescendants, "有看不到的頁面");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.DeleteAsync($"{Space}/pages/2");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Conflict, KnowledgeErrorCodes.PageHasHiddenDescendants);
    }

    [Test]
    public async Task Delete_TrashFull_Returns409()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.KnowledgePages.OnDelete = _ => new Failure(FailureKind.Conflict, KnowledgeErrorCodes.TrashFull, "垃圾桶滿了");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.DeleteAsync($"{Space}/pages/2");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Conflict, KnowledgeErrorCodes.TrashFull);
    }

    [Test]
    public async Task Trash_PassesTheLimitAndCursorAndShowsTheNextCursorAndCapacity()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.KnowledgePages.TrashNextCursor = "下一頁";
        factory.Modules.KnowledgePages.TrashCapacity = new TrashCapacity(812, 1000);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Space}/trash?limit=20&cursor=abc");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.Modules.KnowledgePages.TrashQueries.Single().Query, Is.EqualTo(new TrashQuery("abc", 20)));
            Assert.That(data?["nextCursor"]?.GetValue<string>(), Is.EqualTo("下一頁"));
            Assert.That(JsonNode.DeepEquals(data?["capacity"], JsonNode.Parse("""{"batches":812,"maxBatches":1000}""")), Is.True);
        }
    }

    [Test]
    public async Task Trash_WithoutQueryParameters_UsesTheDefaultLimit()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Space}/trash");
        await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(factory.Modules.KnowledgePages.TrashQueries.Single().Query, Is.EqualTo(new TrashQuery(null, KnowledgeRules.DefaultTrashPageSize)));
    }

    [TestCase("limit=abc")]
    [TestCase("limit=1&limit=2")]
    [TestCase("cursor=a&cursor=b")]
    public async Task Trash_MalformedQuery_Returns422WithoutCallingTheService(string query)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Space}/trash?{query}");

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, "query.invalid");
        Assert.That(factory.Modules.KnowledgePages.TrashQueries, Is.Empty);
    }

    [Test]
    public async Task TrashPurge_ReturnsTheCountsAndPassesTheScope()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var batch = Guid.NewGuid();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var one = await client.PostAsync($"{Space}/trash/purge", ApiRequests.Json(new { deletionId = batch }));
        var oneData = await ApiRequests.AssertSuccessEnvelopeAsync(one, HttpStatusCode.OK);
        using var expired = await client.PostAsync($"{Space}/trash/purge", ApiRequests.Json(new { expired = true }));
        var expiredData = await ApiRequests.AssertSuccessEnvelopeAsync(expired, HttpStatusCode.OK);

        // Assert
        var calls = factory.Modules.KnowledgePages.PurgeCalls;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(calls.Select(c => c.Request.DeletionId), Is.EqualTo(new Guid?[] { batch, null }));
            Assert.That(
                JsonNode.DeepEquals(oneData, JsonNode.Parse("""{"purgedBatches":1,"purgedPages":5,"purgedAttachments":3,"remainingBatches":0,"hasMoreExpired":false}""")),
                Is.True,
                oneData?.ToJsonString());
            Assert.That(
                JsonNode.DeepEquals(expiredData, JsonNode.Parse("""{"purgedBatches":2,"purgedPages":5,"purgedAttachments":3,"remainingBatches":0,"hasMoreExpired":true}""")),
                Is.True,
                expiredData?.ToJsonString());
        }
    }

    [TestCase("{}")]
    [TestCase("""{"expired":false}""")]
    [TestCase("""{"deletionId":null}""")]
    [TestCase("""{"deletionId":null,"expired":false}""")]
    [TestCase("""{"deletionId":"6f1f0e5a-8f3c-4a5e-9d35-0f2f2f2f2f2f","expired":true}""")]
    [TestCase("""{"deletionId":"6f1f0e5a-8f3c-4a5e-9d35-0f2f2f2f2f2f","expired":false}""")]
    public async Task TrashPurge_WithoutExactlyOneScope_IsRejectedAndDeletesNothing(string json)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Act
        using var response = await client.PostAsync($"{Space}/trash/purge", content);

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, KnowledgeErrorCodes.PurgeScopeInvalid);
        Assert.That(factory.Modules.KnowledgePages.PurgeCalls, Is.Empty);
    }

    [TestCase(FailureKind.NotFound, KnowledgeErrorCodes.PageNotFound, HttpStatusCode.NotFound)]
    [TestCase(FailureKind.Forbidden, KnowledgeErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    [TestCase(FailureKind.Conflict, KnowledgeErrorCodes.SpaceArchived, HttpStatusCode.Conflict)]
    public async Task TrashPurge_Failures_MapToTheirStatus(FailureKind kind, string code, HttpStatusCode status)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.KnowledgePages.OnPurge = _ => new Failure(kind, code, "失敗");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync($"{Space}/trash/purge", ApiRequests.Json(new { expired = true }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, status, code);
    }

    [Test]
    public async Task Restore_ReturnsThePage()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync($"{Space}/pages/2/restore", null);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?["number"]?.GetValue<int>(), Is.EqualTo(2));
        Assert.That(factory.Modules.KnowledgePages.Calls.Single(), Is.EqualTo(("restore", "HR", 2)));
    }

    [Test]
    public async Task Trash_ReturnsTheBatchesWithFormerParentAndWhoDeleted()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Space}/trash");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expected = JsonNode.Parse(
            $$$"""
            {"items":[{"deletionId":"0190a000-0000-7000-8000-0000000000e1","page":{"id":"{{{KnowledgeSamples.PageId}}}","number":2,"title":"舊頁"},
              "pageCount":3,"formerParent":{{{ParentRef}}},"deletedAt":"2026-11-20T03:00:00Z","deletedBy":{{{MemberJson}}}}],
             "nextCursor":null,"capacity":null}
            """);
        Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
    }

    [Test]
    public async Task Trash_TopLevelBatch_HasANullFormerParent()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.KnowledgePages.OnTrash = () =>
            [new TrashEntry(Guid.NewGuid(), KnowledgeSamples.Ref(2), 1, null, KnowledgeSamples.At, TestUsers.Member.PersonId)];
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Space}/trash");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?["items"]?[0]?["formerParent"], Is.Null);
    }

    [Test]
    public async Task Restriction_Get_ReplacesPersonIdsWithPeopleAndKeepsTheListOrder()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var gone = Guid.NewGuid();
        factory.Modules.KnowledgePages.OnGetRestriction = () =>
            new PageRestriction([TestUsers.Member.PersonId, gone, TestUsers.Owner.PersonId], [KnowledgeSamples.Ref()]);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Space}/pages/2/restriction");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expected = JsonNode.Parse(
            $$$"""{"people":[{{{MemberJson}}},{"personId":"{{{gone}}}","displayName":"（已不在組織）"},{{{OwnerJson}}}],"restrictedAncestors":[{{{ParentRef}}}],"restrictedBy":null,"canRelax":false}""");
        Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
    }

    [Test]
    public async Task Restriction_Get_ShowsWhoSetTheRestrictionAndWhetherTheCallerCanRelaxIt()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.KnowledgePages.OnGetRestriction = () =>
            new PageRestriction([TestUsers.Owner.PersonId, TestUsers.Member.PersonId], []) { RestrictedBy = TestUsers.Owner.PersonId, CanRelax = true };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Space}/pages/2/restriction");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data?["restrictedBy"], JsonNode.Parse(OwnerJson)), Is.True, data?.ToJsonString());
            Assert.That(data?["canRelax"]?.GetValue<bool>(), Is.True);
        }
    }

    [Test]
    public async Task Restriction_Put_NotTheOwner_Returns403WithTheOwnerRequiredCode()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.KnowledgePages.OnSetRestriction = _ => new Failure(FailureKind.Forbidden, KnowledgeErrorCodes.RestrictionOwnerRequired, "只有設定的人");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PutAsync($"{Space}/pages/2/restriction", ApiRequests.Json(new { people = (Guid[]?)null }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, KnowledgeErrorCodes.RestrictionOwnerRequired);
    }

    [Test]
    public async Task Restriction_Put_PassesThePeopleAndNullRemovesTheRestriction()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var set = await client.PutAsync(
            $"{Space}/pages/2/restriction", ApiRequests.Json(new { people = new[] { TestUsers.Owner.PersonId } }));
        var setData = await ApiRequests.AssertSuccessEnvelopeAsync(set, HttpStatusCode.OK);
        using var removed = await client.PutAsync($"{Space}/pages/2/restriction", ApiRequests.Json(new { people = (Guid[]?)null }));
        var removedData = await ApiRequests.AssertSuccessEnvelopeAsync(removed, HttpStatusCode.OK);

        // Assert
        var calls = factory.Modules.KnowledgePages.RestrictionCalls;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(calls[0].People, Is.EqualTo(new[] { TestUsers.Owner.PersonId }));
            Assert.That(calls[1].People, Is.Null);
            Assert.That(setData?["people"]?[0]?["displayName"]?.GetValue<string>(), Is.EqualTo("Owner Example"));
            Assert.That(removedData?["people"], Is.Null);
        }
    }

    [Test]
    public async Task Restriction_Put_InvalidPeople_Returns422WithThePath()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.KnowledgePages.OnSetRestriction = _ => new Failure(FailureKind.Invalid, KnowledgeErrorCodes.RestrictionInvalid, "不在組織")
        {
            Details = [new FailureDetail(KnowledgeErrorCodes.RestrictionInvalid, "people[0]", "不在組織")],
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PutAsync($"{Space}/pages/2/restriction", ApiRequests.Json(new { people = new[] { Guid.NewGuid() } }));

        // Assert
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, KnowledgeErrorCodes.RestrictionInvalid);
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{KnowledgeErrorCodes.RestrictionInvalid}|people[0]" }));
    }

    [Test]
    public async Task Create_PageLimit_Returns409AndUnknownSpaceReturns404()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.KnowledgePages.OnCreate = _ => new Failure(FailureKind.Conflict, KnowledgeErrorCodes.PageLimitReached, "滿了");
        factory.Modules.KnowledgePages.OnTree = () => KnowledgeSamples.NotFound(KnowledgeErrorCodes.SpaceNotFound);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var full = await client.PostAsync($"{Space}/pages", ApiRequests.Json(new { parentNumber = (int?)null, title = "x" }));
        using var missing = await client.GetAsync("/v1/knowledge/spaces/NOPE/tree");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(full, HttpStatusCode.Conflict, KnowledgeErrorCodes.PageLimitReached);
        await ApiRequests.AssertErrorEnvelopeAsync(missing, HttpStatusCode.NotFound, KnowledgeErrorCodes.SpaceNotFound);
    }

    [Test]
    public async Task EveryEndpoint_WithoutSigningIn_Returns401()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();

        // Act
        using var tree = await client.GetAsync($"{Space}/tree");
        using var get = await client.GetAsync($"{Space}/pages/2");
        using var patch = await client.PatchAsync($"{Space}/pages/2", ApiRequests.Json(new { expectedRevision = 1 }));
        using var delete = await client.DeleteAsync($"{Space}/pages/2");

        // Assert
        Assert.That(new[] { tree, get, patch, delete }.Select(r => r.StatusCode), Is.All.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(factory.Modules.KnowledgePages.UpdateCalls, Is.Empty);
    }
}
