using System.Net;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>專案搜尋的主線（真的資料庫）：說明比對、摘錄、範圍與驗證。</summary>
[TestFixture]
public sealed class ProjectSearchFlowTests : RealStackTestBase
{
    [Test]
    public async Task Search_ByDescription_FindsOnlyProjectsTheCallerCanSeeWithASnippet()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        var token = $"srv{Guid.NewGuid():N}.example.com";
        await RealStackSteps.CreateProjectAsync(owner.Client, "SRA", "simple_tasks");
        await RealStackSteps.CreateProjectAsync(owner.Client, "SRB", "simple_tasks");
        await RealStackSteps.CreateProjectAsync(owner.Client, "SRC", "simple_tasks");
        using var putA = await owner.Client.PutAsync(
            "/v1/projects/SRA/description", ApiRequests.Json(new { text = $"## 主機\n- 名稱：{token.ToUpperInvariant()}\n- 資料庫：db01" }));
        using var putB = await owner.Client.PutAsync("/v1/projects/SRB/description", ApiRequests.Json(new { text = $"在 {token}" }));
        using var viewer = await AddMemberAsync(owner, "SRA", "viewer", "Viewer Example", RealStackSteps.UniqueEmail("search-viewer"));

        // Act
        var ownerHits = await RealStackSteps.GetAsync(owner.Client, $"/v1/search/projects?q={token}");
        var viewerHits = await RealStackSteps.GetAsync(viewer.Client, $"/v1/search/projects?q={token}");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ownerHits.AsArray().Select(h => h!["project"]!["key"]!.GetValue<string>()), Is.EqualTo(new[] { "SRA", "SRB" }));
            Assert.That(ownerHits[0]!["snippet"]!.GetValue<string>(), Is.EqualTo($"名稱：{token.ToUpperInvariant()}"));
            Assert.That(ownerHits[1]!["snippet"]!.GetValue<string>(), Is.EqualTo($"在 {token}"));
            Assert.That(viewerHits.AsArray().Select(h => h!["project"]!["key"]!.GetValue<string>()), Is.EqualTo(new[] { "SRA" }));
            Assert.That(viewerHits[0]!["project"]!["myRole"]!.GetValue<string>(), Is.EqualTo("viewer"));
        }
    }

    [Test]
    public async Task Search_ByKeyOfAnArchivedProject_ReturnsItWithNullSnippet()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "SRZ", "simple_tasks");
        using var archive = await owner.Client.PostAsync("/v1/projects/SRZ/archive", null);

        // Act
        var hits = await RealStackSteps.GetAsync(owner.Client, "/v1/search/projects?q=srz&limit=3");

        // Assert
        var hit = hits.AsArray().Single()!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hit["project"]!["archivedAt"], Is.Not.Null);
            Assert.That(hit["snippet"], Is.Null);
        }
    }

    [TestCase("q=a%0Ab", "q")]
    [TestCase("q=%20%20", "q")]
    [TestCase("q=a&limit=0", "limit")]
    [TestCase("q=a&limit=21", "limit")]
    public async Task Search_InvalidInput_Returns422WithThePath(string parameters, string expectedPath)
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();

        // Act
        using var response = await owner.Client.GetAsync($"/v1/search/projects?{parameters}");
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.QueryInvalid);

        // Assert
        Assert.That(details[0]?["path"]?.GetValue<string>(), Is.EqualTo(expectedPath));
    }
}
