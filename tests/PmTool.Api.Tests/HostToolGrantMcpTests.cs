using System.Net;
using System.Text.Json;
using ModelContextProtocol.Client;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Mcp;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>
/// 主機代理每件工作的工具授權（kjt_）打 /mcp：只列授權的工具、身分是發問的人、專案由工作決定、
/// 提出類的工具同樣的參數只建立一次、授權失效就是 401、失敗算進 BearerFailureLimiter；
/// 工具授權不能變成任何人的登入，打其他端點一律 401。
/// </summary>
[TestFixture]
public sealed class HostToolGrantMcpTests
{
    private static ApiFactory NewFactory() => new(FakeBlueprintValidator.Passing());

    private static Task<McpClient> ConnectAsync(ApiFactory factory, string? project = null) =>
        McpTesting.ConnectAsync(factory, FakeHostToolGrants.Token, project);

    private static HostToolGrant GrantWith(params string[] tools) =>
        new(TestUsers.OrganizationId, TestUsers.Member.PersonId, false, "WEB", tools, FakeHostAgentWorkerService.JobId);

    [Test]
    public async Task ListTools_OnlyTheGrantedOnes()
    {
        // Arrange
        using var factory = NewFactory();
        factory.Modules.HostGrants.Grant = GrantWith("get_item", "propose_item_changes");
        await using var client = await ConnectAsync(factory);

        // Act
        var tools = await client.ListToolsAsync();

        // Assert
        Assert.That(tools.Select(tool => tool.Name).Order(), Is.EqualTo(new[] { "get_item", "propose_item_changes" }));
    }

    [Test]
    public async Task CallTool_Granted_RunsAsTheAskerInTheJobsProjectAndIgnoresTheProjectHeader()
    {
        // Arrange
        using var factory = NewFactory();
        await using var client = await ConnectAsync(factory, project: "OPS");

        // Act
        var (isError, body) = await McpTesting.CallAsync(client, "get_item", new { itemKey = "WEB-1" });
        var (otherProject, otherBody) = await McpTesting.CallAsync(client, "get_item", new { itemKey = "OPS-1" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(factory.Modules.Items.GetCalls.Single(), Is.EqualTo((TestUsers.Member.ToActor(), "WEB-1")), "身分是發問的人");
            Assert.That(otherProject, Is.True, "標頭寫 OPS 也沒用：專案是工作的專案");
            Assert.That(otherBody["error"]?.GetValue<string>(), Does.Contain("找不到項目"));
        }
    }

    [Test]
    public async Task CallTool_TheAskersRoleIsWhatTheGrantSaysNow()
    {
        // Arrange
        using var factory = NewFactory();
        factory.Modules.HostGrants.Grant = GrantWith("get_item") with { IsOrganizationAdmin = true };
        await using var client = await ConnectAsync(factory);

        // Act
        await McpTesting.CallAsync(client, "get_item", new { itemKey = "WEB-1" });

        // Assert
        Assert.That(factory.Modules.Items.GetCalls.Single().Actor, Is.EqualTo(new Actor(TestUsers.OrganizationId, TestUsers.Member.PersonId, true)));
    }

    [TestCase("update_item", """{"itemKey":"WEB-1","title":"x"}""")]
    [TestCase("add_comment", """{"itemKey":"WEB-1","body":"x"}""")]
    [TestCase("list_projects", """{}""")]
    public async Task CallTool_NotGranted_IsAToolErrorAndNoModuleIsCalled(string tool, string json)
    {
        // Arrange
        using var factory = NewFactory();
        await using var client = await ConnectAsync(factory);

        // Act
        var (isError, body) = await McpTesting.CallAsync(client, tool, JsonSerializer.Deserialize<JsonElement>(json));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]?.GetValue<string>(), Does.Contain("不能用"));
            Assert.That(factory.Modules.Items.GetCalls, Is.Empty);
            Assert.That(factory.Modules.Projects.ListCalls, Is.Empty);
        }
    }

    [Test]
    public async Task ProposeNewItems_SameArgumentsTwice_CreatesOnceAndReplaysTheResult()
    {
        // Arrange
        using var factory = NewFactory();
        factory.Modules.HostGrants.Grant = GrantWith("propose_new_items");
        await using var client = await ConnectAsync(factory);
        var arguments = new { projectKey = "WEB", items = new[] { new { type = "task", title = "搬遷" } } };

        // Act
        var (firstError, firstBody) = await McpTesting.CallAsync(client, "propose_new_items", arguments);
        var (secondError, secondBody) = await McpTesting.CallAsync(client, "propose_new_items", arguments);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((firstError, secondError), Is.EqualTo((false, false)));
            Assert.That(factory.Modules.NewItemProposals.Creates, Has.Count.EqualTo(1), "同樣的提案只建立一次");
            Assert.That(secondBody.ToJsonString(), Is.EqualTo(firstBody.ToJsonString()));
        }
    }

    [Test]
    public async Task ProposeItemChanges_SameArgumentsInADifferentKeyOrder_CreatesOnceAndReplaysTheResult()
    {
        // Arrange
        using var factory = NewFactory();
        await using var client = await ConnectAsync(factory);
        const string first = """{"projectKey":"WEB","itemKey":"WEB-12","comment":"做完了","changes":[{"field":"status","value":"done"}]}""";
        const string second = """{"changes":[{"value":"done","field":"status"}],"comment":"做完了","itemKey":"WEB-12","projectKey":"WEB"}""";

        // Act
        var (firstError, firstBody) = await McpTesting.CallAsync(client, "propose_item_changes", JsonSerializer.Deserialize<JsonElement>(first));
        var (secondError, secondBody) = await McpTesting.CallAsync(client, "propose_item_changes", JsonSerializer.Deserialize<JsonElement>(second));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((firstError, secondError), Is.EqualTo((false, false)));
            Assert.That(factory.Modules.ItemProposals.Creates, Has.Count.EqualTo(1), "同樣的提案只建立一次");
            Assert.That(secondBody.ToJsonString(), Is.EqualTo(firstBody.ToJsonString()));
            Assert.That(factory.Modules.HostGrants.BeginCalls.Select(call => call.Hash).Distinct().Count(), Is.EqualTo(1), "鍵的順序不影響指紋");
            Assert.That(factory.Modules.HostGrants.CompleteCalls.First().ResultJson, Is.Not.Null);
        }
    }

    [Test]
    public async Task ProposeItemChanges_DifferentArguments_AreSeparateProposals()
    {
        // Arrange
        using var factory = NewFactory();
        await using var client = await ConnectAsync(factory);

        // Act
        await McpTesting.CallAsync(client, "propose_item_changes", new { projectKey = "WEB", itemKey = "WEB-12", comment = "一" });
        await McpTesting.CallAsync(client, "propose_item_changes", new { projectKey = "WEB", itemKey = "WEB-12", comment = "二" });

        // Assert
        Assert.That(factory.Modules.ItemProposals.Creates, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ProposeItemChanges_FailedAttempt_ReleasesTheInProgressMarkSoTheModelCanRetry()
    {
        // Arrange
        using var factory = NewFactory();
        var attempts = 0;
        factory.Modules.ItemProposals.OnCreate = _ => ++attempts == 1
            ? new Failure(FailureKind.Invalid, AiErrorCodes.SettingsInvalid, "欄位不對。")
            : FakeItemProposals.Proposal();
        await using var client = await ConnectAsync(factory);
        var arguments = new { projectKey = "WEB", itemKey = "WEB-12", comment = "x" };

        // Act
        var (firstError, _) = await McpTesting.CallAsync(client, "propose_item_changes", arguments);
        var (secondError, _) = await McpTesting.CallAsync(client, "propose_item_changes", arguments);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((firstError, secondError), Is.EqualTo((true, false)));
            Assert.That(factory.Modules.HostGrants.CompleteCalls.Select(call => call.ResultJson is null), Is.EqualTo(new[] { true, false }));
            Assert.That(factory.Modules.ItemProposals.Creates, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task ProposeItemChanges_AnotherRequestIsStillRunning_IsAToolErrorWithoutCreating()
    {
        // Arrange
        using var factory = NewFactory();
        var arguments = new { projectKey = "WEB", itemKey = "WEB-12", comment = "x" };
        factory.Modules.HostGrants.ForcedReplay = HostToolReplay.InProgress;
        await using var client = await ConnectAsync(factory);

        // Act
        var (isError, body) = await McpTesting.CallAsync(client, "propose_item_changes", arguments);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]?.GetValue<string>(), Does.Contain("正在建立"));
            Assert.That(factory.Modules.ItemProposals.Creates, Is.Empty);
        }
    }

    [Test]
    public async Task ReadTools_DoNotTouchTheWriteLedger()
    {
        // Arrange
        using var factory = NewFactory();
        await using var client = await ConnectAsync(factory);

        // Act
        await McpTesting.CallAsync(client, "get_item", new { itemKey = "WEB-1" });

        // Assert
        Assert.That(factory.Modules.HostGrants.BeginCalls, Is.Empty);
    }

    [Test]
    public async Task Request_GrantEndedOrCancelled_Is401WithTheMcpChallenge()
    {
        // Arrange
        using var factory = NewFactory();
        factory.Modules.HostGrants.Grant = null;
        using var http = factory.CreateClient();

        // Act
        using var response = await McpTesting.PostRawAsync(http, FakeHostToolGrants.Token);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(response.Headers.WwwAuthenticate.ToString(), Does.Contain("resource_metadata"));
        }
    }

    [Test]
    public async Task Request_GrantEndsBetweenCalls_TheNextCallIsRefused()
    {
        // Arrange
        using var factory = NewFactory();
        using var http = factory.CreateClient();
        using var before = await McpTesting.PostRawAsync(http, FakeHostToolGrants.Token);

        // Act
        factory.Modules.HostGrants.Grant = null;
        using var after = await McpTesting.PostRawAsync(http, FakeHostToolGrants.Token);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(before.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(factory.Modules.HostGrants.ResolveCalls, Has.Count.EqualTo(2), "每個請求都重新問，不快取");
        }
    }

    [Test]
    public async Task Request_TooManyInvalidGrants_AreCountedAndBlockedBeforeLookingThemUp()
    {
        // Arrange
        using var factory = NewFactory();
        using var http = factory.CreateClient();
        for (var i = 0; i < ApiLimits.BearerFailuresPerWindow; i++)
        {
            using var failed = await McpTesting.PostRawAsync(http, $"kjt_wrong-{i}");
            Assert.That(failed.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        var lookups = factory.Modules.HostGrants.ResolveCalls.Count;

        // Act
        using var blocked = await McpTesting.PostRawAsync(http, $"kjt_wrong-again");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(blocked.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(factory.Modules.HostGrants.ResolveCalls, Has.Count.EqualTo(lookups));
        }
    }

    [Test]
    public async Task Request_ManyInvalidGrants_DoNotBlockTheWorkersMachineCredentialOnTheSameAddress()
    {
        // Arrange：worker 與引擎子程序同一個 IP；引擎用剛失效的授權打了很多次。
        using var factory = NewFactory();
        using var http = factory.CreateClient();
        for (var i = 0; i <= ApiLimits.BearerFailuresPerWindow; i++)
        {
            using var failed = await McpTesting.PostRawAsync(http, $"kjt_stale-{i}");
        }

        using var grantBlocked = await McpTesting.PostRawAsync(http, "kjt_stale-again");
        var checkIn = AccessTokenAuthenticationTests.Bearer(HttpMethod.Post, "/v1/host-agent/check-in", FakeHostAgentWorkerService.Credential);
        checkIn.Headers.TryAddWithoutValidation("X-Kumi-Machine", FakeHostAgentWorkerService.MachineKey);
        checkIn.Content = ApiRequests.Json(new { agentVersion = "0.9.0", serverUrl = "http://api:8080", capacity = 0, engines = Array.Empty<object>() });
        var ordinary = AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, "/v1/session", "pmt_" + new string('x', 20));

        // Act
        using var worker = await http.SendAsync(checkIn);
        using var other = await http.SendAsync(ordinary);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(grantBlocked.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(worker.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(other.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "一般 Bearer 照常 401，不是 429");
        }
    }

    [Test]
    public async Task Request_ManyInvalidMachineCredentials_DoNotBlockToolGrants()
    {
        // Arrange
        using var factory = NewFactory();
        using var http = factory.CreateClient();
        for (var i = 0; i <= ApiLimits.BearerFailuresPerWindow; i++)
        {
            using var failed = await http.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Post, "/v1/host-agent/check-in", $"kha_wrong-{i}"));
        }

        // Act
        using var response = await McpTesting.PostRawAsync(http, FakeHostToolGrants.Token);

        // Assert
        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.TooManyRequests));
    }

    [Test]
    public async Task Request_ForeignOrigin_Is403EvenWithAValidGrant()
    {
        // Arrange
        using var factory = NewFactory();
        using var http = factory.CreateClient();

        // Act
        using var response = await McpTesting.PostRawAsync(http, FakeHostToolGrants.Token, origin: "https://evil.example.com");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Request_BodyOverTheLimit_Is413()
    {
        // Arrange
        using var factory = NewFactory();
        using var http = factory.CreateClient();

        // Act
        using var response = await McpTesting.PostRawAsync(http, FakeHostToolGrants.Token, padding: McpLimits.MaxRequestBytes + 10);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.RequestEntityTooLarge));
    }

    [Test]
    public async Task MachineCredential_OnMcp_Is401()
    {
        // Arrange
        using var factory = NewFactory();
        using var http = factory.CreateClient();

        // Act
        using var response = await McpTesting.PostRawAsync(http, FakeHostAgentWorkerService.Credential);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(factory.Modules.HostWorker.AuthenticateCalls, Is.Zero);
            Assert.That(factory.Modules.HostGrants.ResolveCalls, Is.Empty);
        }
    }

    [TestCase("GET", "/v1/session")]
    [TestCase("GET", "/v1/projects")]
    [TestCase("GET", "/v1/items/WEB-1")]
    [TestCase("GET", "/v1/ai/options")]
    [TestCase("POST", "/v1/agent/check-in")]
    [TestCase("POST", "/v1/host-agent/check-in")]
    public async Task ToolGrant_OnAnyOtherEndpoint_Is401AndNeverBecomesAPerson(string method, string route)
    {
        // Arrange
        using var factory = NewFactory();
        using var client = factory.CreateClient();
        var request = AccessTokenAuthenticationTests.Bearer(new HttpMethod(method), route, FakeHostToolGrants.Token);

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
            Assert.That(factory.Modules.HostGrants.ResolveCalls, Is.Empty, "只有 /mcp 解析工具授權");
            Assert.That(factory.Modules.AccessTokens.FindCalls, Is.Empty);
            Assert.That(factory.Modules.Projects.ListCalls, Is.Empty);
        }
    }

    [Test]
    public async Task OrdinaryOAuthTokens_AreUnaffected()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync(TestUsers.Member);

        // Act
        var (isError, _) = await mcp.CallAsync("get_item", new { itemKey = "WEB-1" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False);
            Assert.That(mcp.Factory.Modules.HostGrants.ResolveCalls, Is.Empty);
        }
    }
}
