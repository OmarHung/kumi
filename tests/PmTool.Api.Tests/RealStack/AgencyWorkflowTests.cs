using System.Net;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 「接案專案」的交付流程：一個需求從需求確認一路走到結案。轉換的 allowedRoles 看的是在專案裡被指派的角色，
/// 組織的擁有者也一樣；開專案的人是專案的 admin，不在允許的角色裡，要先把自己改成 project_manager。
/// </summary>
[TestFixture]
public sealed class AgencyWorkflowTests : RealStackTestBase
{
    private static readonly string[] UpToInternalQa = ["quoting", "designing", "developing", "internal_qa"];

    [Test]
    public async Task Requirement_ReachesClientAcceptanceOnlyOnceTheOwnerIsProjectManager()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        var item = await CreateRequirementAsync(owner, "FLOWA");

        // Act
        var internalQa = await RealStackSteps.TransitionThroughAsync(owner.Client, item, UpToInternalQa);
        using var asAdmin = await RealStackSteps.TransitionAsync(owner.Client, item, "client_qa");
        var member = await RealStackSteps.SetMemberAsync(owner.Client, "FLOWA", owner.PersonId, "project_manager");
        var clientQa = await RealStackSteps.TransitionThroughAsync(owner.Client, item, "client_qa");

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(asAdmin, HttpStatusCode.Forbidden, WorkErrorCodes.TransitionRoleRequired);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(internalQa["stateCategory"]?.GetValue<string>(), Is.EqualTo("in_progress"));
            Assert.That(member["displayName"]?.GetValue<string>(), Is.EqualTo(RealStackFactory.OwnerName));
            Assert.That(clientQa["state"]?.GetValue<string>(), Is.EqualTo("client_qa"));
        }
    }

    [Test]
    public async Task Requirement_AtClientAcceptance_ClosesOnlyForTheProjectManagerWithTheSignOffDate()
    {
        // Arrange：先當專案經理把需求送到客戶驗收，再改回 admin。
        using var owner = await SignInAsOwnerAsync();
        var item = await CreateRequirementAsync(owner, "FLOWB");
        await RealStackSteps.SetMemberAsync(owner.Client, "FLOWB", owner.PersonId, "project_manager");
        await RealStackSteps.TransitionThroughAsync(owner.Client, item, [.. UpToInternalQa, "client_qa"]);
        await RealStackSteps.SetMemberAsync(owner.Client, "FLOWB", owner.PersonId, "admin");

        // Act
        using var asAdmin = await RealStackSteps.TransitionAsync(owner.Client, item, "closed");
        await RealStackSteps.SetMemberAsync(owner.Client, "FLOWB", owner.PersonId, "project_manager");
        using var withoutSignOff = await RealStackSteps.TransitionAsync(owner.Client, item, "closed");
        await RealStackSteps.PatchItemAsync(owner.Client, item, new { fields = new { client_signoff_date = "2026-11-30" } });
        var closed = await RealStackSteps.TransitionThroughAsync(owner.Client, item, "closed");
        var project = await RealStackSteps.GetAsync(owner.Client, "/v1/projects/FLOWB");

        // Assert
        var roleDetails = await ApiRequests.AssertFailureEnvelopeAsync(
            asAdmin, HttpStatusCode.Forbidden, WorkErrorCodes.TransitionRoleRequired);
        var fieldDetails = await ApiRequests.AssertFailureEnvelopeAsync(
            withoutSignOff, HttpStatusCode.UnprocessableEntity, WorkErrorCodes.TransitionFieldsRequired);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ApiRequests.Describe(roleDetails), Is.EqualTo(new[] { $"{WorkErrorCodes.TransitionRoleRequired}|role" }));
            Assert.That(roleDetails[0]?["message"]?.GetValue<string>(), Does.Contain("project_manager"));
            Assert.That(ApiRequests.Describe(fieldDetails), Is.EqualTo(new[]
            {
                $"{WorkErrorCodes.TransitionFieldsRequired}|client_signoff_date",
            }));
            Assert.That(closed["stateCategory"]?.GetValue<string>(), Is.EqualTo("done"));
            Assert.That(closed["fields"]?["client_signoff_date"]?.GetValue<string>(), Is.EqualTo("2026-11-30"));
            Assert.That(project["project"]?["myRole"]?.GetValue<string>(), Is.EqualTo("project_manager"));
        }
    }

    private static async Task<string> CreateRequirementAsync(SignedInClient owner, string projectKey)
    {
        await RealStackSteps.CreateProjectAsync(owner.Client, projectKey, "agency_project");
        var item = await RealStackSteps.CreateItemAsync(owner.Client, projectKey, new { type = "requirement", title = "首頁改版需求" });
        Assert.That(item["state"]?.GetValue<string>(), Is.EqualTo("confirming"));
        return item["key"]!.GetValue<string>();
    }
}
