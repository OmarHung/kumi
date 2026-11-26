using System.Net;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>整個堆疊（真的資料庫）上的專案總覽：數字、範圍、只看自己的人，以及「停滯」連到清單用的 <c>updatedBefore</c>。</summary>
[TestFixture]
public sealed class OverviewFlowTests : RealStackTestBase
{
    private const string Route = "/v1/overview/projects";

    [Test]
    public async Task Overview_CountsTheProjectsItemsAndOwnerSeesEveryProjectWhileAnExecutorOnlySeesTheirOwn()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "XOA", "simple_tasks");
        await RealStackSteps.CreateProjectAsync(owner.Client, "XOB", "simple_tasks");
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2).ToString("yyyy-MM-dd");
        await RealStackSteps.CreateItemAsync(owner.Client, "XOA", new { type = "task", title = "過期的", dueDate = yesterday });
        await RealStackSteps.CreateItemAsync(owner.Client, "XOA", new { type = "task", title = "沒有日期的" });
        var finished = await RealStackSteps.CreateItemAsync(owner.Client, "XOA", new { type = "task", title = "做完的", dueDate = yesterday });
        using var moved = await RealStackSteps.TransitionAsync(owner.Client, finished["key"]!.GetValue<string>(), "done");
        await ApiRequests.AssertSuccessEnvelopeAsync(moved, HttpStatusCode.OK);
        using var executor = await AddMemberAsync(owner, "XOA", "contributor", "Overview Executor", RealStackSteps.UniqueEmail("overview"));
        await RealStackSteps.CreateItemAsync(executor.Client, "XOA", new { type = "task", title = "執行者自己開的" });

        // Act
        var ownerRows = await RealStackSteps.GetAsync(owner.Client, $"{Route}?scope=visible");
        var executorRows = await RealStackSteps.GetAsync(executor.Client, $"{Route}?scope=visible");
        var executorManaged = await RealStackSteps.GetAsync(executor.Client, Route);

        // Assert
        var xoa = Row(ownerRows, "XOA");
        var executorXoa = Row(executorRows, "XOA");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(xoa["ownItemsOnly"]!.GetValue<bool>(), Is.False);
            Assert.That(xoa["open"]!.GetValue<int>(), Is.EqualTo(3));
            Assert.That(xoa["overdue"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(xoa["unassigned"]!.GetValue<int>(), Is.EqualTo(3));
            Assert.That(xoa["doneRecently"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(xoa["states"]!.AsArray().Select(s => (s!["key"]!.GetValue<string>(), s["count"]!.GetValue<int>())), Is.EqualTo(new[] { ("todo", 3), ("done", 1) }));
            Assert.That(Row(ownerRows, "XOB")["states"]!.AsArray(), Is.Empty);
            Assert.That(executorXoa["ownItemsOnly"]!.GetValue<bool>(), Is.True);
            Assert.That(executorXoa["open"]!.GetValue<int>(), Is.EqualTo(1), "執行者只算自己開的那一個");
            Assert.That(executorXoa["overdue"]!.GetValue<int>(), Is.Zero);
            Assert.That(Keys(executorRows), Does.Not.Contain("XOB"), "不是成員的專案不出現");
            Assert.That(Keys(executorManaged), Does.Not.Contain("XOA"), "執行者沒有管理這個專案");
        }
    }

    [Test]
    public async Task Overview_UnknownScope_Returns422AtScope()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();

        // Act
        using var response = await owner.Client.GetAsync($"{Route}?scope=mine");
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.QueryInvalid);

        // Assert
        Assert.That(details[0]?["path"]?.GetValue<string>(), Is.EqualTo("scope"));
    }

    [Test]
    public async Task UpdatedBefore_FiltersProjectAndCrossProjectListsAndRejectsAZonelessTime()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "XOC", "simple_tasks");
        await RealStackSteps.CreateItemAsync(owner.Client, "XOC", new { type = "task", title = "剛建立的" });
        var future = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"));
        var past = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"));

        // Act
        var inProjectFuture = await RealStackSteps.GetAsync(owner.Client, $"/v1/projects/XOC/items?updatedBefore={future}");
        var inProjectPast = await RealStackSteps.GetAsync(owner.Client, $"/v1/projects/XOC/items?updatedBefore={past}");
        var acrossFuture = await RealStackSteps.GetAsync(owner.Client, $"/v1/items?project=XOC&updatedBefore={future}");
        var acrossPast = await RealStackSteps.GetAsync(owner.Client, $"/v1/items?project=XOC&updatedBefore={past}");
        using var zoneless = await owner.Client.GetAsync("/v1/projects/XOC/items?updatedBefore=2026-11-10T04:00:00");

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(zoneless, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.QueryInvalid);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(inProjectFuture["items"]!.AsArray(), Has.Count.EqualTo(1));
            Assert.That(inProjectPast["items"]!.AsArray(), Is.Empty);
            Assert.That(acrossFuture["items"]!.AsArray(), Has.Count.EqualTo(1));
            Assert.That(acrossPast["items"]!.AsArray(), Is.Empty);
        }
    }

    private static JsonNode Row(JsonNode rows, string key) =>
        rows.AsArray().Single(r => r!["projectKey"]!.GetValue<string>() == key)!;

    private static string[] Keys(JsonNode rows) => [.. rows.AsArray().Select(r => r!["projectKey"]!.GetValue<string>())];
}
