using System.Net;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>試算表匯入的主線（真的資料庫）：乾跑、建立、全部或全部不、狀態、封存、通知不被洗掉。</summary>
[TestFixture]
public sealed class ItemImportFlowTests : RealStackTestBase
{
    [Test]
    public async Task Import_DryRunThenReal_CreatesTheRowsInOrderAndTheyShowInTheList()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "IMP", "simple_tasks");
        var body = new { rows = new[] { new { type = "task", title = "甲" }, new { type = "task", title = "乙" } } };

        // Act
        using var dry = await owner.Client.PostAsync("/v1/projects/IMP/items/import", ApiRequests.Json(new { body.rows, dryRun = true }));
        var dryData = await ApiRequests.AssertSuccessEnvelopeAsync(dry, HttpStatusCode.OK);
        var afterDry = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/IMP/items");
        using var real = await owner.Client.PostAsync("/v1/projects/IMP/items/import", ApiRequests.Json(body));
        var realData = await ApiRequests.AssertSuccessEnvelopeAsync(real, HttpStatusCode.OK);
        var afterReal = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/IMP/items");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((dryData?["dryRun"]?.GetValue<bool>(), dryData?["count"]?.GetValue<int>()), Is.EqualTo((true, 2)));
            Assert.That(afterDry["items"]?.AsArray(), Is.Empty);
            Assert.That(realData?["createdKeys"]?.AsArray().Select(k => k?.GetValue<string>()), Is.EqualTo(new[] { "IMP-1", "IMP-2" }));
            Assert.That(RealStackSteps.KeysOf(afterReal["items"]), Is.EquivalentTo(new[] { "IMP-1", "IMP-2" }));
        }
    }

    [Test]
    public async Task Import_ABadRow_Returns422WithRowPathsAndCreatesNothing()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "IMB", "simple_tasks");

        // Act
        using var response = await owner.Client.PostAsync(
            "/v1/projects/IMB/items/import",
            ApiRequests.Json(new { rows = new object[] { new { type = "task", title = "好" }, new { type = "task", title = "壞", dueDate = "2026-11-01", startDate = "2026-11-20" } } }));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.ImportInvalid);
        var listed = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/IMB/items");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(details.Select(d => d?["path"]?.GetValue<string>()), Is.EqualTo(new[] { "rows/1/due_date" }));
            Assert.That(listed["items"]?.AsArray(), Is.Empty);
        }
    }

    [Test]
    public async Task Import_WithStates_SetsThemDirectlyForAManagerAndRefusesAnEditor()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "IMS", "simple_tasks");
        using var editor = await AddMemberAsync(owner, "IMS", "editor", "Editor Example", RealStackSteps.UniqueEmail("import-editor"));
        var body = new { rows = new[] { new { type = "task", title = "已完成", state = "done" } } };

        // Act
        using var refused = await editor.Client.PostAsync("/v1/projects/IMS/items/import", ApiRequests.Json(body));
        using var accepted = await owner.Client.PostAsync("/v1/projects/IMS/items/import", ApiRequests.Json(body));
        await ApiRequests.AssertSuccessEnvelopeAsync(accepted, HttpStatusCode.OK);
        var item = await RealStackSteps.GetAsync(owner.Client, "/v1/items/IMS-1");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(refused, HttpStatusCode.Forbidden, WorkErrorCodes.Forbidden);
        Assert.That(item["state"]?.GetValue<string>(), Is.EqualTo("done"));
    }

    [Test]
    public async Task Import_ArchivedProject_Returns409()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "IMA", "simple_tasks");
        using var archive = await owner.Client.PostAsync("/v1/projects/IMA/archive", null);
        await ApiRequests.AssertSuccessEnvelopeAsync(archive, HttpStatusCode.OK);

        // Act
        using var response = await owner.Client.PostAsync(
            "/v1/projects/IMA/items/import", ApiRequests.Json(new { rows = new[] { new { type = "task", title = "甲" } } }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Conflict, WorkErrorCodes.ProjectArchived);
    }

    [Test]
    public async Task Import_AssignedToAnotherMember_DoesNotFloodTheirInboxWithAssignedNotices()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "IMN", "simple_tasks");
        using var member = await AddMemberAsync(owner, "IMN", "editor", "Member Example", RealStackSteps.UniqueEmail("import-notice"));
        await RealStackSteps.CreateItemAsync(owner.Client, "IMN", new { type = "task", title = "一個一個建的", assigneeId = member.PersonId });

        // Act
        using var response = await owner.Client.PostAsync(
            "/v1/projects/IMN/items/import",
            ApiRequests.Json(new { rows = new[] { new { type = "task", title = "匯入一", assigneeId = member.PersonId }, new { type = "task", title = "匯入二", assigneeId = member.PersonId } } }));
        await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
        await Factory.RunNotificationsOnceAsync();
        var inbox = await RealStackSteps.GetAsync(member.Client, "/v1/notifications?limit=100");

        // Assert
        var mine = inbox["items"]!.AsArray().Where(n => n?["projectKey"]?.GetValue<string>() == "IMN").Select(n => n!["itemKey"]?.GetValue<string>()).ToArray();
        Assert.That(mine, Is.EqualTo(new[] { "IMN-1" }), "只有一個一個建的那個有通知");
    }
}
