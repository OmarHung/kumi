using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>管理別人的帳號：停用、重新啟用、重設密碼。誰可以做由身分模組判斷，這裡看 API 怎麼傳、怎麼回。</summary>
[TestFixture]
public sealed class PersonAccountEndpointTests
{
    private const string ResetTo = "reset-password-0000";

    private static readonly string MemberRoute = $"/v1/people/{TestUsers.Member.PersonId}";

    [TestCase("disable", true)]
    [TestCase("enable", false)]
    public async Task SetDisabled_Accepted_ReturnsThePersonWithTheContractShape(string action, bool disabled)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync($"{MemberRoute}/{action}", null);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(JsonNode.DeepEquals(data, ExpectedMember(disabled)), Is.True, data?.ToJsonString());
        Assert.That(factory.Modules.People.SetDisabledCalls, Is.EqualTo(new[]
        {
            new SetDisabledCall(TestUsers.Owner.ToActor(), TestUsers.Member.PersonId, disabled),
        }));
    }

    [Test]
    public async Task ResetPassword_Accepted_ReturnsThePersonAndPassesTheNewPassword()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync($"{MemberRoute}/password", ApiRequests.Json(new { newPassword = ResetTo }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(JsonNode.DeepEquals(data, ExpectedMember(disabled: false)), Is.True, data?.ToJsonString());
        Assert.That(factory.Modules.People.ResetPasswordCalls, Is.EqualTo(new[]
        {
            new ResetPasswordCall(TestUsers.Owner.ToActor(), TestUsers.Member.PersonId, ResetTo),
        }));
    }

    [Test]
    public async Task SetRole_Accepted_ReturnsThePersonWithTheNewRoleAndPassesItToTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync($"{MemberRoute}/role", ApiRequests.Json(new { role = "guest" }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expected = ExpectedMember(disabled: false);
        expected["role"] = "guest";
        Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
        Assert.That(factory.Modules.People.SetRoleCalls, Is.EqualTo(new[]
        {
            new SetRoleCall(TestUsers.Owner.ToActor(), TestUsers.Member.PersonId, OrganizationRole.Guest),
        }));
    }

    [TestCaseSource(nameof(RoleFailures))]
    public async Task SetRole_ModuleRejects_ReturnsTheModuleCode(FailureKind kind, string code, HttpStatusCode expected)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.People.OnSetRole = _ => new Failure(kind, code, "身分模組說的原因。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PutAsync($"{MemberRoute}/role", ApiRequests.Json(new { role = "admin" }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, expected, code);
    }

    [Test]
    public async Task SetRole_PersonIdIsNotAGuid_Returns404PersonNotFoundWithoutCallingTheModule()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync("/v1/people/not-a-guid/role", ApiRequests.Json(new { role = "member" }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, ApiErrorCodes.PersonNotFound);
        Assert.That(factory.Modules.People.SetRoleCalls, Is.Empty);
    }

    [TestCase("""{"role":"superuser"}""", TestName = "SetRole_UnknownRole_IsRejectedWithoutCallingTheModule")]
    [TestCase("{}", TestName = "SetRole_RoleMissing_IsRejectedWithoutCallingTheModule")]
    [TestCase("""{"role":1}""", TestName = "SetRole_RoleIsANumber_IsRejectedWithoutCallingTheModule")]
    public async Task SetRole_BadBody(string body)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync($"{MemberRoute}/role", ApiRequests.Document(body));

        // Assert
        Assert.That((int)response.StatusCode, Is.AnyOf(400, 422));
        Assert.That(factory.Modules.People.SetRoleCalls, Is.Empty);
    }

    [TestCaseSource(nameof(ModuleFailures))]
    public async Task Operation_ModuleRejects_ReturnsTheModuleCode(string operation, FailureKind kind, string code, HttpStatusCode expected)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var failure = new Failure(kind, code, "身分模組說的原因。");
        factory.Modules.People.OnSetDisabled = _ => failure;
        factory.Modules.People.OnResetPassword = _ => failure;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await SendAsync(client, operation, MemberRoute);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, expected, code);
    }

    [Test]
    public async Task ResetPassword_PasswordTooShort_Returns422WithTheFieldPath()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.People.OnResetPassword = _ => new Failure(FailureKind.Invalid, IdentityErrorCodes.PasswordTooShort, "太短。")
        {
            Details = [new FailureDetail(IdentityErrorCodes.PasswordTooShort, "newPassword", "太短。")],
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync($"{MemberRoute}/password", ApiRequests.Json(new { newPassword = "short" }));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, IdentityErrorCodes.PasswordTooShort);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{IdentityErrorCodes.PasswordTooShort}|newPassword" }));
    }

    [TestCase("disable")]
    [TestCase("enable")]
    [TestCase("password")]
    public async Task Operation_PersonIdIsNotAGuid_Returns404PersonNotFoundWithoutCallingTheModule(string operation)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await SendAsync(client, operation, "/v1/people/not-a-guid");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, ApiErrorCodes.PersonNotFound);
        Assert.That(factory.Modules.People.SetDisabledCalls, Is.Empty);
        Assert.That(factory.Modules.People.ResetPasswordCalls, Is.Empty);
    }

    [TestCase("{}", "", TestName = "ResetPassword_NewPasswordMissing_Returns400")]
    [TestCase("""{"password":"long-enough-password"}""", "password", TestName = "ResetPassword_WrongPropertyName_Returns400")]
    public async Task ResetPassword_BadBody(string body, string path)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync($"{MemberRoute}/password", ApiRequests.Document(body));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);

        // Assert
        Assert.That(details.Select(d => d?["path"]?.GetValue<string>()), Is.EqualTo(new[] { path }));
        Assert.That(factory.Modules.People.ResetPasswordCalls, Is.Empty);
    }

    [Test]
    public async Task ListPeople_DisabledPerson_IsListedWithDisabledTrue()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.People.Add(TestUsers.OrganizationId, TestUsers.ToPerson(TestUsers.Member) with { IsDisabled = true });
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync("/v1/people");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var disabled = data?.AsArray().Where(p => p?["disabled"]?.GetValue<bool>() == true).Select(p => p?["personId"]?.GetValue<Guid>());
        Assert.That(disabled, Is.EqualTo(new Guid?[] { TestUsers.Member.PersonId }));
    }

    private static JsonObject ExpectedMember(bool disabled) => new()
    {
        ["personId"] = TestUsers.Member.PersonId,
        ["displayName"] = TestUsers.Member.DisplayName,
        ["email"] = TestUsers.Member.Email,
        ["role"] = "member",
        ["disabled"] = disabled,
    };

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string operation, string personRoute) =>
        operation == "password"
            ? client.PutAsync($"{personRoute}/password", ApiRequests.Json(new { newPassword = ResetTo }))
            : client.PostAsync($"{personRoute}/{operation}", null);

    private static IEnumerable<TestCaseData> RoleFailures() =>
    [
        new(FailureKind.Forbidden, IdentityErrorCodes.Forbidden, HttpStatusCode.Forbidden),
        new(FailureKind.NotFound, IdentityErrorCodes.NotFound, HttpStatusCode.NotFound),
        new(FailureKind.Conflict, IdentityErrorCodes.IsSelf, HttpStatusCode.Conflict),
    ];

    private static IEnumerable<TestCaseData> ModuleFailures()
    {
        (FailureKind Kind, string Code, HttpStatusCode Status)[] failures =
        [
            (FailureKind.Forbidden, IdentityErrorCodes.Forbidden, HttpStatusCode.Forbidden),
            (FailureKind.NotFound, IdentityErrorCodes.NotFound, HttpStatusCode.NotFound),
            (FailureKind.Conflict, IdentityErrorCodes.IsSelf, HttpStatusCode.Conflict),
            (FailureKind.Conflict, IdentityErrorCodes.NoAccount, HttpStatusCode.Conflict),
        ];
        foreach (var operation in new[] { "disable", "enable", "password" })
        {
            foreach (var (kind, code, status) in failures)
            {
                yield return new TestCaseData(operation, kind, code, status).SetArgDisplayNames(operation, code);
            }
        }
    }
}
