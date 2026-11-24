using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Blueprint;
using PmTool.Blueprint.Model;
using PmTool.Blueprint.Planning;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>變更集回應的 JSON 形狀：變更集本身、差異、資料影響、擋住的原因，以及人名的補齊。</summary>
[TestFixture]
public sealed class ChangeSetShapeTests
{
    private const string Route = "/v1/projects/WEB/changesets";

    private static readonly Guid Departed = Guid.Parse("0190a000-0000-7000-8000-0000000000d9");

    [Test]
    public async Task Get_PendingChangeSet_HasTheContractShape()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        var data = await GetDetailAsync(client, 1);

        // Assert
        var expected = new JsonObject
        {
            ["changeSet"] = ExpectedChangeSet(1, "pending", TestUsers.Member, "2026-11-15T02:00:00Z"),
            ["blueprint"] = JsonNode.Parse(BlueprintSerializer.ToJson(TestData.Blueprint())),
            ["baseBlueprint"] = JsonNode.Parse(BlueprintSerializer.ToJson(TestData.Blueprint())),
            ["changes"] = new JsonArray(),
            ["impacts"] = new JsonArray(),
            ["blockers"] = new JsonArray(),
            ["canApply"] = true,
        };
        Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
    }

    [Test]
    public async Task Get_AppliedChangeSet_ReturnsTheBaseBlueprintSeparatelyFromTheProposedOne()
    {
        // Arrange：已套用的變更集，網頁要拿基準版本當「改之前」；目前的藍圖已經是改之後的樣子。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var before = TestData.Blueprint() with { Metadata = TestData.Blueprint().Metadata with { Name = "改名之前" } };
        factory.Modules.Changes.OnGet = call => TestData.ChangeSetDetail(TestData.Decided(
            TestData.ChangeSet(call.Number, TestUsers.Owner.PersonId), ChangeSetStatus.Applied, TestUsers.Owner.PersonId)) with
        { BaseBlueprint = before };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        var data = await GetDetailAsync(client, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["baseBlueprint"]?["metadata"]?["name"]?.GetValue<string>(), Is.EqualTo("改名之前"));
            Assert.That(data?["blueprint"]?["metadata"]?["name"]?.GetValue<string>(), Is.EqualTo("Example 網站"));
        }
    }

    [Test]
    public async Task Apply_AppliedChangeSet_HasTheDeciderTheDecisionTimeAndTheNewVersion()
    {
        // Arrange：提出的是另一個人，決定的人是呼叫者。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Changes.OnApply = call => TestData.ChangeSetDetail(TestData.Decided(
            TestData.ChangeSet(call.Number, TestUsers.Owner.PersonId), ChangeSetStatus.Applied, call.Actor.PersonId));
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync($"{Route}/2/apply", null);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expected = ExpectedChangeSet(2, "applied", TestUsers.Owner, "2026-11-15T03:00:00Z");
        expected["appliedVersion"] = 2;
        expected["decidedBy"] = Person(TestUsers.Member.PersonId, TestUsers.Member.DisplayName);
        expected["decidedAt"] = "2026-11-15T03:30:00Z";
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data?["changeSet"], expected), Is.True, data?["changeSet"]?.ToJsonString());
            Assert.That(data?["canApply"]?.GetValue<bool>(), Is.False);
        }
    }

    [Test]
    public async Task Get_ProposerAndDecider_AreLookedUpTogetherInOneCall()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Changes.OnGet = call => TestData.ChangeSetDetail(TestData.Decided(
            TestData.ChangeSet(call.Number, TestUsers.Guest.PersonId), ChangeSetStatus.Rejected, TestUsers.Owner.PersonId));
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        var data = await GetDetailAsync(client, 5);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["changeSet"]?["status"]?.GetValue<string>(), Is.EqualTo("rejected"));
            Assert.That(data?["changeSet"]?["proposedBy"]?["displayName"]?.GetValue<string>(), Is.EqualTo(TestUsers.Guest.DisplayName));
            Assert.That(data?["changeSet"]?["decidedBy"]?["displayName"]?.GetValue<string>(), Is.EqualTo(TestUsers.Owner.DisplayName));
            Assert.That(data?["changeSet"]?["appliedVersion"], Is.Null);
            var lookup = factory.Modules.People.FindManyCalls.Single();
            Assert.That(lookup.OrganizationId, Is.EqualTo(TestUsers.OrganizationId));
            Assert.That(lookup.PersonIds, Is.EquivalentTo(new[] { TestUsers.Guest.PersonId, TestUsers.Owner.PersonId }));
        }
    }

    [Test]
    public async Task Get_ProposedByTheAssistant_SaysSoInOrigin_AndTheProposerStaysThePerson()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Changes.OnGet = call => TestData.ChangeSetDetail(
            TestData.ChangeSet(call.Number, TestUsers.Member.PersonId) with { Origin = ChangeSetOrigin.Assistant });
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        var data = await GetDetailAsync(client, 4);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["changeSet"]?["origin"]?.GetValue<string>(), Is.EqualTo("assistant"));
            Assert.That(data?["changeSet"]?["proposedBy"]?["personId"]?.GetValue<Guid>(), Is.EqualTo(TestUsers.Member.PersonId));
        }
    }

    [Test]
    public async Task Get_ProposedByAnAgent_SaysSoInOrigin_AndTheProposerStaysThePerson()
    {
        // Arrange：使用者自己的 AI 代理（Claude Code、Codex）透過 MCP 提出的，JSON 的值是 agent。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Changes.OnGet = call => TestData.ChangeSetDetail(
            TestData.ChangeSet(call.Number, TestUsers.Member.PersonId) with { Origin = ChangeSetOrigin.Agent });
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        var data = await GetDetailAsync(client, 5);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["changeSet"]?["origin"]?.GetValue<string>(), Is.EqualTo("agent"));
            Assert.That(data?["changeSet"]?["proposedBy"]?["personId"]?.GetValue<Guid>(), Is.EqualTo(TestUsers.Member.PersonId));
        }
    }

    [Test]
    public async Task List_EachChangeSetCarriesItsOrigin()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Changes.OnList = (_, _) => new[]
        {
            TestData.ChangeSet(3, TestUsers.Member.PersonId) with { Origin = ChangeSetOrigin.Agent },
            TestData.ChangeSet(2, TestUsers.Member.PersonId) with { Origin = ChangeSetOrigin.Assistant },
            TestData.ChangeSet(1, TestUsers.Member.PersonId),
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data!.AsArray().Select(item => item?["origin"]?.GetValue<string>()), Is.EqualTo(new[] { "agent", "assistant", "direct" }));
    }

    [Test]
    public async Task List_EveryChangeSet_HasTheContractShapeAndAllPeopleAreLookedUpOnce()
    {
        // Arrange：其中一個人已經不在組織，名稱給替代文字，整個清單照樣回來。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Changes.OnList = (_, _) => new[]
        {
            TestData.ChangeSet(3, TestUsers.Guest.PersonId),
            TestData.Decided(TestData.ChangeSet(2, Departed), ChangeSetStatus.Expired, TestUsers.Owner.PersonId) with
            {
                DecidedBy = null,
            },
            TestData.Decided(TestData.ChangeSet(1, TestUsers.Owner.PersonId), ChangeSetStatus.Applied, TestUsers.Member.PersonId),
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expired = ExpectedChangeSet(2, "expired", null, "2026-11-15T03:00:00Z", Departed);
        expired["decidedAt"] = "2026-11-15T03:30:00Z";
        var applied = ExpectedChangeSet(1, "applied", TestUsers.Owner, "2026-11-15T02:00:00Z");
        applied["appliedVersion"] = 2;
        applied["decidedBy"] = Person(TestUsers.Member.PersonId, TestUsers.Member.DisplayName);
        applied["decidedAt"] = "2026-11-15T02:30:00Z";
        var expected = new JsonArray(ExpectedChangeSet(3, "pending", TestUsers.Guest, "2026-11-15T04:00:00Z"), expired, applied);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
            Assert.That(factory.Modules.People.FindManyCalls.Single().PersonIds, Is.EquivalentTo(new[]
            {
                TestUsers.Guest.PersonId, Departed, TestUsers.Owner.PersonId, TestUsers.Member.PersonId,
            }));
        }
    }

    [Test]
    public async Task List_NoChangeSets_ReturnsAnEmptyArrayWithoutLookingUpPeople()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(JsonNode.DeepEquals(data, new JsonArray()), Is.True, data?.ToJsonString());
        Assert.That(factory.Modules.People.FindManyCalls, Is.Empty);
    }

    [Test]
    public async Task Get_Changes_HaveKindResourceKeyParentKeyAndChangedProperties()
    {
        // Arrange：位置（currentPath、newPath）是計畫內部用的，不在合約裡。
        var changes = new[]
        {
            Change(ChangeKind.Added, ResourceKind.Field, "budget"),
            Change(ChangeKind.Removed, ResourceKind.State, "review") with { ParentKey = "main", CurrentPath = "/workflows/0/states/2" },
            Change(ChangeKind.Modified, ResourceKind.FieldOption, "high") with
            {
                ParentKey = "impact",
                ChangedProperties = ["name", "color"],
                CurrentPath = "/fields/0/options/1",
                NewPath = "/fields/0/options/1",
            },
        };
        using var factory = DetailFactory(detail => detail with { Changes = changes });
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        var data = await GetDetailAsync(client, 1);

        // Assert
        var expected = new JsonArray(
            ExpectedChange("added", "field", "budget", null),
            ExpectedChange("removed", "state", "review", "main"),
            ExpectedChange("modified", "field_option", "high", "impact", "name", "color"));
        Assert.That(JsonNode.DeepEquals(data?["changes"], expected), Is.True, data?["changes"]?.ToJsonString());
    }

    [Test]
    public async Task Get_EveryResourceKind_IsWrittenInSnakeCase()
    {
        // Arrange
        var changes = Enum.GetValues<ResourceKind>().Select(resource => Change(ChangeKind.Modified, resource, "key")).ToArray();
        using var factory = DetailFactory(detail => detail with { Changes = changes });
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        var data = await GetDetailAsync(client, 1);

        // Assert
        Assert.That(data?["changes"]?.AsArray().Select(c => c?["resource"]?.GetValue<string>()), Is.EqualTo(new[]
        {
            "blueprint", "metadata", "field", "field_option", "workflow", "state",
            "transition", "item_type", "view", "automation", "role", "binding",
        }));
    }

    [Test]
    public async Task Get_EachImpact_CarriesTheSharedPropertiesAndOnlyItsOwn()
    {
        // Arrange
        PlannedImpact[] impacts =
        [
            new(new StateMoveImpact("main", "review", "internal_review"), 12, false),
            new(new FieldDropImpact("legacy"), 4, false),
            new(new FieldConversionImpact("budget", FieldType.Text, FieldType.Number, ConversionFailurePolicy.LeaveEmpty), 7, false),
            new(new FieldConversionImpact("notes", FieldType.Text, FieldType.LongText, null), 9, false),
            new(new FieldConversionImpact("size", FieldType.Text, FieldType.SingleSelect, ConversionFailurePolicy.Abort), 2, true),
            new(new ItemTypeRemovalImpact("bug"), 3, true),
            new(new ParentRemovalImpact("task", "epic"), 1, true),
            new(new RoleRemovalImpact("reviewer"), 0, false),
            new(new FieldDetachImpact("task", "summary"), 5, false),
        ];
        using var factory = DetailFactory(detail => detail with { Impacts = impacts });
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        var data = await GetDetailAsync(client, 1);

        // Assert
        var expected = new JsonArray(
            Impact("state_move", 12, false, ("workflow", "main"), ("from", "review"), ("to", "internal_review")),
            Impact("field_drop", 4, false, ("field", "legacy")),
            Impact("field_conversion", 7, false, ("field", "budget"), ("fromType", "text"), ("toType", "number"), ("onFailure", "leave_empty")),
            Impact("field_conversion", 9, false, ("field", "notes"), ("fromType", "text"), ("toType", "long_text"), ("onFailure", null)),
            Impact("field_conversion", 2, true, ("field", "size"), ("fromType", "text"), ("toType", "single_select"), ("onFailure", "abort")),
            Impact("item_type_removal", 3, true, ("itemType", "bug")),
            Impact("parent_removal", 1, true, ("itemType", "task"), ("parent", "epic")),
            Impact("role_removal", 0, false, ("role", "reviewer")),
            Impact("field_detach", 5, false, ("itemType", "task"), ("field", "summary")));
        Assert.That(JsonNode.DeepEquals(data?["impacts"], expected), Is.True, data?["impacts"]?.ToJsonString());
    }

    [Test]
    public async Task Get_EveryFieldType_IsWrittenAsInTheBlueprintDocument()
    {
        // Arrange：兩兩一組，十種型別各出現一次。
        var types = Enum.GetValues<FieldType>();
        var impacts = types.Chunk(2)
            .Select(pair => new PlannedImpact(new FieldConversionImpact("f", pair[0], pair[1], null), 0, false))
            .ToArray();
        using var factory = DetailFactory(detail => detail with { Impacts = impacts });
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        var data = await GetDetailAsync(client, 1);

        // Assert
        var written = data?["impacts"]?.AsArray()
            .SelectMany(i => new[] { i?["fromType"]?.GetValue<string>(), i?["toType"]?.GetValue<string>() });
        Assert.That(written, Is.EqualTo(new[]
        {
            "text", "long_text", "number", "currency", "date", "single_select", "multi_select", "person", "checkbox", "url",
        }));
    }

    [Test]
    public async Task Get_Blockers_HaveCodePathAndMessage()
    {
        // Arrange
        FailureDetail[] blockers =
        [
            new(WorkErrorCodes.ChangeSetItemsOfRemovedType, "/blueprint/itemTypes", "還有 3 個項目是「錯誤」這個類型。"),
            new(WorkErrorCodes.ChangeSetTooManyItems, "", "一次最多搬移 5,000 個項目。"),
        ];
        using var factory = DetailFactory(detail => detail with { Blockers = blockers, CanApply = false });
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        var data = await GetDetailAsync(client, 1);

        // Assert
        var expected = new JsonArray(
            new JsonObject
            {
                ["code"] = "changeset.items_of_removed_type",
                ["path"] = "/blueprint/itemTypes",
                ["message"] = "還有 3 個項目是「錯誤」這個類型。",
            },
            new JsonObject { ["code"] = "changeset.too_many_items", ["path"] = "", ["message"] = "一次最多搬移 5,000 個項目。" });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data?["blockers"], expected), Is.True, data?["blockers"]?.ToJsonString());
            Assert.That(data?["canApply"]?.GetValue<bool>(), Is.False);
        }
    }

    [Test]
    public async Task Get_ImpactOfAKindTheApiDoesNotKnow_Returns500()
    {
        // Arrange：合約以後加了新的影響卻沒有對應時，不能默默少列一項會動到資料的事。
        using var factory = DetailFactory(detail => detail with { Impacts = [new PlannedImpact(new FutureImpact(), 1, false)] });
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Route}/1");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.InternalServerError, ApiErrorCodes.InternalError);
    }

    private static ApiFactory DetailFactory(Func<ChangeSetDetail, ChangeSetDetail> change)
    {
        var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Changes.OnGet = call =>
            change(TestData.ChangeSetDetail(TestData.ChangeSet(call.Number, call.Actor.PersonId)));
        return factory;
    }

    private static async Task<JsonNode?> GetDetailAsync(HttpClient client, long number)
    {
        using var response = await client.GetAsync($"{Route}/{number}");
        return await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
    }

    private static BlueprintChange Change(ChangeKind kind, ResourceKind resource, string key) =>
        new() { Kind = kind, Resource = resource, Key = key };

    private static JsonObject ExpectedChange(string kind, string resource, string key, string? parentKey, params string[] changed) =>
        new()
        {
            ["kind"] = kind,
            ["resource"] = resource,
            ["key"] = key,
            ["parentKey"] = parentKey,
            ["changedProperties"] = new JsonArray(changed.Select(name => (JsonNode?)name).ToArray()),
        };

    private static JsonObject Impact(string kind, long affected, bool blocksApply, params (string Name, string? Value)[] own)
    {
        var impact = new JsonObject { ["kind"] = kind, ["affected"] = affected, ["blocksApply"] = blocksApply };
        foreach (var (name, value) in own)
        {
            impact[name] = value;
        }

        return impact;
    }

    private static JsonObject ExpectedChangeSet(
        long number, string status, AuthenticatedUser? proposer, string proposedAt, Guid? proposerId = null, string origin = "direct") =>
        new()
        {
            ["number"] = number,
            ["status"] = status,
            ["baseVersion"] = 1,
            ["appliedVersion"] = null,
            ["summary"] = $"第 {number} 份變更",
            ["proposedBy"] = proposer is null
                ? Person(proposerId!.Value, "（已不在組織）")
                : Person(proposer.PersonId, proposer.DisplayName),
            ["proposedAt"] = proposedAt,
            ["decidedBy"] = null,
            ["decidedAt"] = null,
            ["origin"] = origin,
        };

    private static JsonObject Person(Guid personId, string displayName) =>
        new() { ["personId"] = personId, ["displayName"] = displayName };

    private sealed record FutureImpact : DataImpact;
}
