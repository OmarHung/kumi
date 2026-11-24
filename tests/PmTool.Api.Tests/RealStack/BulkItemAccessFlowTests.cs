using System.Net;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 批次操作的權限與找不到：沒有權限整個 403、一筆都不做；看不到的專案是 404。
/// </summary>
[TestFixture]
public sealed class BulkItemAccessFlowTests : RealStackTestBase
{
    [Test]
    public async Task Bulk_ByACommenter_Returns403ForTheWholeRequestAndChangesNothing()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "NOBULK", "simple_tasks");
        await RealStackSteps.CreateItemAsync(owner.Client, "NOBULK", new { type = "task", title = "不能批次" });
        using var commenter = await AddMemberAsync(owner, "NOBULK", "commenter", "Commenter Example", RealStackSteps.UniqueEmail("bulk"));

        // Act
        using var update = await commenter.Client.PostAsync(
            "/v1/projects/NOBULK/items/bulk-update", ApiRequests.Json(new { keys = new[] { "NOBULK-1" }, priority = "high" }));
        using var delete = await commenter.Client.PostAsync(
            "/v1/projects/NOBULK/items/bulk-delete", ApiRequests.Json(new { keys = new[] { "NOBULK-1" } }));
        var item = await RealStackSteps.GetAsync(owner.Client, "/v1/items/NOBULK-1");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(update, HttpStatusCode.Forbidden, WorkErrorCodes.Forbidden);
        await ApiRequests.AssertErrorEnvelopeAsync(delete, HttpStatusCode.Forbidden, WorkErrorCodes.Forbidden);
        Assert.That(item["priority"]?.GetValue<string>(), Is.EqualTo("none"));
    }

    [Test]
    public async Task Bulk_ProjectTheCallerCannotSee_Returns404ProjectNotFound()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();

        // Act
        using var response = await owner.Client.PostAsync("/v1/projects/NOPE/items/bulk-delete", ApiRequests.Json(new { keys = new[] { "NOPE-1" } }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, WorkErrorCodes.ProjectNotFound);
    }
}
