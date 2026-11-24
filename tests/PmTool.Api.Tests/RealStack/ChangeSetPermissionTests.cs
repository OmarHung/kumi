using System.Net;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 誰能做什麼：送出要 blueprint.propose、讀要 blueprint.read、套用要 blueprint.apply；
/// 拒絕是提出的人或有 blueprint.apply 的人。看不到的專案和不存在的一樣。
/// </summary>
[TestFixture]
public sealed class ChangeSetPermissionTests : RealStackTestBase
{
    [Test]
    public async Task Editor_CannotProposeAChangeSet()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        var document = await AddReviewStateAsync(owner, "EDITOR");
        using var editor = await AddMemberAsync(owner, "EDITOR", "editor", "Editor Example", "editor@example.com");

        // Act
        using var propose = await ChangeSetSteps.ProposeAsync(editor.Client, "EDITOR", document);
        var list = await RealStackSteps.GetAsync(editor.Client, ChangeSetSteps.RouteOf("EDITOR"));

        // Assert
        var details = await ApiRequests.AssertFailureEnvelopeAsync(propose, HttpStatusCode.Forbidden, WorkErrorCodes.Forbidden);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(details, Is.Empty);
            Assert.That(list.AsArray(), Is.Empty, "編輯者讀得到清單，被擋下的沒有存");
        }
    }

    [Test]
    public async Task Viewer_ReadsTheChangeSetsButCannotApplyOrRejectThem()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await ChangeSetSteps.ProposedAsync(owner.Client, "VIEWER", await AddReviewStateAsync(owner, "VIEWER"));
        using var viewer = await AddMemberAsync(owner, "VIEWER", "viewer", "Viewer Example", "viewer@example.com");

        // Act
        var list = await RealStackSteps.GetAsync(viewer.Client, ChangeSetSteps.RouteOf("VIEWER"));
        var detail = await ChangeSetSteps.GetAsync(viewer.Client, "VIEWER", 1);
        using var apply = await ChangeSetSteps.ApplyAsync(viewer.Client, "VIEWER", 1);
        using var reject = await ChangeSetSteps.RejectAsync(viewer.Client, "VIEWER", 1);
        var afterwards = await ChangeSetSteps.GetAsync(owner.Client, "VIEWER", 1);

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(apply, HttpStatusCode.Forbidden, WorkErrorCodes.Forbidden);
        await ApiRequests.AssertFailureEnvelopeAsync(reject, HttpStatusCode.Forbidden, WorkErrorCodes.Forbidden);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(list.AsArray().Select(ChangeSetSteps.Describe), Is.EqualTo(new[]
            {
                "#1 pending base=1 applied=null by=Owner Example decidedBy=null",
            }));
            Assert.That(ChangeSetSteps.DescribeChanges(detail["changes"]), Is.EqualTo(new[]
            {
                "added|state|review|task_flow|",
            }));
            Assert.That(detail["canApply"]?.GetValue<bool>(), Is.False, "檢視者沒有核准套用的權限");
            Assert.That(afterwards["canApply"]?.GetValue<bool>(), Is.True, "擁有者看同一份還是可以套用");
            Assert.That(ChangeSetSteps.StatusOf(afterwards), Is.EqualTo("pending"));
        }
    }

    [Test]
    public async Task Proposer_WithoutApplyPermission_CanRejectOnlyTheirOwnChangeSet()
    {
        // Arrange：先套用一份變更集，加上一個只能提出、不能套用的自訂角色。
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "OWN", "simple_tasks");
        var first = await ChangeSetSteps.BlueprintOfAsync(owner.Client, "OWN");
        await ChangeSetSteps.ProposedAsync(owner.Client, "OWN", ChangeSetSteps.Json(1, "加上提案人角色", ChangeSetSteps.WithRole(
            first, "proposer", "提案人", WorkPermissions.ItemRead, WorkPermissions.BlueprintRead, WorkPermissions.BlueprintPropose)));
        await ChangeSetSteps.AppliedAsync(owner.Client, "OWN", 1);
        using var proposer = await AddMemberAsync(owner, "OWN", "proposer", "Proposer Example", "proposer@example.com");
        var second = await ChangeSetSteps.BlueprintOfAsync(owner.Client, "OWN");
        await ChangeSetSteps.ProposedAsync(owner.Client, "OWN", ChangeSetSteps.Json(
            2, "加上審查中", ChangeSetSteps.WithState(second, "task_flow", "review", "審查中", "in_progress")));

        // Act
        var own = await ChangeSetSteps.ProposedAsync(proposer.Client, "OWN", ChangeSetSteps.Json(
            2, "加上工時", ChangeSetSteps.WithField(second, "task", "effort_hours", "工時", "number")));
        using var apply = await ChangeSetSteps.ApplyAsync(proposer.Client, "OWN", 3);
        using var rejectOthers = await ChangeSetSteps.RejectAsync(proposer.Client, "OWN", 2);
        using var rejectOwn = await ChangeSetSteps.RejectAsync(proposer.Client, "OWN", 3);
        var list = await RealStackSteps.GetAsync(owner.Client, ChangeSetSteps.RouteOf("OWN"));

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(apply, HttpStatusCode.Forbidden, WorkErrorCodes.Forbidden);
        await ApiRequests.AssertFailureEnvelopeAsync(rejectOthers, HttpStatusCode.Forbidden, WorkErrorCodes.Forbidden);
        var rejected = await ApiRequests.AssertSuccessEnvelopeAsync(rejectOwn, HttpStatusCode.OK);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(own["canApply"]?.GetValue<bool>(), Is.False, "提案人沒有核准套用的權限");
            Assert.That(ChangeSetSteps.Describe(rejected?["changeSet"]), Is.EqualTo("#3 rejected base=2 applied=null by=Proposer Example decidedBy=Proposer Example"));
            Assert.That(rejected?["changeSet"]?["decidedBy"]?["personId"]?.GetValue<Guid>(), Is.EqualTo(proposer.PersonId));
            Assert.That(list.AsArray().Select(ChangeSetSteps.Describe), Is.EqualTo(new[]
            {
                "#3 rejected base=2 applied=null by=Proposer Example decidedBy=Proposer Example",
                "#2 pending base=2 applied=null by=Owner Example decidedBy=null",
                "#1 applied base=1 applied=2 by=Owner Example decidedBy=Owner Example",
            }));
        }
    }

    [Test]
    public async Task NonMember_CannotSeeOrTouchTheChangeSets()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await ChangeSetSteps.ProposedAsync(owner.Client, "SECRET", await AddReviewStateAsync(owner, "SECRET"));
        var blueprint = await ChangeSetSteps.BlueprintOfAsync(owner.Client, "SECRET");
        await RealStackSteps.CreateMemberAsync(owner.Client, "Outsider Example", "outsider@example.com");
        using var outsider = await SignInAsAsync("outsider@example.com", RealStackSteps.MemberPassword);

        // Act
        using var propose = await ChangeSetSteps.ProposeAsync(outsider.Client, "SECRET", ChangeSetSteps.Json(
            1, "外人加上卡關", ChangeSetSteps.WithState(blueprint, "task_flow", "stuck", "卡關", "in_progress")));
        using var list = await outsider.Client.GetAsync(ChangeSetSteps.RouteOf("SECRET"));
        using var detail = await outsider.Client.GetAsync(ChangeSetSteps.RouteOf("SECRET", 1));
        using var apply = await ChangeSetSteps.ApplyAsync(outsider.Client, "SECRET", 1);
        using var reject = await ChangeSetSteps.RejectAsync(outsider.Client, "SECRET", 1);
        using var unknown = await owner.Client.GetAsync(ChangeSetSteps.RouteOf("SECRET", 99));
        var remaining = await RealStackSteps.GetAsync(owner.Client, ChangeSetSteps.RouteOf("SECRET"));

        // Assert：專案的 404 和沒有這份變更集的 404 代碼不同；看不到專案的人只會拿到前者。
        await ApiRequests.AssertFailureEnvelopeAsync(propose, HttpStatusCode.NotFound, WorkErrorCodes.ProjectNotFound);
        await ApiRequests.AssertFailureEnvelopeAsync(list, HttpStatusCode.NotFound, WorkErrorCodes.ProjectNotFound);
        await ApiRequests.AssertFailureEnvelopeAsync(detail, HttpStatusCode.NotFound, WorkErrorCodes.ProjectNotFound);
        await ApiRequests.AssertFailureEnvelopeAsync(apply, HttpStatusCode.NotFound, WorkErrorCodes.ProjectNotFound);
        await ApiRequests.AssertFailureEnvelopeAsync(reject, HttpStatusCode.NotFound, WorkErrorCodes.ProjectNotFound);
        await ApiRequests.AssertFailureEnvelopeAsync(unknown, HttpStatusCode.NotFound, WorkErrorCodes.ChangeSetNotFound);
        Assert.That(remaining.AsArray().Select(ChangeSetSteps.Describe), Is.EqualTo(new[]
        {
            "#1 pending base=1 applied=null by=Owner Example decidedBy=null",
        }));
    }

    /// <summary>開一個「簡單任務看板」的專案，回傳加上「審查中」狀態的變更集文件。</summary>
    private static async Task<HttpContent> AddReviewStateAsync(SignedInClient owner, string key)
    {
        await RealStackSteps.CreateProjectAsync(owner.Client, key, "simple_tasks");
        var blueprint = await ChangeSetSteps.BlueprintOfAsync(owner.Client, key);
        return ChangeSetSteps.Json(1, "加上審查中", ChangeSetSteps.WithState(blueprint, "task_flow", "review", "審查中", "in_progress"));
    }
}
