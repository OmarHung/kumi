using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Notifications;

namespace PmTool.Api.Tests;

/// <summary>/v1/email-settings 與 /v1/account/email-notifications：權限、形狀、密碼、細項、測試信。</summary>
[TestFixture]
public sealed class EmailSettingsEndpointTests
{
    private const string Route = "/v1/email-settings";
    private const string TestRoute = "/v1/email-settings/test";
    private const string PreferenceRoute = "/v1/account/email-notifications";

    private static object GmailBody(string? password = "app-password-123") => new
    {
        enabled = true,
        provider = "gmail",
        username = "team@example.com",
        password,
        fromName = "Example 公司",
        siteUrl = "https://pm.example.com",
    };

    private static Failure Forbidden() =>
        new(FailureKind.Forbidden, NotificationErrorCodes.EmailForbidden, "只有組織的擁有者或管理員能看、能改寄信設定。");

    [Test]
    public async Task Get_Admin_ReturnsTheContractShapeWithTheUpdatersName()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync(Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var expected = JsonNode.Parse($$$"""
            {"enabled":true,"provider":"gmail","host":null,"port":null,"security":null,"username":"team@example.com","hasPassword":true,
             "fromAddress":null,"fromName":"Example 公司","siteUrl":"https://pm.example.com",
             "status":{"lastSentAt":"2026-11-21T06:05:00Z","lastFailedAt":null,"lastError":null},
             "updatedAt":"2026-11-21T06:00:00Z","updatedBy":{"personId":"{{{TestUsers.Owner.PersonId}}}","displayName":"{{{TestUsers.Owner.DisplayName}}}"}}
            """);
        Assert.That(JsonNode.DeepEquals(data, expected), Is.True, data?.ToJsonString());
    }

    [Test]
    public async Task Get_NeverSaved_HasNullTimestampsAndUpdater()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.EmailSettings.OnGet = _ => new EmailSettings(
            false, EmailProvider.Gmail, null, null, null, null, false, null, null, null, new EmailDeliveryStatus(null, null, null), null, null);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync(Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["updatedAt"], Is.Null);
            Assert.That(data?["updatedBy"], Is.Null);
            Assert.That(data?["enabled"]?.GetValue<bool>(), Is.False);
            Assert.That(data?["hasPassword"]?.GetValue<bool>(), Is.False);
        }
    }

    [Test]
    public async Task Get_UpdaterNoLongerInTheOrganization_ShowsThePlaceholderName()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var gone = Guid.CreateVersion7();
        factory.Modules.EmailSettings.OnGet = _ => FakeEmailSettingsService.Stored(
            new EmailSettingsInput(true, EmailProvider.Gmail, null, null, null, "team@example.com", "x", null, null, "https://pm.example.com"), gone);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync(Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?["updatedBy"]?["displayName"]?.GetValue<string>(), Is.EqualTo("（已不在組織）"));
    }

    [Test]
    public async Task Get_Response_NeverContainsAPasswordOrItsProperty()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync(Route);
        var text = await response.Content.ReadAsStringAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(text, Does.Not.Contain("stored-secret"));
            Assert.That(text.ToLowerInvariant(), Does.Not.Contain("\"password\""));
            Assert.That(text, Does.Contain("\"hasPassword\":true"));
        }
    }

    [TestCase(nameof(TestUsers.Owner), true)]
    [TestCase(nameof(TestUsers.Member), false)]
    [TestCase(nameof(TestUsers.Guest), false)]
    public async Task Get_PassesWhetherTheSessionIsAnOrganizationAdminToTheModule(string who, bool isAdmin)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var user = who switch
        {
            nameof(TestUsers.Owner) => TestUsers.Owner,
            nameof(TestUsers.Member) => TestUsers.Member,
            _ => TestUsers.Guest,
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, user);

        // Act
        using var response = await client.GetAsync(Route);

        // Assert
        var actor = factory.Modules.EmailSettings.GetCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(actor.IsOrganizationAdmin, Is.EqualTo(isAdmin));
            Assert.That(actor.PersonId, Is.EqualTo(user.PersonId));
            Assert.That(actor.OrganizationId, Is.EqualTo(user.OrganizationId));
        }
    }

    [TestCase("GET")]
    [TestCase("PUT")]
    [TestCase("POST")]
    public async Task Settings_ModuleSaysForbidden_Returns403WithTheEmailCode(string method)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.EmailSettings.OnGet = _ => Forbidden();
        factory.Modules.EmailSettings.OnSave = (_, _) => Forbidden();
        factory.Modules.EmailSettings.OnSendTest = (_, _, _) => Forbidden();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);
        using var request = new HttpRequestMessage(
            new HttpMethod(method), method == "POST" ? TestRoute : Route)
        {
            Content = method == "GET" ? null : ApiRequests.Json(GmailBody()),
        };

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, NotificationErrorCodes.EmailForbidden);
    }

    [Test]
    public async Task Put_Gmail_PassesTheInputToTheModuleAndReturnsTheSavedSettingsWithoutThePassword()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync(Route, ApiRequests.Json(GmailBody()));
        var text = await response.Content.ReadAsStringAsync();

        // Assert
        var call = factory.Modules.EmailSettings.SaveCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(call.Input, Is.EqualTo(new EmailSettingsInput(
                true, EmailProvider.Gmail, null, null, null, "team@example.com", "app-password-123", null, "Example 公司", "https://pm.example.com")));
            Assert.That(text, Does.Not.Contain("app-password-123"));
            Assert.That(text, Does.Contain("\"hasPassword\":true"));
        }
    }

    [Test]
    public async Task Put_SmtpWithSnakeCaseEnums_ParsesProviderAndSecurity()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        var body = new
        {
            enabled = true,
            provider = "smtp",
            host = "smtp.example.com",
            port = 587,
            security = "start_tls",
            username = "mailer",
            password = "secret",
            fromAddress = "noreply@example.com",
            fromName = (string?)null,
            siteUrl = "https://pm.example.com",
        };

        // Act
        using var response = await client.PutAsync(Route, ApiRequests.Json(body));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var input = factory.Modules.EmailSettings.SaveCalls.Single().Input;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(input.Provider, Is.EqualTo(EmailProvider.Smtp));
            Assert.That(input.Security, Is.EqualTo(SmtpSecurity.StartTls));
            Assert.That(input.Port, Is.EqualTo(587));
            Assert.That(data?["provider"]?.GetValue<string>(), Is.EqualTo("smtp"));
            Assert.That(data?["security"]?.GetValue<string>(), Is.EqualTo("start_tls"));
        }
    }

    [TestCase("start_tls", SmtpSecurity.StartTls)]
    [TestCase("tls", SmtpSecurity.Tls)]
    [TestCase("none", SmtpSecurity.None)]
    public async Task Put_EachSecurityName_IsRecognized(string name, SmtpSecurity expected)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync(Route, ApiRequests.Document($$"""{"enabled":false,"provider":"smtp","security":"{{name}}"}"""));

        // Assert
        Assert.That(factory.Modules.EmailSettings.SaveCalls.Single().Input.Security, Is.EqualTo(expected));
    }

    [TestCase("""{"enabled":true,"provider":"gmail"}""", null, TestName = "Put_PasswordOmitted_MeansKeepTheStoredOne")]
    [TestCase("""{"enabled":true,"provider":"gmail","password":null}""", null, TestName = "Put_PasswordNull_MeansKeepTheStoredOne")]
    [TestCase("""{"enabled":true,"provider":"gmail","password":""}""", "", TestName = "Put_PasswordEmpty_MeansClearIt")]
    [TestCase("""{"enabled":true,"provider":"gmail","password":"new-one"}""", "new-one", TestName = "Put_PasswordGiven_MeansReplaceIt")]
    public async Task Put_Password_IsPassedOnExactlyAsSent(string body, string? expected)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync(Route, ApiRequests.Document(body));

        // Assert
        Assert.That(factory.Modules.EmailSettings.SaveCalls.Single().Input.Password, Is.EqualTo(expected));
    }

    [Test]
    public async Task Put_ModuleRejectsFields_Returns422WithTheDetailPaths()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.EmailSettings.OnSave = (_, _) => new Failure(
            FailureKind.Invalid, NotificationErrorCodes.EmailSettingsInvalid, "寄信設定有幾個欄位不對。")
        {
            Details =
            [
                new FailureDetail(NotificationErrorCodes.EmailPortNotAllowed, "port", "連接埠只能是 25、465、587、2525。"),
                new FailureDetail(NotificationErrorCodes.EmailInsecureCredentials, "security", "不加密時不能設帳號或密碼。"),
                new FailureDetail(NotificationErrorCodes.EmailFieldRequired, "siteUrl", "要填網站網址。"),
            ],
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync(Route, ApiRequests.Json(GmailBody()));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(
            response, HttpStatusCode.UnprocessableEntity, NotificationErrorCodes.EmailSettingsInvalid);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[]
        {
            "email.port_not_allowed|port",
            "email.insecure_credentials|security",
            "email.field_required|siteUrl",
        }));
    }

    [TestCase("""{"provider":"gmail"}""", "", TestName = "Put_EnabledMissing_Returns400")]
    [TestCase("""{"enabled":true}""", "", TestName = "Put_ProviderMissing_Returns400")]
    [TestCase("""{"enabled":true,"provider":"outlook"}""", "provider", TestName = "Put_UnknownProvider_Returns400")]
    [TestCase("""{"enabled":true,"provider":"smtp","security":"STARTTLS"}""", "security", TestName = "Put_SecurityInTheWrongSpelling_Returns400")]
    [TestCase("""{"enabled":true,"provider":"smtp","port":"587"}""", "port", TestName = "Put_PortAsAString_Returns400")]
    [TestCase("""{"enabled":true,"provider":"gmail","extra":1}""", "extra", TestName = "Put_UnknownProperty_Returns400")]
    [TestCase("""{"enabled":"yes","provider":"gmail"}""", "enabled", TestName = "Put_EnabledNotABoolean_Returns400")]
    public async Task Put_BadBody_Returns400WithoutCallingTheModule(string body, string path)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PutAsync(Route, ApiRequests.Document(body));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(details, Has.Count.EqualTo(1));
            Assert.That(details[0]?["path"]?.GetValue<string>(), Is.EqualTo(path).Or.Contain(path));
            Assert.That(factory.Modules.EmailSettings.SaveCalls, Is.Empty);
        }
    }

    [Test]
    public async Task Put_WithoutTheCsrfHeader_IsRejected()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        client.DefaultRequestHeaders.Remove(ApiRequests.CsrfHeader);

        // Act
        using var response = await client.PutAsync(Route, ApiRequests.Json(GmailBody()));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(factory.Modules.EmailSettings.SaveCalls, Is.Empty);
        }
    }

    [Test]
    public async Task Test_Success_SendsToTheSignedInPersonsOwnEmailAndSaysSo()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(TestRoute, ApiRequests.Json(GmailBody()));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var call = factory.Modules.EmailSettings.TestCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data, new JsonObject { ["sentTo"] = TestUsers.Owner.Email }), Is.True, data?.ToJsonString());
            Assert.That(call.Recipient, Is.EqualTo(TestUsers.Owner.Email));
            Assert.That(call.Input.Password, Is.EqualTo("app-password-123"));
            Assert.That(factory.Modules.EmailSettings.SaveCalls, Is.Empty, "測試信不存設定");
        }
    }

    [Test]
    public async Task Test_BodyWithARecipient_IsRejectedSoNobodyElseCanBeMailed()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(
            TestRoute, ApiRequests.Document("""{"enabled":true,"provider":"gmail","to":"victim@example.com"}"""));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(factory.Modules.EmailSettings.TestCalls, Is.Empty);
        }
    }

    [Test]
    public async Task Test_SendFails_Returns422WithTheReason()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.EmailSettings.OnSendTest = (_, _, _) => new Failure(
            FailureKind.Invalid, NotificationErrorCodes.EmailSendFailed, "連不上 smtp.example.com:587");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(TestRoute, ApiRequests.Json(GmailBody()));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, NotificationErrorCodes.EmailSendFailed);
        Assert.That((await ApiRequests.ReadEnvelopeAsync(response))["error"]?["message"]?.GetValue<string>(), Is.EqualTo("連不上 smtp.example.com:587"));
    }

    [Test]
    public async Task Test_FieldsInvalid_Returns422WithTheDetails()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.EmailSettings.OnSendTest = (_, _, _) => new Failure(
            FailureKind.Invalid, NotificationErrorCodes.EmailSettingsInvalid, "寄信設定有幾個欄位不對。")
        {
            Details = [new FailureDetail(NotificationErrorCodes.EmailFieldRequired, "password", "要填密碼。")],
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(TestRoute, ApiRequests.Json(GmailBody()));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(
            response, HttpStatusCode.UnprocessableEntity, NotificationErrorCodes.EmailSettingsInvalid);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { "email.field_required|password" }));
    }

    [Test]
    public async Task Test_MoreThanFivePerMinute_Returns429ForThatPersonOnly()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var owner = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        using var other = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < ApiLimits.EmailTestPermitsPerWindow; i++)
        {
            using var allowed = await owner.PostAsync(TestRoute, ApiRequests.Json(GmailBody()));
            statuses.Add(allowed.StatusCode);
        }

        // Act
        using var tooMany = await owner.PostAsync(TestRoute, ApiRequests.Json(GmailBody()));
        using var otherPerson = await other.PostAsync(TestRoute, ApiRequests.Json(GmailBody()));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(tooMany, HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(statuses, Is.All.EqualTo(HttpStatusCode.OK));
            Assert.That(otherPerson.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(factory.Modules.EmailSettings.TestCalls, Has.Count.EqualTo(ApiLimits.EmailTestPermitsPerWindow + 1));
        }
    }

    [Test]
    public async Task Test_LimitOnlyAppliesToTestMails_NotToSavingSettings()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        for (var i = 0; i <= ApiLimits.EmailTestPermitsPerWindow; i++)
        {
            using var test = await client.PostAsync(TestRoute, ApiRequests.Json(GmailBody()));
        }

        // Act
        using var save = await client.PutAsync(Route, ApiRequests.Json(GmailBody()));

        // Assert
        Assert.That(save.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Preference_Get_ReturnsTheTwoFlags()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.EmailPreferences.OnGet = _ => new EmailPreference(false, true);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Guest);

        // Act
        using var response = await client.GetAsync(PreferenceRoute);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse("""{"enabled":false,"organizationEnabled":true}""")), Is.True, data?.ToJsonString());
    }

    [Test]
    public async Task Preference_Put_SavesForTheSignedInPersonAndReturnsTheNewState()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PutAsync(PreferenceRoute, ApiRequests.Json(new { enabled = false }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        var call = factory.Modules.EmailPreferences.SetCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(call.Actor.PersonId, Is.EqualTo(TestUsers.Member.PersonId));
            Assert.That(call.Enabled, Is.False);
            Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse("""{"enabled":false,"organizationEnabled":true}""")), Is.True, data?.ToJsonString());
        }
    }

    [TestCase("{}", TestName = "Preference_Put_EnabledMissing_Returns400")]
    [TestCase("""{"enabled":"no"}""", TestName = "Preference_Put_EnabledNotABoolean_Returns400")]
    [TestCase("""{"enabled":true,"extra":1}""", TestName = "Preference_Put_UnknownProperty_Returns400")]
    public async Task Preference_Put_BadBody_Returns400WithoutSaving(string body)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PutAsync(PreferenceRoute, ApiRequests.Document(body));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(factory.Modules.EmailPreferences.SetCalls, Is.Empty);
        }
    }

    [Test]
    public async Task Put_InputHandedToTheModule_NeverPrintsThePassword()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);
        using var response = await client.PutAsync(Route, ApiRequests.Json(GmailBody("very-secret-value")));

        // Act
        var text = factory.Modules.EmailSettings.SaveCalls.Single().Input.ToString();

        // Assert
        Assert.That(text, Does.Not.Contain("very-secret-value").And.Contain("***"));
    }
}
