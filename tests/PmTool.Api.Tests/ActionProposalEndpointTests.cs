using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

/// <summary>通用動作建議的端點：形狀、卡片輸入（有傳到服務、回應裡沒有）、錯誤碼、套用不跟著取消、只能用 Cookie。規則在 AI 模組的測試。</summary>
[TestFixture]
public sealed class ActionProposalEndpointTests
{
    private static readonly string Route = $"/v1/ai/action-proposals/{FakeActionProposals.ProposalId}";

    private static ApiFactory NewFactory() => new(FakeBlueprintValidator.Passing());

    [Test]
    public async Task Get_ReturnsTheProposalInTheContractShape()
    {
        // Arrange
        const string expected = """
            {"id":"a5a5a5a5-0000-0000-0000-000000000001","kind":"people","projectKey":null,"title":"建議調整人員","reason":null,
             "steps":[{"label":"新增人員","target":"新同事","before":null,"after":"新同事（new@example.com）· 一般成員","note":null,
                       "format":"text","done":false,"error":null,"link":null}],
             "inputs":[{"key":"password:0","label":"新同事的初始密碼","kind":"password","hint":null}],
             "status":"pending","createdAt":"2026-11-27T03:00:00Z","decidedAt":null,"error":null}
            """;
        using var factory = NewFactory();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.GetAsync(Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse(expected)), Is.True, data?.ToJsonString());
            Assert.That(factory.Modules.ActionProposals.Calls.Single(), Is.EqualTo(("get", TestUsers.Member.ToActor(), FakeActionProposals.ProposalId)));
        }
    }

    [Test]
    public async Task Apply_WithInputs_PassesThemToTheServiceAndNeverEchoesThem()
    {
        // Arrange
        const string secret = "S3cret-初始密碼-ZZ9";
        using var factory = NewFactory();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsJsonAsync(Route + "/apply", new { inputs = new Dictionary<string, string> { ["password:0"] = secret } });
        var raw = await response.Content.ReadAsStringAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(raw, Does.Not.Contain(secret));
            Assert.That(JsonNode.Parse(raw)?["data"]?["status"]?.GetValue<string>(), Is.EqualTo("applied"));
            Assert.That(factory.Modules.ActionProposals.AppliedInputs.Single()["password:0"], Is.EqualTo(secret));
        }
    }

    [Test]
    public async Task Apply_WithoutBodyOrWithoutInputs_PassesEmptyInputs()
    {
        // Arrange
        using var factory = NewFactory();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var noBody = await client.PostAsync(Route + "/apply", null);
        using var emptyObject = await client.PostAsJsonAsync(Route + "/apply", new { });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(noBody.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(emptyObject.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(factory.Modules.ActionProposals.AppliedInputs, Has.Count.EqualTo(2));
            Assert.That(factory.Modules.ActionProposals.AppliedInputs, Is.All.Empty);
        }
    }

    [TestCase("""{"inputs":["x"]}""")]
    [TestCase("""{"inputs":{"password:0":5}}""")]
    [TestCase("""{"inputs":{"password:0":"含\u0007控制字元"}}""")]
    [TestCase("""{"inputs":{"含\u0001控制字元":"x"}}""")]
    [TestCase("not json")]
    [TestCase("[]")]
    public async Task Apply_WithMalformedBody_Is422AndTheServiceIsNotCalled(string body)
    {
        // Arrange
        using var factory = NewFactory();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        // Act
        using var response = await client.PostAsync(Route + "/apply", content);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, AiErrorCodes.FieldInvalid);
            Assert.That(factory.Modules.ActionProposals.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task Apply_WithAChunkedBodyOverTheLimit_Is413AndTheServiceIsNotCalled()
    {
        // Arrange：沒有 Content-Length，只能讀到超過上限才知道。
        using var factory = NewFactory();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);
        var body = "{\"inputs\":{\"password:0\":\"" + new string('a', 200 * 1024) + "\"}}";
        using var content = new StreamContent(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body)));
        content.Headers.ContentType = new("application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, Route + "/apply") { Content = content };
        request.Headers.TransferEncodingChunked = true;

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.RequestEntityTooLarge, ApiErrorCodes.PayloadTooLarge);
            Assert.That(factory.Modules.ActionProposals.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task Apply_WithAContentLengthOverTheLimit_Is413()
    {
        // Arrange
        using var factory = NewFactory();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);
        using var content = new StringContent("{\"inputs\":{\"k\":\"" + new string('a', 70 * 1024) + "\"}}", System.Text.Encoding.UTF8, "application/json");

        // Act
        using var response = await client.PostAsync(Route + "/apply", content);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.RequestEntityTooLarge, ApiErrorCodes.PayloadTooLarge);
    }

    [Test]
    public async Task Apply_WithTooLongInputValue_Is422()
    {
        // Arrange
        using var factory = NewFactory();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsJsonAsync(Route + "/apply", new { inputs = new Dictionary<string, string> { ["password:0"] = new string('a', 1001) } });

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, AiErrorCodes.FieldInvalid);
    }

    [Test]
    public async Task Apply_DoesNotFollowTheRequestsCancellation_AndDismissIsCalledAsTheSignedInPerson()
    {
        // Arrange
        using var factory = NewFactory();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var apply = await client.PostAsync(Route + "/apply", null);
        using var dismiss = await client.PostAsync(Route + "/dismiss", null);
        var dismissed = await ApiRequests.AssertSuccessEnvelopeAsync(dismiss, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.Modules.ActionProposals.ApplyTokens.Single().CanBeCanceled, Is.False);
            Assert.That(dismissed?["status"]?.GetValue<string>(), Is.EqualTo("dismissed"));
            Assert.That(factory.Modules.ActionProposals.Calls.Select(c => c.Action), Is.EqualTo(new[] { "apply", "dismiss" }));
            Assert.That(factory.Modules.ActionProposals.Calls.Select(c => c.Actor), Is.All.EqualTo(TestUsers.Member.ToActor()));
        }
    }

    [Test]
    public async Task Failures_AreReturnedWithTheModulesCodeAndStatus()
    {
        // Arrange
        var cases = new (string Method, string Suffix, Failure Failure, HttpStatusCode Status)[]
        {
            ("GET", "", new Failure(FailureKind.NotFound, AiErrorCodes.ActionProposalNotFound, "找不到"), HttpStatusCode.NotFound),
            ("POST", "/apply", new Failure(FailureKind.NotFound, AiErrorCodes.ActionProposalNotFound, "找不到"), HttpStatusCode.NotFound),
            ("POST", "/apply", new Failure(FailureKind.Conflict, AiErrorCodes.ActionProposalClosed, "已經處理過"), HttpStatusCode.Conflict),
            ("POST", "/apply", new Failure(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "還有欄位沒填"), HttpStatusCode.UnprocessableEntity),
            ("POST", "/dismiss", new Failure(FailureKind.NotFound, AiErrorCodes.ActionProposalNotFound, "找不到"), HttpStatusCode.NotFound),
            ("POST", "/dismiss", new Failure(FailureKind.Conflict, AiErrorCodes.ActionProposalClosed, "已經處理過"), HttpStatusCode.Conflict),
        };

        foreach (var (method, suffix, failure, status) in cases)
        {
            using var factory = NewFactory();
            var fake = factory.Modules.ActionProposals;
            fake.OnGet = _ => failure;
            fake.OnApply = _ => failure;
            fake.OnDismiss = _ => failure;
            using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

            // Act
            using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), Route + suffix));

            // Assert
            await ApiRequests.AssertErrorEnvelopeAsync(response, status, failure.Code);
        }
    }

    [Test]
    public async Task Route_IdThatIsNotAGuid_Is404WithoutCallingTheModule()
    {
        // Arrange
        using var factory = NewFactory();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/ai/action-proposals/not-a-guid");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(factory.Modules.ActionProposals.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task EveryEndpoint_PersonalAccessToken_IsRefusedBecauseItsWebOnly()
    {
        // Arrange
        using var factory = NewFactory();
        var secret = factory.Modules.AccessTokens.Issue(
            TestUsers.Owner, PmTool.Identity.AccessTokenScope.ItemsRead, PmTool.Identity.AccessTokenScope.ItemsWrite,
            PmTool.Identity.AccessTokenScope.BlueprintRead, PmTool.Identity.AccessTokenScope.BlueprintWrite);
        using var client = factory.CreateClient();

        foreach (var (method, suffix) in new[] { ("GET", ""), ("POST", "/apply"), ("POST", "/dismiss") })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), Route + suffix);
            request.Headers.Authorization = new("Bearer", secret);

            // Act
            using var response = await client.SendAsync(request);

            // Assert
            await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.InsufficientScope);
        }

        Assert.That(factory.Modules.ActionProposals.Calls, Is.Empty);
    }

    [Test]
    public async Task Get_NotSignedIn_Is401()
    {
        // Arrange
        using var factory = NewFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(Route);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}
