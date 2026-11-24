using System.Net;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>組織管理員新增一般成員；成員只看得到自己被加入的專案，權限照被指派的角色。</summary>
[TestFixture]
public sealed class MemberAccessTests : RealStackTestBase
{
    [Test]
    public async Task NewMember_BeforeBeingAdded_CannotSeeOrJoinTheProject()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "HIDDEN", "simple_tasks");
        var person = await RealStackSteps.CreateMemberAsync(owner.Client, "Outsider Example", "outsider@example.com");

        // Act
        using var member = await SignInAsAsync("outsider@example.com", RealStackSteps.MemberPassword);
        var session = await RealStackSteps.GetAsync(member.Client, Sessions.Route);
        var projects = await RealStackSteps.GetAsync(member.Client, "/v1/projects");
        using var detail = await member.Client.GetAsync("/v1/projects/HIDDEN");
        using var join = await RealStackSteps.PutMemberAsync(member.Client, "HIDDEN", member.PersonId, "editor");

        // Assert：看不到的專案和不存在的一樣，連自己加入也不行。
        await ApiRequests.AssertFailureEnvelopeAsync(detail, HttpStatusCode.NotFound, WorkErrorCodes.ProjectNotFound);
        await ApiRequests.AssertFailureEnvelopeAsync(join, HttpStatusCode.NotFound, WorkErrorCodes.ProjectNotFound);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(person["role"]?.GetValue<string>(), Is.EqualTo("member"));
            Assert.That(person["personId"]?.GetValue<Guid>(), Is.EqualTo(member.PersonId));
            Assert.That(session["user"]?["role"]?.GetValue<string>(), Is.EqualTo("member"));
            Assert.That(projects.AsArray(), Is.Empty);
        }
    }

    [Test]
    public async Task Viewer_SeesTheProjectButCannotCreateItemsOrManageMembers()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "SHARED", "simple_tasks");
        var person = await RealStackSteps.CreateMemberAsync(owner.Client, "Viewer Example", "viewer@example.com");
        var personId = person["personId"]!.GetValue<Guid>();
        var added = await RealStackSteps.SetMemberAsync(owner.Client, "SHARED", personId, "viewer");
        using var viewer = await SignInAsAsync("viewer@example.com", RealStackSteps.MemberPassword);

        // Act
        var projects = await RealStackSteps.GetAsync(viewer.Client, "/v1/projects");
        var detail = await RealStackSteps.GetAsync(viewer.Client, "/v1/projects/SHARED");
        using var create = await viewer.Client.PostAsync(
            "/v1/projects/SHARED/items", ApiRequests.Json(new { type = "task", title = "檢視者不能新增" }));
        using var promote = await RealStackSteps.PutMemberAsync(viewer.Client, "SHARED", viewer.PersonId, "admin");

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(create, HttpStatusCode.Forbidden, WorkErrorCodes.Forbidden);
        await ApiRequests.AssertFailureEnvelopeAsync(promote, HttpStatusCode.Forbidden, WorkErrorCodes.Forbidden);
        var permissions = detail["myPermissions"]?.AsArray().Select(p => p?.GetValue<string>()).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(added["displayName"]?.GetValue<string>(), Is.EqualTo("Viewer Example"));
            Assert.That(RealStackSteps.KeysOf(projects), Is.EqualTo(new[] { "SHARED" }));
            Assert.That(projects[0]?["myRole"]?.GetValue<string>(), Is.EqualTo("viewer"));
            Assert.That(detail["project"]?["myRole"]?.GetValue<string>(), Is.EqualTo("viewer"));
            Assert.That(permissions, Does.Contain(WorkPermissions.ItemRead));
            Assert.That(permissions, Does.Not.Contain(WorkPermissions.ItemCreate));
        }
    }
}
