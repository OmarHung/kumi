using System.Net;
using PmTool.Ai;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 主機代理的權限，用真的身分與 AI 模組：只有擁有者能管理（管理員、一般成員、訪客、執行者都 403）、
/// 所有有效成員（擁有者、管理員、一般成員，新加入的也是）都能在選單看到主機來源、訪客看不到；
/// 機器憑證與個人存取權杖都不能當成人用。
/// </summary>
[TestFixture]
public sealed class HostAgentAccessFlowTests : HostAgentFlowBase
{
    [OneTimeSetUp]
    public Task SetUpAsync() => SetUpHostAsync();

    [Test]
    public async Task Overview_Owner_SeesTheAgentTheReadyEngineAndTheEnabledSource()
    {
        // Act
        var overview = await RealStackSteps.GetAsync(Owner.Client, Root);

        // Assert
        var agent = overview["agents"]!.AsArray().Single(a => a!["id"]!.GetValue<Guid>() == AgentId)!;
        var claude = agent["engines"]!.AsArray().Single(e => e!["kind"]!.GetValue<string>() == "claude_code")!;
        var source = overview["sources"]!.AsArray().Single(s => s!["id"]!.GetValue<Guid>() == SourceId)!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(agent["state"]!.GetValue<string>(), Is.EqualTo("active"));
            Assert.That(agent["online"]!.GetValue<bool>(), Is.True);
            Assert.That(agent["machineName"]!.GetValue<string>(), Is.EqualTo("host-01"));
            Assert.That(claude["readiness"]!.GetValue<string>(), Is.EqualTo("ready"));
            Assert.That(claude["billing"]!.GetValue<string>(), Is.EqualTo("api"));
            Assert.That(claude["authorizedBy"]!["displayName"]!.GetValue<string>(), Is.EqualTo(RealStackFactory.OwnerName));
            Assert.That(source["status"]!.GetValue<string>(), Is.EqualTo("ready"));
            Assert.That(source["billingAcknowledgedAt"], Is.Not.Null);
        }
    }

    [Test]
    public async Task EveryManagementEndpoint_AdminMemberContributorAndGuest_Is403OwnerOnly()
    {
        // Arrange
        (string Method, string Route, object? Body)[] requests =
        [
            ("GET", Root, null),
            ("POST", $"{Root}/agents", new { name = "另一台" }),
            ("PATCH", $"{Root}/agents/{AgentId}", new { name = "x", enabled = false }),
            ("POST", $"{Root}/agents/{AgentId}/pairing", null),
            ("DELETE", $"{Root}/agents/{AgentId}", null),
            ("POST", $"{Root}/agents/{AgentId}/engines/claude_code/login", null),
            ("POST", $"{Root}/agents/{AgentId}/engines/claude_code/logout", null),
            ("DELETE", $"{Root}/logins/{Guid.NewGuid()}", null),
            ("POST", $"{Root}/sources", SourceBody(enabled: false)),
            ("PUT", $"{Root}/sources/{SourceId}", SourceBody()),
            ("DELETE", $"{Root}/sources/{SourceId}", null),
            ("GET", $"{Root}/sources/{SourceId}/usage", null),
        ];

        foreach (var person in new[] { Admin, Member, Contributor, Guest })
        {
            foreach (var (method, route, body) in requests)
            {
                // Act
                using var request = new HttpRequestMessage(new HttpMethod(method), route);
                if (body is not null)
                {
                    request.Content = ApiRequests.Json(body);
                }

                using var response = await person.Client.SendAsync(request);

                // Assert
                await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, AiErrorCodes.HostOwnerOnly);
            }
        }

        var after = await RealStackSteps.GetAsync(Owner.Client, Root);
        Assert.That(after["sources"]!.AsArray().Select(s => s!["id"]!.GetValue<Guid>()), Does.Contain(SourceId), "403 的請求什麼都沒改");
    }

    [Test]
    public async Task Options_OwnerAdminMemberAndContributor_SeeTheHostedSource_GuestDoesNot()
    {
        // Act / Assert
        foreach (var person in new[] { Owner, Admin, Member, Contributor })
        {
            var options = await RealStackSteps.GetAsync(person.Client, "/v1/ai/options");
            var hosted = options["connections"]!.AsArray().Where(c => c!["hosted"]!.GetValue<bool>()).ToList();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(options["available"]!.GetValue<bool>(), Is.True);
                Assert.That(hosted, Has.Count.EqualTo(1));
                Assert.That(hosted[0]!["id"]!.GetValue<Guid>(), Is.EqualTo(SourceId));
                Assert.That(hosted[0]!["kind"]!.GetValue<string>(), Is.EqualTo("claude_code"));
                Assert.That(hosted[0]!["personal"]!.GetValue<bool>(), Is.False);
                Assert.That(hosted[0]!["notice"], Is.Null);
                Assert.That(hosted[0]!["models"]!.AsArray().Select(m => m!["id"]!.GetValue<string>()), Is.EqualTo(new[] { "default" }));
            }
        }

        var guestOptions = await RealStackSteps.GetAsync(Guest.Client, "/v1/ai/options");
        Assert.That(guestOptions["connections"]!.AsArray().Where(c => c!["hosted"]!.GetValue<bool>()), Is.Empty);
    }

    [Test]
    public async Task Options_AMemberAddedLater_SeesTheHostedSourceWithoutAnyOtherSetting()
    {
        // Arrange
        using var latecomer = await AddMemberAsync(Owner, Project, "editor", "Latecomer Example", RealStackSteps.UniqueEmail("host-latecomer"));

        // Act
        var options = await RealStackSteps.GetAsync(latecomer.Client, "/v1/ai/options");

        // Assert
        Assert.That(options["connections"]!.AsArray().Select(c => c!["id"]!.GetValue<Guid>()), Does.Contain(SourceId));
    }

    [Test]
    public async Task MachineCredentialAndToolGrant_CannotBeUsedAsAPerson()
    {
        // Arrange：機器憑證是真的（配對拿到的），打管理端點與一般端點都不行。
        using var client = Factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {Credential}");

        // Act
        using var overview = await client.GetAsync(Root);
        using var projects = await client.GetAsync("/v1/projects");
        using var session = await client.GetAsync("/v1/session");

        // Assert
        foreach (var response in new[] { overview, projects, session })
        {
            await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
        }
    }

    [Test]
    public async Task ManagementEndpoints_APersonalAccessTokenOfTheOwner_Is403()
    {
        // Arrange
        using var created = await Owner.Client.PostAsync(
            "/v1/account/tokens", ApiRequests.Json(new { name = "e2e", scopes = new[] { "items:read" }, expiresInDays = (int?)null }));
        var token = await ApiRequests.AssertSuccessEnvelopeAsync(created, HttpStatusCode.Created);
        using var client = Factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {token!["secret"]!.GetValue<string>()}");

        // Act
        using var response = await client.GetAsync(Root);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.InsufficientScope);
    }

    [Test]
    public async Task ApiBillingNotAcknowledged_CannotEnableASource()
    {
        // Arrange
        var body = new
        {
            agentId = AgentId,
            engine = "codex",
            name = "主機上的 Codex",
            enabled = true,
            enabledModels = Array.Empty<string>(),
            defaultModel = (string?)null,
            defaultEffort = (string?)null,
            limits = new { dailyRequestsPerPerson = (int?)null, monthlyRequests = (int?)null, monthlyTokens = (long?)null },
            acknowledgeApiBilling = false,
        };

        // Act
        using var response = await Owner.Client.PostAsync($"{Root}/sources", ApiRequests.Json(body));

        // Assert：沒裝、沒登入的引擎不能開，沒勾計費確認也不行（哪一項先擋不重要）。
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
    }
}
