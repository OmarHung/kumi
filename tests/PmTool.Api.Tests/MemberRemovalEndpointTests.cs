using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>移除成員與最後一位 admin。看不看得到專案、能不能管理成員、是不是成員都由工作引擎判斷。</summary>
[TestFixture]
public sealed class MemberRemovalEndpointTests
{
    [Test]
    public async Task RemoveMember_Member_ReturnsHisMembershipWithoutLookingUpThePerson()
    {
        // Arrange：要移除的人可能已經不在組織裡了，所以不查人員目錄，也就不帶顯示名稱。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        var person = TestUsers.Member.PersonId;

        // Act
        using var response = await client.DeleteAsync($"/v1/projects/WEB/members/{person}");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data, new JsonObject { ["personId"] = person, ["role"] = "editor" }), Is.True, data?.ToJsonString());
            Assert.That(factory.Modules.Projects.RemoveMemberCalls, Is.EqualTo(new[] { (TestUsers.Owner.ToActor(), "WEB", person) }));
            Assert.That(factory.Modules.People.FindManyCalls, Is.Empty);
        }
    }

    [TestCase(FailureKind.NotFound, WorkErrorCodes.MemberNotFound, HttpStatusCode.NotFound)]
    [TestCase(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, HttpStatusCode.NotFound)]
    [TestCase(FailureKind.Forbidden, WorkErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    [TestCase(FailureKind.Conflict, WorkErrorCodes.LastAdmin, HttpStatusCode.Conflict)]
    public async Task RemoveMember_ModuleFails_ReturnsTheMatchingStatusWithTheModuleCode(FailureKind kind, string code, HttpStatusCode expected)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Projects.OnRemoveMember = (_, _, _) => new Failure(kind, code, "模組說的原因。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.DeleteAsync($"/v1/projects/WEB/members/{TestUsers.Member.PersonId}");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, expected, code);
    }

    [Test]
    public async Task RemoveMember_PersonIdIsNotAGuid_Returns404WithoutCallingTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.DeleteAsync("/v1/projects/WEB/members/not-a-guid");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, ApiErrorCodes.NotFound);
        Assert.That(factory.Modules.Projects.RemoveMemberCalls, Is.Empty);
    }

    [Test]
    public async Task SetMember_DemotingTheLastAdmin_Returns409()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Projects.OnSetMember = (_, _, _, _) =>
            new Failure(FailureKind.Conflict, WorkErrorCodes.LastAdmin, "這是專案唯一的管理員。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync(
            $"/v1/projects/WEB/members/{TestUsers.Owner.PersonId}", ApiRequests.Json(new { role = "editor" }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Conflict, WorkErrorCodes.LastAdmin);
    }
}
