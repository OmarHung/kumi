using System.Net;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>GET /v1/search/items：參數、回應的形狀、驗證錯誤與權杖範圍。</summary>
[TestFixture]
public sealed class ItemSearchEndpointTests
{
    private const string Route = "/v1/search/items";

    [Test]
    public async Task Search_SignedIn_ReturnsItemsInTheListShapeWithNamesLookedUpOnce()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Items.OnSearch = (_, _, _) => new List<WorkItem>
        {
            TestData.Item("WEB-1", TestUsers.Member.PersonId),
            TestData.Item("APP-2", TestUsers.Member.PersonId),
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Route}?q=%E7%99%BB%E5%85%A5");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var items = data?["items"]?.AsArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items?.Select(i => i?["key"]?.GetValue<string>()), Is.EqualTo(new[] { "WEB-1", "APP-2" }));
            Assert.That(items?[0]?["assignee"]?["displayName"]?.GetValue<string>(), Is.EqualTo(TestUsers.Member.DisplayName));
            Assert.That(items?[0]?.AsObject().ContainsKey("description"), Is.False, "和清單一樣不帶說明");
            Assert.That(data?.AsObject().Select(p => p.Key), Is.EqualTo(new[] { "items" }));
            Assert.That(factory.Modules.People.FindManyCalls.Single().PersonIds, Is.EquivalentTo(new[] { TestUsers.Member.PersonId, TestData.Item().CreatedBy }), "負責人與建立者的名字一次查齊");
        }
    }

    [Test]
    public async Task Search_WithoutLimit_UsesTheDefaultOfEight()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Route}?q=abc");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(factory.Modules.Items.SearchCalls.Single(), Is.EqualTo((TestUsers.Member.ToActor(), "abc", 8)));
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
        Assert.That(factory.Modules.Items.SearchCalls.Single().Limit, Is.EqualTo(15));
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
        Assert.That(factory.Modules.Items.SearchCalls, Is.Empty);
    }

    [Test]
    public async Task Search_ModuleRejectsTheText_Returns422WithTheModuleCode()
    {
        // Arrange：長度與字元由模組檢查，規則只有一份。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Items.OnSearch = (_, _, _) =>
            new Failure(FailureKind.Invalid, WorkErrorCodes.QueryInvalid, "搜尋的文字太長。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Route}?q={new string('a', 300)}");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.QueryInvalid);
    }
}
