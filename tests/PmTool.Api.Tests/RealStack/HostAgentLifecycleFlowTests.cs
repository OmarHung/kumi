using System.Net;
using PmTool.Ai;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 主機代理的生命週期：訂閱帳號的登入被拒絕、機器憑證換掉後舊的失效、停用與登出之後不派工、移除後 worker 用舊憑證是 401。
/// 每個測試用自己的代理，互不影響。
/// </summary>
[TestFixture]
public sealed class HostAgentLifecycleFlowTests : HostAgentFlowBase
{
    [OneTimeSetUp]
    public Task SetUpAsync() => SetUpHostAsync();

    [Test]
    public async Task Login_SubscriptionBilling_IsRejectedAndTheEngineCannotBecomeASource()
    {
        // Arrange
        var pairing = await PostOwnerAsync($"{Root}/agents", new { name = "訂閱測試機" }, HttpStatusCode.Created);
        var agentId = pairing["agent"]!["id"]!.GetValue<Guid>();
        using var anonymous = Factory.CreateClient();
        using var paired = await anonymous.PostAsync(
            "/v1/host-agent/pair",
            ApiRequests.Json(new { code = pairing["pairingCode"]!.GetValue<string>(), machineKey = "machine-0002", machineName = "host-02", agentVersion = "0.9.0" }));
        var credential = (await ApiRequests.AssertSuccessEnvelopeAsync(paired, HttpStatusCode.OK))!["credential"]!.GetValue<string>();
        using var worker = Factory.CreateClient();
        worker.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {credential}");
        var start = await PostOwnerAsync($"{Root}/agents/{agentId}/engines/claude_code/login", null, HttpStatusCode.OK);
        using var claim = await worker.PostAsync("/v1/host-agent/logins/claim", ApiRequests.Json(new { code = start["code"]!.GetValue<string>() }));
        var loginId = (await ApiRequests.AssertSuccessEnvelopeAsync(claim, HttpStatusCode.OK))!["loginId"]!.GetValue<Guid>();

        // Act
        using var complete = await worker.PostAsync(
            $"/v1/host-agent/logins/{loginId}/complete", ApiRequests.Json(new { loggedIn = true, billing = "subscription" }));
        using var replay = await worker.PostAsync(
            $"/v1/host-agent/logins/{loginId}/complete", ApiRequests.Json(new { loggedIn = true, billing = "api" }));
        using var source = await Owner.Client.PostAsync(
            $"{Root}/sources",
            ApiRequests.Json(new
            {
                agentId,
                engine = "claude_code",
                name = "訂閱的來源",
                enabled = true,
                enabledModels = new[] { "default" },
                defaultModel = (string?)null,
                defaultEffort = (string?)null,
                limits = new { dailyRequestsPerPerson = (int?)null, monthlyRequests = (int?)null, monthlyTokens = (long?)null },
                acknowledgeApiBilling = true,
            }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(complete, HttpStatusCode.UnprocessableEntity, AiErrorCodes.HostLoginRejected);
        await ApiRequests.AssertErrorEnvelopeAsync(replay, HttpStatusCode.UnprocessableEntity, AiErrorCodes.HostLoginRejected);
        Assert.That(source.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        await RemoveAgentAsync(agentId);
    }

    [Test]
    public async Task Pairing_ACodeCanOnlyBeUsedOnce_AndTheOldCredentialStopsWorkingAfterARePair()
    {
        // Arrange
        var created = await PostOwnerAsync($"{Root}/agents", new { name = "重新配對測試機" }, HttpStatusCode.Created);
        var agentId = created["agent"]!["id"]!.GetValue<Guid>();
        var code = created["pairingCode"]!.GetValue<string>();
        using var anonymous = Factory.CreateClient();
        var body = ApiRequests.Json(new { code, machineKey = "machine-0003", machineName = "host-03", agentVersion = "0.9.0" });
        using var first = await anonymous.PostAsync("/v1/host-agent/pair", body);
        var oldCredential = (await ApiRequests.AssertSuccessEnvelopeAsync(first, HttpStatusCode.OK))!["credential"]!.GetValue<string>();

        // Act
        using var replay = await anonymous.PostAsync(
            "/v1/host-agent/pair", ApiRequests.Json(new { code, machineKey = "machine-0003", machineName = "host-03", agentVersion = "0.9.0" }));
        var repair = await PostOwnerAsync($"{Root}/agents/{agentId}/pairing", null, HttpStatusCode.OK);
        using var oldWorker = Factory.CreateClient();
        oldWorker.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {oldCredential}");
        using var oldCheckIn = await oldWorker.PostAsync("/v1/host-agent/check-in", ApiRequests.Json(CheckInBody(capacity: 0)));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(replay, HttpStatusCode.UnprocessableEntity, AiErrorCodes.HostPairingInvalid);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(repair["pairingCode"]!.GetValue<string>(), Is.Not.EqualTo(code));
            Assert.That(oldCheckIn.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        await RemoveAgentAsync(agentId);
    }

    [Test]
    public async Task Pairing_ByAnOwnerWhoIsNoLongerTheOwner_IsRefused()
    {
        // Arrange：另一位擁有者開始配對之後，被降成管理員。
        var promoted = await AddPersonAsync("owner", "Second Owner", projectRole: "editor");
        using var second = promoted;
        var created = await PostAsAsync(second, $"{Root}/agents", new { name = "降權測試機" });
        using var demote = await Owner.Client.PutAsync($"/v1/people/{second.PersonId}/role", ApiRequests.Json(new { role = "admin" }));
        await ApiRequests.AssertSuccessEnvelopeAsync(demote, HttpStatusCode.OK);
        using var anonymous = Factory.CreateClient();

        // Act
        using var response = await anonymous.PostAsync(
            "/v1/host-agent/pair",
            ApiRequests.Json(new { code = created["pairingCode"]!.GetValue<string>(), machineKey = "machine-0004", machineName = "host-04", agentVersion = "0.9.0" }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, AiErrorCodes.HostPairingInvalid);
        await RemoveAgentAsync(created["agent"]!["id"]!.GetValue<Guid>());
    }

    [Test]
    public async Task DisablingTheSource_StopsNewMessages_WithASentenceForMembers()
    {
        // Arrange：對話先建好；來源關掉之後才送。
        var route = await NewConversationRouteAsync(Member);
        using var disabled = await Owner.Client.PutAsync($"{Root}/sources/{SourceId}", ApiRequests.Json(SourceBody(enabled: false)));
        await ApiRequests.AssertSuccessEnvelopeAsync(disabled, HttpStatusCode.OK);

        // Act
        using var response = await SendAsync(Member, conversationRoute: route);

        // Assert
        try
        {
            await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, AiErrorCodes.HostSourceUnavailable);
        }
        finally
        {
            using var enabled = await Owner.Client.PutAsync($"{Root}/sources/{SourceId}", ApiRequests.Json(SourceBody()));
            await ApiRequests.AssertSuccessEnvelopeAsync(enabled, HttpStatusCode.OK);
        }
    }

    private static async Task<System.Text.Json.Nodes.JsonNode> PostAsAsync(SignedInClient person, string route, object body)
    {
        using var response = await person.Client.PostAsync(route, ApiRequests.Json(body));
        return await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created) ?? throw new AssertionException("沒有 data");
    }
}
