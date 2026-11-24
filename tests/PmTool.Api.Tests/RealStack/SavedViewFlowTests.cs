using System.Net;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>整個堆疊（真的資料庫）上的檢視：存、列、換、刪，只有自己看得到。</summary>
[TestFixture]
public sealed class SavedViewFlowTests : RealStackTestBase
{
    [Test]
    public async Task OwnerAndMember_EachKeepTheirOwnViews()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "VIEW", "simple_tasks");
        using var member = await AddMemberAsync(owner, "VIEW", "viewer", "View Member", RealStackSteps.UniqueEmail("view"));
        const string Route = "/v1/projects/VIEW/saved-views";

        // Act
        using var created = await owner.Client.PostAsync(Route, ApiRequests.Json(Body("我本週要交的", "board", due: "week", priority: "high")));
        var view = await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created);
        var viewId = view!["id"]!.GetValue<Guid>();
        using var duplicate = await owner.Client.PostAsync(Route, ApiRequests.Json(Body("我本週要交的".ToUpperInvariant(), "table")));
        using var invalid = await owner.Client.PostAsync(Route, ApiRequests.Json(Body("x", "table", due: "someday")));
        using var memberCreated = await member.Client.PostAsync(Route, ApiRequests.Json(Body("我本週要交的", "table")));
        var ownersList = await RealStackSteps.GetAsync(owner.Client, Route);
        var membersList = await RealStackSteps.GetAsync(member.Client, Route);
        using var memberDeletes = await member.Client.DeleteAsync($"{Route}/{viewId}");
        using var replaced = await owner.Client.PutAsync($"{Route}/{viewId}", ApiRequests.Json(Body("改過的", "table", priority: "low")));
        var replacedView = await ApiRequests.AssertSuccessEnvelopeAsync(replaced, HttpStatusCode.OK);
        using var deleted = await owner.Client.DeleteAsync($"{Route}/{viewId}");
        var afterDelete = await RealStackSteps.GetAsync(owner.Client, Route);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(duplicate, HttpStatusCode.Conflict, WorkErrorCodes.SavedViewNameTaken);
        var details = await ApiRequests.AssertFailureEnvelopeAsync(invalid, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.SavedViewInvalid);
        await ApiRequests.AssertSuccessEnvelopeAsync(memberCreated, HttpStatusCode.Created);
        await ApiRequests.AssertErrorEnvelopeAsync(memberDeletes, HttpStatusCode.NotFound, WorkErrorCodes.SavedViewNotFound);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(details[0]?["path"]?.GetValue<string>(), Is.EqualTo("query.due"));
            Assert.That(view["query"]!["priority"]!.GetValue<string>(), Is.EqualTo("high"));
            Assert.That(ownersList.AsArray().Select(v => v!["id"]!.GetValue<Guid>()), Is.EqualTo(new[] { viewId }));
            Assert.That(membersList.AsArray(), Has.Count.EqualTo(1), "成員只有自己的，同名也可以");
            Assert.That(membersList[0]!["id"]!.GetValue<Guid>(), Is.Not.EqualTo(viewId));
            Assert.That(replacedView!["name"]!.GetValue<string>(), Is.EqualTo("改過的"));
            Assert.That(replacedView["query"]!["due"] is null, Is.True, "整個換掉：沒給的條件被清掉");
            Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(afterDelete.AsArray(), Is.Empty);
        }
    }

    [Test]
    public async Task ProjectTheMemberIsNotIn_IsNotFound()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "HID", "simple_tasks");
        await RealStackSteps.CreateProjectAsync(owner.Client, "OPEN", "simple_tasks");
        using var member = await AddMemberAsync(owner, "OPEN", "editor", "Open Member", RealStackSteps.UniqueEmail("open"));

        // Act
        using var list = await member.Client.GetAsync("/v1/projects/HID/saved-views");
        using var create = await member.Client.PostAsync("/v1/projects/HID/saved-views", ApiRequests.Json(Body("x", "table")));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(list, HttpStatusCode.NotFound, WorkErrorCodes.ProjectNotFound);
        await ApiRequests.AssertErrorEnvelopeAsync(create, HttpStatusCode.NotFound, WorkErrorCodes.ProjectNotFound);
    }

    private static object Body(string name, string layout, string? due = null, string? priority = null) =>
        new { name, layout, query = new { due, priority } };
}
