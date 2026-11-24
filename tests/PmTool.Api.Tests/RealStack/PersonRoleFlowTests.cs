using System.Net;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests.RealStack;

/// <summary>擁有者改別人的組織角色，從 HTTP 一路到資料庫：同一個 Cookie 的下一個請求就是新的角色。</summary>
[TestFixture]
public sealed class PersonRoleFlowTests : RealStackTestBase
{
    [Test]
    public async Task SetRole_AdminDemotedToMember_TheSameCookieNoLongerSeesProjectsTheyAreNotIn()
    {
        // Arrange：管理員看得到組織裡所有專案；先查一次，讓舊角色進快取。
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "ROLEFLOW", "simple_tasks");
        var email = RealStackSteps.UniqueEmail("role");
        var person = await RealStackSteps.CreateMemberAsync(owner.Client, "Role Example", email);
        var personId = person["personId"]!.GetValue<Guid>();
        using var promoted = await owner.Client.PutAsync($"/v1/people/{personId}/role", ApiRequests.Json(new { role = "admin" }));
        await ApiRequests.AssertSuccessEnvelopeAsync(promoted, HttpStatusCode.OK);
        using var admin = await SignInAsAsync(email, RealStackSteps.MemberPassword);
        var before = await RealStackSteps.GetAsync(admin.Client, "/v1/projects");
        var sessionBefore = await RealStackSteps.GetAsync(admin.Client, Sessions.Route);

        // Act
        using var response = await owner.Client.PutAsync($"/v1/people/{personId}/role", ApiRequests.Json(new { role = "member" }));
        var changed = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var after = await RealStackSteps.GetAsync(admin.Client, "/v1/projects");
        var sessionAfter = await RealStackSteps.GetAsync(admin.Client, Sessions.Route);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(changed?["role"]?.GetValue<string>(), Is.EqualTo("member"));
            Assert.That(before.AsArray().Select(p => p?["key"]?.GetValue<string>()), Does.Contain("ROLEFLOW"));
            Assert.That(sessionBefore["user"]?["role"]?.GetValue<string>(), Is.EqualTo("admin"));
            Assert.That(after.AsArray(), Is.Empty);
            Assert.That(sessionAfter["user"]?["role"]?.GetValue<string>(), Is.EqualTo("member"));
        }
    }

    [Test]
    public async Task SetRole_TheirOwnRole_Returns409()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();

        // Act
        using var response = await owner.Client.PutAsync($"/v1/people/{owner.PersonId}/role", ApiRequests.Json(new { role = "member" }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Conflict, IdentityErrorCodes.IsSelf);
    }

    [Test]
    public async Task SetRole_ByAMember_Returns403()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        var email = RealStackSteps.UniqueEmail("plain");
        await RealStackSteps.CreateMemberAsync(owner.Client, "Plain Example", email);
        using var member = await SignInAsAsync(email, RealStackSteps.MemberPassword);

        // Act
        using var response = await member.Client.PutAsync($"/v1/people/{owner.PersonId}/role", ApiRequests.Json(new { role = "guest" }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, IdentityErrorCodes.Forbidden);
    }
}
