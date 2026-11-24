using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Blueprint;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>變更集的五個端點：成功時的回應、交給模組的參數、編號、沒有內文的端點、模組的失敗與防偽標頭。</summary>
[TestFixture]
public sealed class ChangeSetEndpointTests
{
    private const string Route = "/v1/projects/WEB/changesets";

    private const string Document = """{"baseVersion":1,"summary":"拆開驗收","blueprint":{}}""";

    [Test]
    public async Task List_SignedIn_PassesTheProjectAndReturnsTheChangeSetsInTheModulesOrder()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Changes.OnList = (_, _) => new[]
        {
            TestData.ChangeSet(3, TestUsers.Owner.PersonId),
            TestData.ChangeSet(2, TestUsers.Member.PersonId),
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync(Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?.AsArray().Select(c => c?["number"]?.GetValue<long>()), Is.EqualTo(new long[] { 3, 2 }));
            Assert.That(factory.Modules.Changes.ListCalls, Is.EqualTo(new[] { (TestUsers.Member.ToActor(), "WEB") }));
        }
    }

    [Test]
    public async Task Propose_Accepted_Returns201WithTheLocationOfTheChangeSetAndItsDetail()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Changes.OnPropose = call => TestData.ChangeSetDetail(TestData.ChangeSet(7, call.Actor.PersonId));
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(Route, ApiRequests.Document(Document));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Headers.Location?.ToString(), Is.EqualTo("/v1/projects/WEB/changesets/7"));
            Assert.That(data?["changeSet"]?["number"]?.GetValue<long>(), Is.EqualTo(7));
            Assert.That(data?["changeSet"]?["status"]?.GetValue<string>(), Is.EqualTo("pending"));
            Assert.That(data?["canApply"]?.GetValue<bool>(), Is.True);
            Assert.That(factory.Modules.Changes.ProposeCalls, Is.EqualTo(new[]
            {
                new ProposeCall(TestUsers.Owner.ToActor(), "WEB", Document, DocumentFormat.Json),
            }));
        }
    }

    [Test]
    public async Task Get_Found_PassesTheNumberAndReturnsTheDetail()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"{Route}/12");
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["changeSet"]?["number"]?.GetValue<long>(), Is.EqualTo(12));
            Assert.That(factory.Modules.Changes.GetCalls, Is.EqualTo(new[] { new ChangeSetCall(TestUsers.Member.ToActor(), "WEB", 12) }));
        }
    }

    [Test]
    public async Task Apply_Accepted_ReturnsTheAppliedDetail()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync($"{Route}/4/apply", null);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["changeSet"]?["status"]?.GetValue<string>(), Is.EqualTo("applied"));
            Assert.That(data?["changeSet"]?["appliedVersion"]?.GetValue<long>(), Is.EqualTo(2));
            Assert.That(factory.Modules.Changes.ApplyCalls, Is.EqualTo(new[] { new ChangeSetCall(TestUsers.Owner.ToActor(), "WEB", 4) }));
            Assert.That(factory.Modules.Changes.RejectCalls, Is.Empty);
        }
    }

    [Test]
    public async Task Reject_Accepted_ReturnsTheRejectedDetail()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync($"{Route}/4/reject", null);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["changeSet"]?["status"]?.GetValue<string>(), Is.EqualTo("rejected"));
            Assert.That(data?["changeSet"]?["decidedBy"]?["personId"]?.GetValue<Guid>(), Is.EqualTo(TestUsers.Member.PersonId));
            Assert.That(factory.Modules.Changes.RejectCalls, Is.EqualTo(new[] { new ChangeSetCall(TestUsers.Member.ToActor(), "WEB", 4) }));
            Assert.That(factory.Modules.Changes.ApplyCalls, Is.Empty);
        }
    }

    [TestCase("GET", "abc")]
    [TestCase("GET", "0")]
    [TestCase("GET", "-1")]
    [TestCase("GET", "1.5")]
    [TestCase("GET", "1e3")]
    [TestCase("GET", "99999999999999999999")]
    [TestCase("POST", "0/apply")]
    [TestCase("POST", "abc/apply")]
    [TestCase("POST", "-3/reject")]
    [TestCase("POST", "x/reject")]
    public async Task Endpoint_NumberIsNotAPositiveInteger_Returns404WithoutReachingTheModule(string method, string rest)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);
        using var request = new HttpRequestMessage(new HttpMethod(method), $"{Route}/{rest}");

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, ApiErrorCodes.NotFound);
        Assert.That(factory.Modules.Changes.TotalCalls, Is.Zero);
    }

    [TestCase("apply", "application/json", """{"reason":"沒有用"}""")]
    [TestCase("reject", "text/plain", "這段內文不會被讀")]
    [TestCase("apply", "application/json", "{ 不是 JSON")]
    public async Task ApplyOrReject_WithABody_IgnoresTheBodyLikeOtherEndpointsWithoutOne(
        string action, string contentType, string body)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync($"{Route}/4/{action}", ApiRequests.Document(body, contentType));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        Assert.That(factory.Modules.Changes.TotalCalls, Is.EqualTo(1));
    }

    [TestCase("propose", FailureKind.Invalid, WorkErrorCodes.ChangeSetInvalid, HttpStatusCode.UnprocessableEntity)]
    [TestCase("propose", FailureKind.Forbidden, WorkErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    [TestCase("propose", FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, HttpStatusCode.NotFound)]
    [TestCase("list", FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, HttpStatusCode.NotFound)]
    [TestCase("list", FailureKind.Forbidden, WorkErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    [TestCase("get", FailureKind.NotFound, WorkErrorCodes.ChangeSetNotFound, HttpStatusCode.NotFound)]
    [TestCase("get", FailureKind.Forbidden, WorkErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    [TestCase("apply", FailureKind.NotFound, WorkErrorCodes.ChangeSetNotFound, HttpStatusCode.NotFound)]
    [TestCase("apply", FailureKind.Conflict, WorkErrorCodes.ChangeSetNotPending, HttpStatusCode.Conflict)]
    [TestCase("apply", FailureKind.Conflict, WorkErrorCodes.ChangeSetOutdated, HttpStatusCode.Conflict)]
    [TestCase("apply", FailureKind.Conflict, WorkErrorCodes.ChangeSetApplyBlocked, HttpStatusCode.Conflict)]
    [TestCase("apply", FailureKind.Conflict, WorkErrorCodes.ChangeSetBusy, HttpStatusCode.Conflict)]
    [TestCase("apply", FailureKind.Forbidden, WorkErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    [TestCase("reject", FailureKind.NotFound, WorkErrorCodes.ChangeSetNotFound, HttpStatusCode.NotFound)]
    [TestCase("reject", FailureKind.Conflict, WorkErrorCodes.ChangeSetNotPending, HttpStatusCode.Conflict)]
    [TestCase("reject", FailureKind.Forbidden, WorkErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    public async Task Endpoint_ModuleFails_ReturnsTheMatchingStatusWithTheModulesCodeMessageAndDetails(
        string endpoint, FailureKind kind, string code, HttpStatusCode expected)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        FailWith(factory.Modules.Changes, endpoint, new Failure(kind, code, "模組說的原因。")
        {
            Details =
            [
                new FailureDetail(IssueCodes.PlanStateMoveRequired, "/migrations/moveStates", "「待驗收」被拿掉了，要指定搬去哪裡。"),
                new FailureDetail(WorkErrorCodes.ChangeSetItemsOfRemovedType, "", "還有 3 個項目是「錯誤」這個類型。"),
            ],
        });
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await SendAsync(client, endpoint);
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, expected, code);
        var envelope = JsonNode.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(envelope?["error"]?["message"]?.GetValue<string>(), Is.EqualTo("模組說的原因。"));
            Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[]
            {
                "plan.state_move_required|/migrations/moveStates", "changeset.items_of_removed_type|",
            }));
            Assert.That(details.Select(d => d?["message"]?.GetValue<string>()), Is.EqualTo(new[]
            {
                "「待驗收」被拿掉了，要指定搬去哪裡。", "還有 3 個項目是「錯誤」這個類型。",
            }));
            Assert.That(factory.Modules.People.FindManyCalls, Is.Empty);
        }
    }

    [TestCase("propose")]
    [TestCase("apply")]
    [TestCase("reject")]
    public async Task Write_SignedInWithoutTheCsrfHeader_Returns403WithoutReachingTheModule(string endpoint)
    {
        // Arrange：工作階段是真的，只是沒帶防偽標頭（別的網站的表單送得出 Cookie，送不出自訂標頭）。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var signIn = factory.CreateApiClient();
        var cookie = await Sessions.SignInForCookieAsync(signIn, TestUsers.Owner);
        using var client = factory.CreateClient(new() { HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", cookie);

        // Act
        using var response = await SendAsync(client, endpoint);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.CsrfRequired);
        Assert.That(factory.Modules.Changes.TotalCalls, Is.Zero);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string endpoint) => endpoint switch
    {
        "propose" => client.PostAsync(Route, ApiRequests.Document(Document)),
        "list" => client.GetAsync(Route),
        "get" => client.GetAsync($"{Route}/3"),
        "apply" => client.PostAsync($"{Route}/3/apply", null),
        "reject" => client.PostAsync($"{Route}/3/reject", null),
        _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, "沒有這個端點。"),
    };

    private static void FailWith(FakeBlueprintChangeService changes, string endpoint, Failure failure)
    {
        switch (endpoint)
        {
            case "propose":
                changes.OnPropose = _ => failure;
                break;
            case "list":
                changes.OnList = (_, _) => failure;
                break;
            case "get":
                changes.OnGet = _ => failure;
                break;
            case "apply":
                changes.OnApply = _ => failure;
                break;
            case "reject":
                changes.OnReject = _ => failure;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, "沒有這個端點。");
        }
    }
}
