using System.Net;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 移除成員：他之後看不到這個專案，他負責的項目照舊、別的欄位照常能改，只是不能再把他填進去；
/// 不是組織管理員的人不能把專案唯一的管理員移掉或換成別的角色；組織的擁有者（組織管理員）可以，單人使用時才換得了角色。
/// </summary>
[TestFixture]
public sealed class MemberRemovalFlowTests : RealStackTestBase
{
    [Test]
    public async Task RemoveMember_HeNoLongerSeesTheProjectButHisItemsStayAssignedToHim()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "LEAVE", "simple_tasks");
        using var editor = await AddMemberAsync(owner, "LEAVE", "editor", "Leaver Example", "leaver@example.com");
        await RealStackSteps.CreateItemAsync(owner.Client, "LEAVE", new { type = "task", title = "他負責的", assigneeId = editor.PersonId });
        await RealStackSteps.CreateItemAsync(owner.Client, "LEAVE", new { type = "task", title = "沒人負責" });
        var seenBefore = await RealStackSteps.GetAsync(editor.Client, "/v1/projects/LEAVE");

        // Act
        using var removed = await owner.Client.DeleteAsync($"/v1/projects/LEAVE/members/{editor.PersonId}");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(removed, HttpStatusCode.OK);
        using var seenAfter = await editor.Client.GetAsync("/v1/projects/LEAVE");
        var projects = await RealStackSteps.GetAsync(editor.Client, "/v1/projects");
        var mine = await RealStackSteps.GetAsync(editor.Client, "/v1/my-work");
        var members = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/LEAVE");
        var retitled = await RealStackSteps.PatchItemAsync(owner.Client, "LEAVE-1", new { title = "改了標題" });
        using var reassigned = await owner.Client.PatchAsync("/v1/items/LEAVE-2", ApiRequests.Json(new { assigneeId = editor.PersonId }));
        using var again = await owner.Client.DeleteAsync($"/v1/projects/LEAVE/members/{editor.PersonId}");

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(seenAfter, HttpStatusCode.NotFound, WorkErrorCodes.ProjectNotFound);
        await ApiRequests.AssertFailureEnvelopeAsync(reassigned, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.ItemUnknownAssignee);
        await ApiRequests.AssertErrorEnvelopeAsync(again, HttpStatusCode.NotFound, WorkErrorCodes.MemberNotFound);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(seenBefore["project"]?["myRole"]?.GetValue<string>(), Is.EqualTo("editor"));
            Assert.That(data?["personId"]?.GetValue<Guid>(), Is.EqualTo(editor.PersonId));
            Assert.That(data?["role"]?.GetValue<string>(), Is.EqualTo("editor"));
            Assert.That(projects.AsArray(), Is.Empty);
            Assert.That(mine["items"]?.AsArray(), Is.Empty);
            Assert.That(members["members"]?.AsArray().Select(m => m?["personId"]?.GetValue<Guid>()), Is.EqualTo(new[] { (Guid?)owner.PersonId }));
            Assert.That(retitled["assignee"]?["personId"]?.GetValue<Guid>(), Is.EqualTo(editor.PersonId), "負責人照舊");
            Assert.That(retitled["assignee"]?["displayName"]?.GetValue<string>(), Is.EqualTo("Leaver Example"));
        }
    }

    [Test]
    public async Task OnlyAdminWhoIsNotAnOrganizationAdmin_CannotLeaveOrStepDown()
    {
        // Arrange：擁有者開專案、加一位管理員，再把自己移出去；那一位就是唯一的管理員，而且不是組織管理員。
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "SOLO", "simple_tasks");
        using var soleAdmin = await AddMemberAsync(owner, "SOLO", "admin", "Solo Admin Example", "solo-admin@example.com");
        using var ownerLeft = await owner.Client.DeleteAsync($"/v1/projects/SOLO/members/{owner.PersonId}");
        await ApiRequests.AssertSuccessEnvelopeAsync(ownerLeft, HttpStatusCode.OK);

        // Act
        using var removed = await soleAdmin.Client.DeleteAsync($"/v1/projects/SOLO/members/{soleAdmin.PersonId}");
        using var demoted = await RealStackSteps.PutMemberAsync(soleAdmin.Client, "SOLO", soleAdmin.PersonId, "editor");
        var project = await RealStackSteps.GetAsync(soleAdmin.Client, "/v1/projects/SOLO");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(removed, HttpStatusCode.Conflict, WorkErrorCodes.LastAdmin);
        await ApiRequests.AssertErrorEnvelopeAsync(demoted, HttpStatusCode.Conflict, WorkErrorCodes.LastAdmin);
        Assert.That(
            project["members"]?.AsArray().Select(m => (m?["personId"]?.GetValue<Guid>(), m?["role"]?.GetValue<string>())),
            Is.EqualTo(new[] { ((Guid?)soleAdmin.PersonId, (string?)"admin") }));
    }

    [Test]
    public async Task OwnerWhoIsTheOnlyAdmin_CanStepDownAndLeaveAndStillManagesTheProject()
    {
        // Arrange：擁有者開的專案，他是唯一的管理員。
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "OWNER", "simple_tasks");

        // Act
        var demoted = await RealStackSteps.SetMemberAsync(owner.Client, "OWNER", owner.PersonId, "editor");
        var asEditor = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/OWNER");
        using var removed = await owner.Client.DeleteAsync($"/v1/projects/OWNER/members/{owner.PersonId}");
        var removedData = await ApiRequests.AssertSuccessEnvelopeAsync(removed, HttpStatusCode.OK);
        var afterLeaving = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/OWNER");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(demoted["role"]?.GetValue<string>(), Is.EqualTo("editor"));
            Assert.That(asEditor["project"]?["myRole"]?.GetValue<string>(), Is.EqualTo("editor"));
            Assert.That(asEditor["myPermissions"]?.AsArray().Select(p => p?.GetValue<string>()), Does.Contain("project.manage"));
            Assert.That(removedData?["role"]?.GetValue<string>(), Is.EqualTo("editor"));
            Assert.That(afterLeaving["members"]?.AsArray(), Is.Empty);
            Assert.That(afterLeaving["myPermissions"]?.AsArray().Select(p => p?.GetValue<string>()), Does.Contain("project.manage"));
        }
    }
}
