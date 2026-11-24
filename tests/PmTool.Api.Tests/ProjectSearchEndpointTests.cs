using System.Net;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>GET /v1/search/projects：參數、回應的形狀、驗證錯誤（規則在工作引擎，這裡只轉換格式與狀態碼）。</summary>
[TestFixture]
public sealed class ProjectSearchEndpointTests
{
    private const string Route = "/v1/search/projects";

    [Test]
    public async Task Search_Hits_ReturnsAnArrayOfProjectInTheListShapeAndSnippet()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Descriptions.OnSearch = (_, _, _) => new List<ProjectSearchHit>
        {
            new(TestData.Project("WEB"), "主機：web01.example.com"),
            new(TestData.Project("APP", archivedAt: TestData.UpdatedAt), null),
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Route}?q=web01");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var hits = data!.AsArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits.Select(h => h!.AsObject().Select(p => p.Key).ToArray()), Is.All.EqualTo(new[] { "project", "snippet" }));
            Assert.That(hits[0]!["project"]!["key"]!.GetValue<string>(), Is.EqualTo("WEB"));
            Assert.That(
                hits[0]!["project"]!.AsObject().Select(p => p.Key),
                Is.EqualTo(new[] { "id", "key", "name", "blueprintVersion", "myRole", "createdAt", "archivedAt" }));
            Assert.That(hits[0]!["snippet"]!.GetValue<string>(), Is.EqualTo("主機：web01.example.com"));
            Assert.That(hits[1]!["project"]!["archivedAt"]!.GetValue<DateTimeOffset>(), Is.EqualTo(TestData.UpdatedAt));
            Assert.That(hits[1]!["snippet"], Is.Null);
        }
    }

    [Test]
    public async Task Search_NoHits_ReturnsAnEmptyArray()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Route}?q=nothing");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data!.AsArray(), Is.Empty);
    }

    [Test]
    public async Task Search_WithoutLimit_UsesTheDefaultOfEightAndPassesTheText()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Route}?q=%E4%B8%BB%E6%A9%9F");

        // Assert
        Assert.That(factory.Modules.Descriptions.SearchCalls.Single(), Is.EqualTo((TestUsers.Member.ToActor(), "主機", 8)));
    }

    [Test]
    public async Task Search_Limit_IsPassedThrough()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Route}?q=abc&limit=15");

        // Assert
        Assert.That(factory.Modules.Descriptions.SearchCalls.Single().Limit, Is.EqualTo(15));
    }

    [TestCase("", "q", TestName = "Search_MissingQ_Returns422")]
    [TestCase("q=", "q", TestName = "Search_EmptyQ_Returns422")]
    [TestCase("q=a&q=b", "q", TestName = "Search_QGivenTwice_Returns422")]
    [TestCase("q=a&limit=many", "limit", TestName = "Search_LimitNotANumber_Returns422")]
    [TestCase("q=a&limit=-1", "limit", TestName = "Search_NegativeLimit_Returns422")]
    public async Task Search_InvalidParameters_Returns422WithoutReachingTheModule(string parameters, string expectedPath)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Route}?{parameters}");
        var details = await ApiRequests.AssertFailureEnvelopeAsync(
            response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.QueryInvalid);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{WorkErrorCodes.QueryInvalid}|{expectedPath}" }));
        Assert.That(factory.Modules.Descriptions.SearchCalls, Is.Empty);
    }

    [Test]
    public async Task Search_ModuleRejectsTheLimit_Returns422WithTheModulePath()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Descriptions.OnSearch = (_, _, _) =>
            new Failure(FailureKind.Invalid, WorkErrorCodes.QueryInvalid, "數量要在 1 到 20 之間。")
            {
                Details = [new FailureDetail(WorkErrorCodes.QueryInvalid, "limit", "數量要在 1 到 20 之間。")],
            };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Route}?q=a&limit=21");
        var details = await ApiRequests.AssertFailureEnvelopeAsync(
            response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.QueryInvalid);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{WorkErrorCodes.QueryInvalid}|limit" }));
    }
}
