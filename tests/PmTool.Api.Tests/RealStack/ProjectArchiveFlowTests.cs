using System.Net;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>專案改名與封存的主線（真的資料庫）：改名、封存後唯讀、我的工作不列、取消封存。</summary>
[TestFixture]
public sealed class ProjectArchiveFlowTests : RealStackTestBase
{
    [Test]
    public async Task RenameArchiveAndUnarchive_FullFlow_ChangesTheProjectAndBlocksWritesWhileArchived()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "ARC", "simple_tasks");
        await RealStackSteps.CreateItemAsync(owner.Client, "ARC", new { type = "task", title = "我的事", assigneeId = owner.PersonId });

        // Act
        using var renamed = await owner.Client.PutAsync("/v1/projects/ARC/name", ApiRequests.Json(new { name = "新名稱" }));
        var renamedData = await ApiRequests.AssertSuccessEnvelopeAsync(renamed, HttpStatusCode.OK);
        using var archived = await owner.Client.PostAsync("/v1/projects/ARC/archive", null);
        var archivedData = await ApiRequests.AssertSuccessEnvelopeAsync(archived, HttpStatusCode.OK);
        var detail = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/ARC");
        var myWork = await RealStackSteps.GetAsync(owner.Client, "/v1/my-work");
        using var blocked = await owner.Client.PostAsync(
            "/v1/projects/ARC/items", ApiRequests.Json(new { type = "task", title = "不能新增" }));
        using var unarchived = await owner.Client.PostAsync("/v1/projects/ARC/unarchive", null);
        var unarchivedData = await ApiRequests.AssertSuccessEnvelopeAsync(unarchived, HttpStatusCode.OK);
        var myWorkAfter = await RealStackSteps.GetAsync(owner.Client, "/v1/my-work");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(renamedData?["name"]?.GetValue<string>(), Is.EqualTo("新名稱"));
            Assert.That(archivedData?["archivedAt"]?.GetValue<DateTimeOffset?>(), Is.Not.Null);
            Assert.That(
                detail["myPermissions"]?.AsArray().Select(p => p?.GetValue<string>()),
                Is.EquivalentTo(new[] { WorkPermissions.ItemRead, WorkPermissions.BlueprintRead, WorkPermissions.ProjectManage }));
            Assert.That(myWork["items"]?.AsArray(), Is.Empty);
            Assert.That(unarchivedData?.AsObject().ContainsKey("archivedAt") == true && unarchivedData["archivedAt"] is null, Is.True);
            Assert.That(myWorkAfter["items"]?.AsArray(), Has.Count.EqualTo(1));
        }

        await ApiRequests.AssertErrorEnvelopeAsync(blocked, HttpStatusCode.Conflict, WorkErrorCodes.ProjectArchived);
    }

    [Test]
    public async Task Archive_ByAnEditor_Returns403AndTheProjectStaysLive()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "ARE", "simple_tasks");
        using var editor = await AddMemberAsync(owner, "ARE", "editor", "Editor Example", RealStackSteps.UniqueEmail("arc-editor"));

        // Act
        using var response = await editor.Client.PostAsync("/v1/projects/ARE/archive", null);
        var detail = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/ARE");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, WorkErrorCodes.Forbidden);
        Assert.That(RealStackSteps.IsWrittenAsNull(detail["project"], "archivedAt"), Is.True);
    }

    [Test]
    public async Task Rename_BlankName_Returns422WithTheNamePath()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "ARN", "simple_tasks");

        // Act
        using var response = await owner.Client.PutAsync("/v1/projects/ARN/name", ApiRequests.Json(new { name = "  " }));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.ProjectNameRequired);

        // Assert
        Assert.That(details[0]?["path"]?.GetValue<string>(), Is.EqualTo("name"));
    }
}
