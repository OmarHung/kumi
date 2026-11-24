using System.Net;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>整個堆疊上依自訂欄位篩選與排序：網址參數、me、沒有填、排序，以及個人檢視存得下欄位條件。</summary>
[TestFixture]
public sealed class ItemFieldQueryFlowTests : RealStackTestBase
{
    [Test]
    public async Task List_FilterAndSortByCustomFields_OverHttp()
    {
        // Arrange：feature、bug、chore 是選項的順序。
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "FLD", "simple_tasks");
        var one = await AddAsync(owner, "一", ("category", "chore"), ("story_points", 10), ("reviewer", owner.PersonId.ToString()));
        var two = await AddAsync(owner, "二", ("category", "feature"), ("story_points", 9), ("blocked", true));
        var three = await AddAsync(owner, "三", ("category", "bug"), ("tags", new[] { "frontend" }));
        var four = await AddAsync(owner, "四");

        // Act
        var bugOrChore = await ListAsync(owner, "field.category=bug&field.category=chore");
        var noCategory = await ListAsync(owner, "field.category=_none");
        var mine = await ListAsync(owner, "field.reviewer=me");
        var blocked = await ListAsync(owner, "field.blocked=true");
        var both = await ListAsync(owner, "field.category=feature&field.blocked=true");
        var byOption = await ListAsync(owner, "sort=field.category:asc");
        var byPointsDescending = await ListAsync(owner, "sort=field.story_points:desc");
        using var unsortable = await owner.Client.GetAsync("/v1/projects/FLD/items?sort=field.tags:asc");
        using var unfilterable = await owner.Client.GetAsync("/v1/projects/FLD/items?field.summary=x1");
        using var comma = await owner.Client.GetAsync("/v1/projects/FLD/items?field.category=bug,chore");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(bugOrChore, Is.EquivalentTo(new[] { one, three }));
            Assert.That(noCategory, Is.EqualTo(new[] { four }));
            Assert.That(mine, Is.EqualTo(new[] { one }));
            Assert.That(blocked, Is.EqualTo(new[] { two }));
            Assert.That(both, Is.EqualTo(new[] { two }));
            Assert.That(byOption, Is.EqualTo(new[] { two, three, one, four }));
            Assert.That(byPointsDescending, Is.EqualTo(new[] { one, two, three, four }));
        }

        var unsortableDetails = await ApiRequests.AssertFailureEnvelopeAsync(
            unsortable, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.QueryInvalid);
        var unfilterableDetails = await ApiRequests.AssertFailureEnvelopeAsync(
            unfilterable, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.QueryInvalid);
        await ApiRequests.AssertFailureEnvelopeAsync(comma, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.QueryInvalid);
        Assert.That(ApiRequests.Describe(unsortableDetails), Is.EqualTo(new[] { $"{WorkErrorCodes.QueryInvalid}|sort" }));
        Assert.That(ApiRequests.Describe(unfilterableDetails), Is.EqualTo(new[] { $"{WorkErrorCodes.QueryInvalid}|field.summary" }));
    }

    [Test]
    public async Task SavedView_FieldsAndGroup_AreStoredAndReadBack()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "FLV", "simple_tasks");
        const string Route = "/v1/projects/FLV/saved-views";
        var body = new
        {
            name = "依分類",
            layout = "board",
            query = new { fields = new Dictionary<string, string> { ["category"] = "bug,_none" }, group = "field.category", sort = "field.story_points:asc" },
        };

        // Act
        using var created = await owner.Client.PostAsync(Route, ApiRequests.Json(body));
        await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created);
        var listed = await RealStackSteps.GetAsync(owner.Client, Route);
        using var invalid = await owner.Client.PostAsync(Route, ApiRequests.Json(new
        {
            name = "壞的",
            layout = "table",
            query = new { group = "priority", sort = "field:asc" },
        }));

        // Assert
        var query = listed[0]!["query"]!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query["fields"]!["category"]!.GetValue<string>(), Is.EqualTo("bug,_none"));
            Assert.That(query["group"]!.GetValue<string>(), Is.EqualTo("field.category"));
            Assert.That(query["sort"]!.GetValue<string>(), Is.EqualTo("field.story_points:asc"));
        }

        var details = await ApiRequests.AssertFailureEnvelopeAsync(
            invalid, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.SavedViewInvalid);
        Assert.That(details.Select(d => d?["path"]?.GetValue<string>()), Is.EquivalentTo(new[] { "query.sort", "query.group" }));
    }

    private static async Task<string> AddAsync(SignedInClient client, string title, params (string Key, object Value)[] fields)
    {
        var item = await RealStackSteps.CreateItemAsync(
            client.Client, "FLD", new { type = "task", title, fields = fields.ToDictionary(f => f.Key, f => f.Value) });
        return item["key"]!.GetValue<string>();
    }

    private static async Task<string[]> ListAsync(SignedInClient client, string query)
    {
        var data = await RealStackSteps.GetAsync(client.Client, $"/v1/projects/FLD/items?{query}");
        return RealStackSteps.KeysOf(data["items"]);
    }
}
