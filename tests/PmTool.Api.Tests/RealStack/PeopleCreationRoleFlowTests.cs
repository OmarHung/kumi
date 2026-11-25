using System.Net;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests.RealStack;

/// <summary>新增人員的角色規則（真的身分模組）：管理員只能新增一般成員與訪客，擁有者與管理員只能由擁有者新增。</summary>
[TestFixture]
public sealed class PeopleCreationRoleFlowTests : RealStackTestBase
{
    [TestCase("admin")]
    [TestCase("owner")]
    public async Task CreatePerson_AdminAddingAnEqualOrHigherRole_Returns403(string role)
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        using var created = await owner.Client.PostAsync(
            "/v1/people", ApiRequests.Json(new { displayName = "Admin Example", email = $"admin-of-{role}@example.com", password = RealStackSteps.MemberPassword, role = "admin" }));
        await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created);
        using var admin = await SignInAsAsync($"admin-of-{role}@example.com", RealStackSteps.MemberPassword);

        // Act
        using var response = await admin.Client.PostAsync(
            "/v1/people", ApiRequests.Json(new { displayName = "New Example", email = $"new-{role}@example.com", password = RealStackSteps.MemberPassword, role }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, IdentityErrorCodes.Forbidden);
    }

    [Test]
    public async Task CreatePerson_AdminAddingAMember_Succeeds()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        using var created = await owner.Client.PostAsync(
            "/v1/people", ApiRequests.Json(new { displayName = "Admin Two", email = "admin-two@example.com", password = RealStackSteps.MemberPassword, role = "admin" }));
        await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created);
        using var admin = await SignInAsAsync("admin-two@example.com", RealStackSteps.MemberPassword);

        // Act
        var member = await RealStackSteps.CreateMemberAsync(admin.Client, "Member Example", "member-by-admin@example.com");

        // Assert
        Assert.That(member["role"]?.GetValue<string>(), Is.EqualTo("member"));
    }
}
