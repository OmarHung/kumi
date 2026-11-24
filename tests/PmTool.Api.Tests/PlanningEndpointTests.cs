using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Work;
using static PmTool.Api.Tests.ProjectEndpointTests;

namespace PmTool.Api.Tests;

/// <summary>週期與里程碑的 API 形狀：路徑、內文、回應與錯誤。規則由工作引擎判斷。</summary>
[TestFixture]
public sealed class PlanningEndpointTests
{
    private const string Cycles = "/v1/projects/WEB/cycles";
    private const string Milestones = "/v1/projects/WEB/milestones";
    private static readonly Guid CycleId = FakePlanningService.CycleId;
    private static readonly Guid MilestoneId = FakePlanningService.MilestoneId;

    private const string ExpectedCycle = """
        {"id":"018f0000-0000-7000-8000-0000000000c1","name":"衝刺 1","startDate":"2026-11-02","endDate":"2026-11-15",
         "progress":{"total":10,"done":3,"canceled":1,"estimateTotal":21.5,"estimateDone":8}}
        """;

    private const string ExpectedMilestone = """
        {"id":"018f0000-0000-7000-8000-0000000000a1","name":"v1.0 上線","dueDate":"2026-12-01","description":"第一個對外的版本。",
         "progress":{"total":0,"done":0,"canceled":0,"estimateTotal":null,"estimateDone":null}}
        """;

    [Test]
    public async Task ListCycles_ReturnsTheContractShapeAndPassesTheActor()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync(Cycles);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse($"[{ExpectedCycle}]")), Is.True, data?.ToJsonString());
            Assert.That(factory.Modules.Planning.Calls.Single(), Is.EqualTo(("ListCyclesAsync", TestUsers.Owner.ToActor(), "WEB", (Guid?)null, (object?)null)));
        }
    }

    [Test]
    public async Task CreateCycle_Returns201WithLocationAndPassesTheDraft()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(
            Cycles, ApiRequests.Document("""{"name":"衝刺 1","startDate":"2026-11-02","endDate":"2026-11-15"}"""));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Headers.Location?.ToString(), Is.EqualTo($"/v1/projects/WEB/cycles/{CycleId}"));
            Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse(ExpectedCycle)), Is.True, data?.ToJsonString());
            Assert.That(
                factory.Modules.Planning.Calls.Single().Draft,
                Is.EqualTo(new CycleDraft("衝刺 1", new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 15))));
        }
    }

    [Test]
    public async Task UpdateCycle_Returns200AndPassesTheId()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory);

        // Act
        using var response = await client.PutAsync(
            $"{Cycles}/{CycleId}", ApiRequests.Document("""{"name":"衝刺 1","startDate":"2026-11-02","endDate":"2026-11-15"}"""));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse(ExpectedCycle)), Is.True, data?.ToJsonString());
        Assert.That(factory.Modules.Planning.Calls.Single().Id, Is.EqualTo(CycleId));
    }

    [Test]
    public async Task DeleteCycle_Returns204WithoutBody()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory);

        // Act
        using var response = await client.DeleteAsync($"{Cycles}/{CycleId}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.Empty);
        Assert.That(factory.Modules.Planning.Calls.Single().Name, Is.EqualTo("DeleteCycleAsync"));
    }

    [TestCase("""{"startDate":"2026-11-02","endDate":"2026-11-15"}""", "")]
    [TestCase("""{"name":5,"startDate":"2026-11-02","endDate":"2026-11-15"}""", "name")]
    [TestCase("""{"name":"x","startDate":"11/02/2026","endDate":"2026-11-15"}""", "startDate")]
    [TestCase("""{"name":"x","startDate":"2026-11-02","endDate":null}""", "endDate")]
    [TestCase("""{"name":"x","startDate":"2026-11-02","endDate":"2026-11-15","extra":1}""", "extra")]
    public async Task CreateCycle_BodyOfTheWrongShape_Returns400AndDoesNotCallTheModule(string body, string path)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(Cycles, ApiRequests.Document(body));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);

        // Assert
        Assert.That(details[0]?["path"]?.GetValue<string>(), Is.EqualTo(path));
        Assert.That(factory.Modules.Planning.Calls, Is.Empty);
    }

    [TestCase(FailureKind.NotFound, WorkErrorCodes.CycleNotFound, HttpStatusCode.NotFound)]
    [TestCase(FailureKind.Conflict, WorkErrorCodes.CycleNameTaken, HttpStatusCode.Conflict)]
    [TestCase(FailureKind.Conflict, WorkErrorCodes.CycleOverlaps, HttpStatusCode.Conflict)]
    [TestCase(FailureKind.Conflict, WorkErrorCodes.CycleLimitReached, HttpStatusCode.Conflict)]
    [TestCase(FailureKind.Invalid, WorkErrorCodes.PlanningInvalid, HttpStatusCode.UnprocessableEntity)]
    [TestCase(FailureKind.Forbidden, WorkErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    public async Task CycleFailures_AreMappedToTheirStatusCodesAndCodes(FailureKind kind, string code, HttpStatusCode status)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Planning.OnCreateCycle = _ => new Failure(kind, code, "失敗")
        {
            Details = [new FailureDetail(WorkErrorCodes.PlanningInvalid, "endDate", "結束日不能早於開始日。")],
        };
        using var client = await SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(
            Cycles, ApiRequests.Document("""{"name":"衝刺 1","startDate":"2026-11-02","endDate":"2026-11-15"}"""));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, status, code);

        // Assert
        Assert.That(details.Single()?["path"]?.GetValue<string>(), Is.EqualTo("endDate"));
    }

    [Test]
    public async Task ListMilestones_ReturnsTheContractShape()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Milestones);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse($"[{ExpectedMilestone}]")), Is.True, data?.ToJsonString());
    }

    [Test]
    public async Task CreateMilestone_Returns201AndDescriptionIsOptional()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync(
            Milestones, ApiRequests.Document("""{"name":"v1.0 上線","dueDate":"2026-12-01"}"""));
        await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        Assert.That(response.Headers.Location?.ToString(), Is.EqualTo($"/v1/projects/WEB/milestones/{MilestoneId}"));
        Assert.That(
            factory.Modules.Planning.Calls.Single().Draft,
            Is.EqualTo(new MilestoneDraft("v1.0 上線", new DateOnly(2026, 12, 1), null)));
    }

    [Test]
    public async Task UpdateAndDeleteMilestone_PassTheId()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory);

        // Act
        using var put = await client.PutAsync(
            $"{Milestones}/{MilestoneId}", ApiRequests.Document("""{"name":"v1.0 上線","dueDate":"2026-12-01","description":"說明"}"""));
        using var delete = await client.DeleteAsync($"{Milestones}/{MilestoneId}");

        // Assert
        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(factory.Modules.Planning.Calls.Select(c => (c.Name, c.Id)), Is.EqualTo(new[]
        {
            ("UpdateMilestoneAsync", (Guid?)MilestoneId),
            ("DeleteMilestoneAsync", (Guid?)MilestoneId),
        }));
    }

    [TestCase("PUT")]
    [TestCase("DELETE")]
    public async Task IdIsNotAGuid_Returns404AndDoesNotCallTheModule(string method)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory);
        using var request = new HttpRequestMessage(new HttpMethod(method), $"{Cycles}/not-a-guid")
        {
            Content = method == "PUT" ? ApiRequests.Document("""{"name":"x","startDate":"2026-11-02","endDate":"2026-11-15"}""") : null,
        };

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(factory.Modules.Planning.Calls, Is.Empty);
    }

    [Test]
    public async Task NotSignedIn_Returns401()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();

        // Act
        using var response = await client.GetAsync(Cycles);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}
