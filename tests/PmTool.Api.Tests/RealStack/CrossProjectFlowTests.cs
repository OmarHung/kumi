using System.Net;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>整個堆疊（真的資料庫）上的跨專案清單、類型與「我的工作」的檢視。</summary>
[TestFixture]
public sealed class CrossProjectFlowTests : RealStackTestBase
{
    private const string ViewsRoute = "/v1/my-work/saved-views";

    [Test]
    public async Task ItemsAndTypes_MemberSeesOnlyTheirProjects_AndProjectParameterNarrowsTheList()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "XPA", "simple_tasks");
        await RealStackSteps.CreateProjectAsync(owner.Client, "XPB", "simple_tasks");
        await CreateTaskAsync(owner, "XPA", "甲專案的任務");
        await CreateTaskAsync(owner, "XPB", "乙專案的任務");
        using var member = await AddMemberAsync(owner, "XPA", "editor", "Cross Member", RealStackSteps.UniqueEmail("cross"));

        // Act
        var ownerAll = await RealStackSteps.GetAsync(owner.Client, "/v1/items");
        var ownerNarrowed = await RealStackSteps.GetAsync(owner.Client, "/v1/items?project=xpb");
        var ownerTwo = await RealStackSteps.GetAsync(owner.Client, "/v1/items?project=XPA&project=XPB&q=%E4%B9%99");
        var memberAll = await RealStackSteps.GetAsync(member.Client, "/v1/items");
        var memberHidden = await RealStackSteps.GetAsync(member.Client, "/v1/items?project=XPB");
        var ownerTypes = await RealStackSteps.GetAsync(owner.Client, "/v1/item-types");
        var memberTypes = await RealStackSteps.GetAsync(member.Client, "/v1/item-types");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Keys(ownerAll), Is.SupersetOf(new[] { "XPA-1", "XPB-1" }));
            Assert.That(Keys(ownerNarrowed), Is.EqualTo(new[] { "XPB-1" }));
            Assert.That(Keys(ownerTwo), Is.EqualTo(new[] { "XPB-1" }));
            Assert.That(Keys(memberAll), Is.EqualTo(new[] { "XPA-1" }));
            Assert.That(memberHidden["items"]!.AsArray(), Is.Empty);
            Assert.That(Types(ownerTypes, "task"), Is.SupersetOf(new[] { "XPA", "XPB" }));
            Assert.That(Types(memberTypes, "task"), Is.EqualTo(new[] { "XPA" }));
        }
    }

    [Test]
    public async Task Items_PagingAcrossProjects_FollowsTheCursorToTheEnd()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "XPC", "simple_tasks");
        await RealStackSteps.CreateProjectAsync(owner.Client, "XPD", "simple_tasks");
        foreach (var title in new[] { "一", "二", "三" })
        {
            await CreateTaskAsync(owner, "XPC", title);
            await CreateTaskAsync(owner, "XPD", title);
        }

        // Act
        var seen = new List<string>();
        string? cursor = null;
        do
        {
            var page = await RealStackSteps.GetAsync(owner.Client, $"/v1/items?project=XPC&project=XPD&limit=4{(cursor is null ? string.Empty : $"&cursor={cursor}")}");
            seen.AddRange(Keys(page));
            cursor = page["nextCursor"]?.GetValue<string>();
        }
        while (cursor is not null);

        // Assert
        Assert.That(seen, Is.EqualTo(new[] { "XPC-1", "XPC-2", "XPC-3", "XPD-1", "XPD-2", "XPD-3" }));
    }

    [Test]
    public async Task Items_MoreThanTwentyProjects_Returns422AtProject()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        var query = string.Join('&', Enumerable.Range(1, CrossProjectScope.MaxProjects + 1).Select(n => $"project=P{n}"));

        // Act
        using var response = await owner.Client.GetAsync($"/v1/items?{query}");
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.QueryInvalid);

        // Assert
        Assert.That(details[0]?["path"]?.GetValue<string>(), Is.EqualTo("project"));
    }

    [Test]
    public async Task MyWorkViews_AreSavedPerPersonValidatedAndIndependentOfProjectViews()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "XPV", "simple_tasks");
        using var member = await AddMemberAsync(owner, "XPV", "editor", "View Member", RealStackSteps.UniqueEmail("mywork"));

        // Act
        using var created = await owner.Client.PostAsync(ViewsRoute, ApiRequests.Json(Body("完成", "board", new { category = "done", project = "xpv", group = "project" })));
        var view = await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created);
        var viewId = view!["id"]!.GetValue<Guid>();
        using var duplicate = await owner.Client.PostAsync(ViewsRoute, ApiRequests.Json(Body("完成".ToUpperInvariant() + " ", "table", new { })));
        using var invalid = await owner.Client.PostAsync(ViewsRoute, ApiRequests.Json(Body("x", "table", new { category = "open", cycle = "current", assignee = "all" })));
        using var projectView = await owner.Client.PostAsync(
            "/v1/projects/XPV/saved-views", ApiRequests.Json(Body("專案的檢視", "table", new { category = "done" })));
        using var sameNameForMember = await member.Client.PostAsync(ViewsRoute, ApiRequests.Json(Body("完成", "table", new { })));
        var ownersList = await RealStackSteps.GetAsync(owner.Client, ViewsRoute);
        var membersList = await RealStackSteps.GetAsync(member.Client, ViewsRoute);
        using var memberDeletes = await member.Client.DeleteAsync($"{ViewsRoute}/{viewId}");
        using var replaced = await owner.Client.PutAsync($"{ViewsRoute}/{viewId}", ApiRequests.Json(Body("改過的", "table", new { due = "week" })));
        var replacedView = await ApiRequests.AssertSuccessEnvelopeAsync(replaced, HttpStatusCode.OK);
        using var deleted = await owner.Client.DeleteAsync($"{ViewsRoute}/{viewId}");
        var afterDelete = await RealStackSteps.GetAsync(owner.Client, ViewsRoute);
        var projectViews = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/XPV/saved-views");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(duplicate, HttpStatusCode.Conflict, WorkErrorCodes.SavedViewNameTaken);
        var details = await ApiRequests.AssertFailureEnvelopeAsync(invalid, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.SavedViewInvalid);
        var projectDetails = await ApiRequests.AssertFailureEnvelopeAsync(projectView, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.SavedViewInvalid);
        await ApiRequests.AssertSuccessEnvelopeAsync(sameNameForMember, HttpStatusCode.Created);
        await ApiRequests.AssertErrorEnvelopeAsync(memberDeletes, HttpStatusCode.NotFound, WorkErrorCodes.SavedViewNotFound);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(view["query"]!["project"]!.GetValue<string>(), Is.EqualTo("XPV"));
            Assert.That(view["query"]!["category"]!.GetValue<string>(), Is.EqualTo("done"));
            Assert.That(details.AsArray().Select(d => d!["path"]!.GetValue<string>()), Is.EquivalentTo(new[] { "query.category", "query.cycle", "query.assignee" }));
            Assert.That(projectDetails[0]?["path"]?.GetValue<string>(), Is.EqualTo("query.category"));
            Assert.That(ownersList.AsArray().Select(v => v!["id"]!.GetValue<Guid>()), Is.EqualTo(new[] { viewId }));
            Assert.That(membersList.AsArray(), Has.Count.EqualTo(1), "成員只有自己的，同名也可以");
            Assert.That(replacedView!["name"]!.GetValue<string>(), Is.EqualTo("改過的"));
            Assert.That(replacedView["query"]!["category"] is null, Is.True, "整個換掉：沒給的條件被清掉");
            Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(afterDelete.AsArray(), Is.Empty);
            Assert.That(projectViews.AsArray(), Is.Empty, "「我的工作」的檢視不會出現在專案裡");
        }
    }

    private static async Task CreateTaskAsync(SignedInClient client, string projectKey, string title)
    {
        using var response = await client.Client.PostAsync(
            $"/v1/projects/{projectKey}/items", ApiRequests.Json(new { type = "task", title }));
        await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);
    }

    private static string[] Keys(JsonNode? page) =>
        [.. page!["items"]!.AsArray().Select(i => i!["key"]!.GetValue<string>())];

    private static string[] Types(JsonNode? types, string key) =>
        [.. types!.AsArray().Single(t => t!["key"]!.GetValue<string>() == key)!["projectKeys"]!.AsArray().Select(p => p!.GetValue<string>())];

    private static object Body(string name, string layout, object query) => new { name, layout, query };
}
