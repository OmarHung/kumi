using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>GET /v1/audit-events：只給 Cookie、權限在模組、回應帶上人的顯示名稱。</summary>
[TestFixture]
public sealed class AuditEndpointTests
{
    private const string Route = "/v1/audit-events";

    private static AuditEntry Entry(Guid? actor, Guid? target, string action = AuditActions.PasswordReset) => new(
        Guid.Parse("0190b000-0000-7000-8000-000000000001"),
        new DateTimeOffset(2026, 11, 25, 8, 0, 0, TimeSpan.Zero),
        action,
        AuditOutcomes.Succeeded,
        actor,
        target,
        "203.0.113.5",
        new Dictionary<string, string> { ["tokenName"] = "CI" });

    [Test]
    public async Task List_Owner_ReturnsTheEntriesWithDisplayNames()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var gone = Guid.NewGuid();
        factory.Modules.Audit.OnList = (_, _) => new AuditPage([Entry(TestUsers.Owner.PersonId, gone)], "next-cursor");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync(Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expected = JsonNode.Parse(
            """
            {"items":[{"eventId":"0190b000-0000-7000-8000-000000000001","occurredAt":"2026-11-25T08:00:00Z",
              "action":"password_reset","outcome":"succeeded",
              "actor":{"personId":"@OWNER@","displayName":"@NAME@"},
              "target":{"personId":"@GONE@","displayName":null},
              "sourceAddress":"203.0.113.5","details":{"tokenName":"CI"}}],
             "nextCursor":"next-cursor"}
            """.Replace("@OWNER@", TestUsers.Owner.PersonId.ToString()).Replace("@NAME@", TestUsers.Owner.DisplayName).Replace("@GONE@", gone.ToString()));
        Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
    }

    [Test]
    public async Task List_EntryWithoutActorOrTarget_ReturnsNullForThem()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Audit.OnList = (_, _) => new AuditPage([Entry(null, null)], null);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync(Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var item = data?["items"]?[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(item?["actor"], Is.Null);
            Assert.That(item?["target"], Is.Null);
            Assert.That(data?["nextCursor"], Is.Null);
        }
    }

    [Test]
    public async Task List_PassesTheQueryToTheModuleWithTheCallersIdentity()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync($"{Route}?limit=20&cursor=abc&person={TestUsers.Member.PersonId}");

        // Assert
        var call = factory.Modules.Audit.ListCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(call.Actor.PersonId, Is.EqualTo(TestUsers.Owner.PersonId));
            Assert.That(call.Actor.OrganizationId, Is.EqualTo(TestUsers.OrganizationId));
            Assert.That(call.Query, Is.EqualTo(new AuditQuery("abc", 20, TestUsers.Member.PersonId)));
        }
    }

    [Test]
    public async Task List_WithoutParameters_UsesTheDefaults()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync(Route);

        // Assert
        Assert.That(factory.Modules.Audit.ListCalls.Single().Query, Is.EqualTo(new AuditQuery(null, AuditRules.DefaultLimit, null)));
    }

    [TestCase("limit=abc", "limit")]
    [TestCase("person=not-a-guid", "person")]
    [TestCase("limit=1&limit=2", "limit")]
    public async Task List_BadParameterFromAnOwner_Returns422AuditQueryInvalidWithoutRunningTheQuery(string query, string path)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync($"{Route}?{query}");

        // Assert
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, IdentityErrorCodes.AuditQueryInvalid);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(details[0]?["path"]?.GetValue<string>(), Is.EqualTo(path));
            Assert.That(factory.Modules.Audit.ListCalls.Select(call => call.Query.Limit), Is.All.EqualTo(0), "只問了權限，沒有真的查");
        }
    }

    [TestCase("limit=abc")]
    [TestCase("person=not-a-guid")]
    [TestCase("limit=1&limit=2")]
    public async Task List_BadParameterFromANonOwner_Returns403NotTheParameterError(string query)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Audit.OnList = (_, _) => new Failure(FailureKind.Forbidden, IdentityErrorCodes.Forbidden, "只有組織的擁有者能看稽核紀錄。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.GetAsync($"{Route}?{query}");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, IdentityErrorCodes.Forbidden);
    }

    [Test]
    public async Task List_ModuleSaysForbidden_Returns403()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Audit.OnList = (_, _) => new Failure(FailureKind.Forbidden, IdentityErrorCodes.Forbidden, "只有組織的擁有者能看稽核紀錄。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.GetAsync(Route);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, IdentityErrorCodes.Forbidden);
    }

    [Test]
    public async Task List_ModuleSaysInvalid_Returns422()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Audit.OnList = (_, _) => new Failure(FailureKind.Invalid, IdentityErrorCodes.AuditQueryInvalid, "分頁的游標不對。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync($"{Route}?cursor=x");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
    }

    [Test]
    public async Task List_NotSignedIn_Returns401()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();

        // Act
        using var response = await client.GetAsync(Route);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
    }

    [Test]
    public async Task List_AccessTokenWithEveryScope_Returns403InsufficientScope()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(
            TestUsers.Owner, AccessTokenScope.ItemsRead, AccessTokenScope.ItemsWrite, AccessTokenScope.BlueprintRead,
            AccessTokenScope.BlueprintWrite);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, Route, secret));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.InsufficientScope);
        Assert.That(factory.Modules.Audit.ListCalls, Is.Empty);
    }
}
