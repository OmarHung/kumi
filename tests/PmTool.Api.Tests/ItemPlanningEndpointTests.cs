using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>項目的 cycleId、milestoneId 與清單的 cycle、milestone 篩選在 API 上的形狀。</summary>
[TestFixture]
public sealed class ItemPlanningEndpointTests
{
    private static readonly Guid CycleId = Guid.Parse("018f0000-0000-7000-8000-0000000000c1");
    private static readonly Guid MilestoneId = Guid.Parse("018f0000-0000-7000-8000-0000000000a1");

    [Test]
    public async Task GetItem_WritesCycleIdAndMilestoneId_NullWhenNotSet()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Items.OnGet = (_, key) => TestData.Item(key) with { CycleId = CycleId };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/items/WEB-7");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?["cycleId"]?.GetValue<Guid>(), Is.EqualTo(CycleId));
        Assert.That(data?.AsObject().ContainsKey("milestoneId"), Is.True);
        Assert.That(data?["milestoneId"], Is.Null);
    }

    [Test]
    public async Task CreateItem_PassesCycleIdAndMilestoneId()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        var body = new { type = "task", title = "寫首頁", cycleId = CycleId, milestoneId = MilestoneId };

        // Act
        using var response = await client.PostAsync("/v1/projects/WEB/items", ApiRequests.Json(body));
        await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        var item = factory.Modules.Items.CreateCalls.Single().Item;
        Assert.That((item.CycleId, item.MilestoneId), Is.EqualTo(((Guid?)CycleId, (Guid?)MilestoneId)));
    }

    [Test]
    public async Task PatchItem_NullClearsAndMissingLeavesAlone()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var clear = await client.PatchAsync("/v1/items/WEB-7", ApiRequests.Json(new { cycleId = (Guid?)null }));
        using var set = await client.PatchAsync("/v1/items/WEB-7", ApiRequests.Json(new { milestoneId = MilestoneId }));

        // Assert
        var calls = factory.Modules.Items.UpdateCalls;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(calls[0].Patch.CycleId, Is.EqualTo(new Optional<Guid?>(null)));
            Assert.That(calls[0].Patch.MilestoneId.HasValue, Is.False);
            Assert.That(calls[1].Patch.CycleId.HasValue, Is.False);
            Assert.That(calls[1].Patch.MilestoneId, Is.EqualTo(new Optional<Guid?>(MilestoneId)));
        }
    }

    [Test]
    public async Task PatchItem_CycleIdNotAGuid_Returns400WithThePath()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PatchAsync("/v1/items/WEB-7", ApiRequests.Json(new { cycleId = "abc" }));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);

        // Assert
        Assert.That(details[0]?["path"]?.GetValue<string>(), Is.EqualTo("cycleId"));
        Assert.That(factory.Modules.Items.UpdateCalls, Is.Empty);
    }

    [Test]
    public async Task List_CycleAndMilestoneParameters_BecomeTheQuery()
    {
        // Act
        var byId = await QueryForAsync($"?cycle={CycleId}&milestone={MilestoneId}");
        var none = await QueryForAsync("?cycle=none&milestone=none");
        var empty = await QueryForAsync("?cycle=&milestone=");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((byId.CycleId, byId.OnlyWithoutCycle, byId.MilestoneId, byId.OnlyWithoutMilestone), Is.EqualTo(((Guid?)CycleId, false, (Guid?)MilestoneId, false)));
            Assert.That((none.CycleId, none.OnlyWithoutCycle, none.MilestoneId, none.OnlyWithoutMilestone), Is.EqualTo(((Guid?)null, true, (Guid?)null, true)));
            Assert.That((empty.CycleId, empty.OnlyWithoutCycle, empty.MilestoneId, empty.OnlyWithoutMilestone), Is.EqualTo(((Guid?)null, false, (Guid?)null, false)));
        }
    }

    [TestCase("cycle=current", "cycle")]
    [TestCase("cycle=abc", "cycle")]
    [TestCase("cycle=none&cycle=none", "cycle")]
    [TestCase("milestone=current", "milestone")]
    [TestCase("milestone=xyz", "milestone")]
    public async Task List_BadCycleOrMilestone_Returns422QueryInvalid(string parameters, string path)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"/v1/projects/WEB/items?{parameters}");
        var details = await ApiRequests.AssertFailureEnvelopeAsync(
            response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.QueryInvalid);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{WorkErrorCodes.QueryInvalid}|{path}" }));
        Assert.That(factory.Modules.Items.ListCalls, Is.Empty);
    }

    [Test]
    public async Task SavedView_CycleAndMilestoneAreWrittenAndRead()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        const string body = """{"name":"衝刺","layout":"board","query":{"cycle":"current","milestone":"none"}}""";

        // Act
        using var response = await client.PostAsync("/v1/projects/WEB/saved-views", ApiRequests.Document(body));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        var query = factory.Modules.SavedViews.CreateCalls.Single().Draft.Query;
        using (Assert.EnterMultipleScope())
        {
            Assert.That((query.Cycle, query.Milestone), Is.EqualTo(("current", "none")));
            Assert.That(data?["query"]?["cycle"]?.GetValue<string>(), Is.EqualTo("current"));
            Assert.That(data?["query"]?["milestone"]?.GetValue<string>(), Is.EqualTo("none"));
        }
    }

    private static async Task<WorkItemQuery> QueryForAsync(string queryString)
    {
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        using var response = await client.GetAsync("/v1/projects/WEB/items" + queryString);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return factory.Modules.Items.ListCalls.Single().Query;
    }
}
