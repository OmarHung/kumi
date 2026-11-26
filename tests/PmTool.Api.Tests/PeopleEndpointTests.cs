using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

[TestFixture]
public sealed class PeopleEndpointTests
{
    [Test]
    public async Task ListPeople_Member_ReturnsTheOrganizationsPeople()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.GetAsync("/v1/people");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var guest = data?.AsArray().Single(p => p?["personId"]?.GetValue<Guid>() == TestUsers.Guest.PersonId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?.AsArray(), Has.Count.EqualTo(3));
            Assert.That(JsonNode.DeepEquals(guest, new JsonObject
            {
                ["personId"] = TestUsers.Guest.PersonId,
                ["displayName"] = TestUsers.Guest.DisplayName,
                ["email"] = TestUsers.Guest.Email,
                ["role"] = "guest",
                ["disabled"] = false,
                ["jobTitle"] = null,
                ["responsibilities"] = null,
            }), Is.True, guest?.ToJsonString());
        }
    }

    [Test]
    public async Task ListPeople_Guest_Returns403Forbidden()
    {
        // Arrange：訪客是客戶或外包，不該看到整個組織的人員與 Email。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Guest);

        // Act
        using var response = await client.GetAsync("/v1/people");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.Forbidden);
    }

    [Test]
    public async Task CreatePerson_Accepted_Returns201AndPassesTheRequest()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        var body = new { displayName = "New Example", email = "new@example.com", password = "long-enough-password", role = "admin" };

        // Act
        using var response = await client.PostAsync("/v1/people", ApiRequests.Json(body));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.Modules.People.CreateCalls, Is.EqualTo(new[]
            {
                (TestUsers.Owner.ToActor(), new NewPerson("New Example", "new@example.com", "long-enough-password", OrganizationRole.Admin)),
            }));
            Assert.That(data?["displayName"]?.GetValue<string>(), Is.EqualTo("New Example"));
            Assert.That(data?["email"]?.GetValue<string>(), Is.EqualTo("new@example.com"));
            Assert.That(data?["role"]?.GetValue<string>(), Is.EqualTo("admin"));
            Assert.That(data?["disabled"]?.GetValue<bool>(), Is.False);
            Assert.That(data?.AsObject().ContainsKey("password"), Is.False);
        }
    }

    [TestCase("1", TestName = "CreatePerson_RoleAsNumber_Returns400")]
    [TestCase("\"superuser\"", TestName = "CreatePerson_UnknownRole_Returns400")]
    [TestCase("\"Admin\"", TestName = "CreatePerson_RoleInAnotherCase_Returns400")]
    public async Task CreatePerson_BadRole(string role)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        var body = $$"""{"displayName":"New Example","email":"new@example.com","password":"long-enough-password","role":{{role}}}""";

        // Act
        using var response = await client.PostAsync("/v1/people", ApiRequests.Document(body));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{ApiErrorCodes.BadRequest}|role" }));
        Assert.That(factory.Modules.People.CreateCalls, Is.Empty);
    }

    [TestCase(FailureKind.Forbidden, IdentityErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    [TestCase(FailureKind.Conflict, IdentityErrorCodes.EmailTaken, HttpStatusCode.Conflict)]
    [TestCase(FailureKind.Invalid, IdentityErrorCodes.PasswordTooShort, HttpStatusCode.UnprocessableEntity)]
    public async Task CreatePerson_ModuleRejects_ReturnsTheModuleCode(FailureKind kind, string code, HttpStatusCode expected)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.People.OnCreate = (_, _) => new Failure(kind, code, "身分模組說的原因。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);
        var body = new { displayName = "New Example", email = "new@example.com", password = "short", role = "member" };

        // Act
        using var response = await client.PostAsync("/v1/people", ApiRequests.Json(body));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, expected, code);
    }
}
