using System.Net;
using PmTool.Api.Tests.Support;
using PmTool.Blueprint.Model;
using PmTool.Work;
using static PmTool.Api.Tests.ProjectEndpointTests;

namespace PmTool.Api.Tests;

/// <summary>專案總覽 <c>GET /v1/overview/projects</c> 的 API 形狀：範圍參數、JSON 形狀與錯誤。</summary>
[TestFixture]
public sealed class OverviewEndpointTests
{
    private const string Route = "/v1/overview/projects";

    private static readonly Guid CycleId = Guid.Parse("018f0000-0000-7000-8000-0000000000c2");
    private static readonly Guid MilestoneId = Guid.Parse("018f0000-0000-7000-8000-0000000000a2");

    [Test]
    public async Task List_SignedIn_ReturnsEveryFieldOfTheOverviewInTheDocumentedShape()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Overview.OnList = () =>
        [
            new ProjectOverview(
                "WEB",
                "範例專案",
                OwnItemsOnly: true,
                [new OverviewStateCount("todo", "待辦", StateCategory.NotStarted, 3), new OverviewStateCount("done", "完成", StateCategory.Done, 1)],
                Open: 3,
                Overdue: 1,
                DueSoon: 2,
                Unassigned: 1,
                Stale: 1,
                DoneRecently: 1,
                new OverviewCycle(CycleId, "衝刺 1", new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 15), new ItemProgress(4, 1, 0, 10m, 2m)),
                new OverviewMilestone(MilestoneId, "上線", new DateOnly(2026, 12, 1), new ItemProgress(0, 0, 0, null, null)),
                new DateTimeOffset(2026, 11, 10, 4, 0, 0, TimeSpan.Zero)),
        ];
        using var client = await SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var row = data!.AsArray().Single()!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row["projectKey"]?.GetValue<string>(), Is.EqualTo("WEB"));
            Assert.That(row["name"]?.GetValue<string>(), Is.EqualTo("範例專案"));
            Assert.That(row["ownItemsOnly"]?.GetValue<bool>(), Is.True);
            Assert.That(row["states"]?[0]?["key"]?.GetValue<string>(), Is.EqualTo("todo"));
            Assert.That(row["states"]?[0]?["name"]?.GetValue<string>(), Is.EqualTo("待辦"));
            Assert.That(row["states"]?[0]?["category"]?.GetValue<string>(), Is.EqualTo("not_started"));
            Assert.That(row["states"]?[0]?["count"]?.GetValue<int>(), Is.EqualTo(3));
            Assert.That(
                new[] { "open", "overdue", "dueSoon", "unassigned", "stale", "doneRecently" }.Select(name => row[name]?.GetValue<int>()),
                Is.EqualTo(new int?[] { 3, 1, 2, 1, 1, 1 }));
            Assert.That(row["currentCycle"]?["id"]?.GetValue<Guid>(), Is.EqualTo(CycleId));
            Assert.That(row["currentCycle"]?["name"]?.GetValue<string>(), Is.EqualTo("衝刺 1"));
            Assert.That(row["currentCycle"]?["startDate"]?.GetValue<string>(), Is.EqualTo("2026-11-02"));
            Assert.That(row["currentCycle"]?["endDate"]?.GetValue<string>(), Is.EqualTo("2026-11-15"));
            Assert.That(row["currentCycle"]?["progress"]?["total"]?.GetValue<int>(), Is.EqualTo(4));
            Assert.That(row["currentCycle"]?["progress"]?["estimateDone"]?.GetValue<decimal>(), Is.EqualTo(2m));
            Assert.That(row["nextMilestone"]?["id"]?.GetValue<Guid>(), Is.EqualTo(MilestoneId));
            Assert.That(row["nextMilestone"]?["dueDate"]?.GetValue<string>(), Is.EqualTo("2026-12-01"));
            Assert.That(row["nextMilestone"]?["progress"]?["estimateTotal"], Is.Null);
            Assert.That(row["lastUpdatedAt"]?.GetValue<DateTimeOffset>(), Is.EqualTo(new DateTimeOffset(2026, 11, 10, 4, 0, 0, TimeSpan.Zero)));
        }
    }

    [Test]
    public async Task List_NoCycleMilestoneOrItems_ReturnsNullsNotMissingProperties()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Overview.OnList = () => [new ProjectOverview("WEB", "範例專案", false, [], 0, 0, 0, 0, 0, 0, null, null, null)];
        using var client = await SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var row = data!.AsArray().Single()!.AsObject();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row["states"]!.AsArray(), Is.Empty);
            Assert.That(row.ContainsKey("currentCycle") && row["currentCycle"] is null, Is.True);
            Assert.That(row.ContainsKey("nextMilestone") && row["nextMilestone"] is null, Is.True);
            Assert.That(row.ContainsKey("lastUpdatedAt") && row["lastUpdatedAt"] is null, Is.True);
        }
    }

    [TestCase(null, ProjectOverviewScope.Managed)]
    [TestCase("managed", ProjectOverviewScope.Managed)]
    [TestCase("visible", ProjectOverviewScope.Visible)]
    public async Task List_ScopeParameter_IsPassedToTheModuleWithManagedAsTheDefault(string? scope, ProjectOverviewScope expected)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync(scope is null ? Route : $"{Route}?scope={scope}");
        await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var call = factory.Modules.Overview.Calls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(call.Scope, Is.EqualTo(expected));
            Assert.That(call.Actor, Is.EqualTo(TestUsers.Owner.ToActor()));
        }
    }

    [Test]
    public async Task List_UnknownScope_Returns422AtScopeAndDoesNotCallTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Route}?scope=everything");
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.QueryInvalid);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(details[0]?["path"]?.GetValue<string>(), Is.EqualTo("scope"));
            Assert.That(factory.Modules.Overview.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task List_NotSignedIn_Returns401()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(Route);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}
