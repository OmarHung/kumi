using System.Net;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

/// <summary>除了登入、健康檢查與兩個驗證端點，每個端點都要先登入。新加的端點也要列進來。</summary>
[TestFixture]
public sealed class SignInRequiredTests
{
    private static readonly object[] SignedInEndpoints =
    [
        new object[] { "GET", "/v1/session" },
        new object[] { "GET", "/v1/cli" },
        new object[] { "GET", "/v1/cli/download/macos-arm64" },
        new object[] { "GET", "/v1/templates" },
        new object[] { "GET", "/v1/projects" },
        new object[] { "POST", "/v1/projects" },
        new object[] { "GET", "/v1/projects/WEB" },
        new object[] { "PUT", $"/v1/projects/WEB/members/{TestUsers.Member.PersonId}" },
        new object[] { "GET", "/v1/projects/WEB/items" },
        new object[] { "POST", "/v1/projects/WEB/items" },
        new object[] { "GET", "/v1/items/WEB-1" },
        new object[] { "PATCH", "/v1/items/WEB-1" },
        new object[] { "POST", "/v1/items/WEB-1/transitions" },
        new object[] { "GET", "/v1/projects/WEB/items/deleted" },
        new object[] { "POST", "/v1/items/WEB-1/restore" },
        new object[] { "POST", "/v1/projects/WEB/items/bulk-update" },
        new object[] { "POST", "/v1/projects/WEB/items/bulk-delete" },
        new object[] { "POST", "/v1/projects/WEB/items/bulk-restore" },
        new object[] { "GET", "/v1/items/WEB-1/activity" },
        new object[] { "POST", "/v1/items/WEB-1/comments" },
        new object[] { "GET", "/v1/projects/WEB/changesets" },
        new object[] { "POST", "/v1/projects/WEB/changesets" },
        new object[] { "GET", "/v1/projects/WEB/changesets/1" },
        new object[] { "POST", "/v1/projects/WEB/changesets/1/apply" },
        new object[] { "POST", "/v1/projects/WEB/changesets/1/reject" },
        new object[] { "GET", "/v1/my-work" },
        new object[] { "GET", "/v1/search/items?q=a" },
        new object[] { "GET", "/v1/search/projects?q=a" },
        new object[] { "GET", "/v1/people" },
        new object[] { "POST", "/v1/people" },
        new object[] { "POST", $"/v1/people/{TestUsers.Member.PersonId}/disable" },
        new object[] { "POST", $"/v1/people/{TestUsers.Member.PersonId}/enable" },
        new object[] { "PUT", $"/v1/people/{TestUsers.Member.PersonId}/password" },
        new object[] { "PUT", $"/v1/people/{TestUsers.Member.PersonId}/role" },
        new object[] { "PUT", "/v1/account/password" },
        new object[] { "POST", "/v1/account/other-sessions/sign-out" },
        new object[] { "GET", "/v1/email-settings" },
        new object[] { "PUT", "/v1/email-settings" },
        new object[] { "POST", "/v1/email-settings/test" },
        new object[] { "GET", "/v1/account/email-notifications" },
        new object[] { "PUT", "/v1/account/email-notifications" },
        new object[] { "POST", "/v1/client-errors" },
        new object[] { "GET", "/v1/ai/host" },
        new object[] { "POST", "/v1/ai/host/agents" },
        new object[] { "PATCH", $"/v1/ai/host/agents/{FakeHostAgentAdminService.AgentId}" },
        new object[] { "POST", $"/v1/ai/host/agents/{FakeHostAgentAdminService.AgentId}/pairing" },
        new object[] { "DELETE", $"/v1/ai/host/agents/{FakeHostAgentAdminService.AgentId}" },
        new object[] { "POST", $"/v1/ai/host/agents/{FakeHostAgentAdminService.AgentId}/engines/codex/login" },
        new object[] { "POST", $"/v1/ai/host/agents/{FakeHostAgentAdminService.AgentId}/engines/codex/logout" },
        new object[] { "DELETE", $"/v1/ai/host/logins/{FakeHostAgentAdminService.LoginId}" },
        new object[] { "POST", "/v1/ai/host/sources" },
        new object[] { "PUT", $"/v1/ai/host/sources/{FakeHostAgentAdminService.SourceId}" },
        new object[] { "DELETE", $"/v1/ai/host/sources/{FakeHostAgentAdminService.SourceId}" },
        new object[] { "GET", $"/v1/ai/host/sources/{FakeHostAgentAdminService.SourceId}/usage" },
    ];

    [TestCaseSource(nameof(SignedInEndpoints))]
    public async Task Endpoint_WithoutSession_Returns401WithoutReachingTheModules(string method, string route)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), route) { Content = ApiRequests.Json(new { }) };

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.Modules.Projects.ListCalls, Is.Empty);
            Assert.That(factory.Modules.Projects.CreateCalls, Is.Empty);
            Assert.That(factory.Modules.Items.GetCalls, Is.Empty);
            Assert.That(factory.Modules.Activity.ListCalls, Is.Empty);
            Assert.That(factory.Modules.Activity.CommentCalls, Is.Empty);
            Assert.That(factory.Modules.Changes.TotalCalls, Is.Zero);
            Assert.That(factory.Modules.People.CreateCalls, Is.Empty);
            Assert.That(factory.Modules.People.SetDisabledCalls, Is.Empty);
            Assert.That(factory.Modules.People.ResetPasswordCalls, Is.Empty);
            Assert.That(factory.Modules.People.SetRoleCalls, Is.Empty);
            Assert.That(factory.Modules.Accounts.Calls, Is.Empty);
            Assert.That(factory.Modules.EmailSettings.GetCalls, Is.Empty);
            Assert.That(factory.Modules.EmailSettings.SaveCalls, Is.Empty);
            Assert.That(factory.Modules.EmailSettings.TestCalls, Is.Empty);
            Assert.That(factory.Modules.EmailPreferences.SetCalls, Is.Empty);
            Assert.That(factory.Modules.Sessions.EndOthersCalls, Is.Empty);
            Assert.That(factory.Modules.HostAdmin.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task Endpoint_WithACookieThatCannotBeDecrypted_Returns401()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        using var request = Sessions.WithCookie(HttpMethod.Get, "/v1/projects", $"{Sessions.CookieName}=forged-value");

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
    }
}
