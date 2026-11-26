using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Live;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>AI 助理的成員建議端點：形狀、錯誤碼、只能用 Cookie、套用後叫連線重算權限。規則（誰能套用、部分成功）在 AI 模組的測試。</summary>
[TestFixture]
public sealed class MemberProposalEndpointTests
{
    private static readonly string Route = $"/v1/ai/member-proposals/{FakeMemberProposals.ProposalId}";

    private const string PendingJson = """
        {"id":"77777777-7777-7777-7777-777777777777","projectKey":"WEB",
         "changes":[
           {"personId":"88888888-8888-8888-8888-888888888888","displayName":"新人","role":"contributor","roleName":"執行者","previousRole":null,"previousRoleName":null},
           {"personId":"99999999-9999-9999-9999-999999999999","displayName":"老手","role":"editor","roleName":"編輯者","previousRole":"contributor","previousRoleName":"執行者"},
           {"personId":"cccccccc-cccc-cccc-cccc-cccccccccccc","displayName":"舊人","role":null,"roleName":null,"previousRole":"viewer","previousRoleName":"檢視者"}],
         "status":"pending","createdAt":"2026-11-27T03:00:00Z","decidedAt":null,"error":null}
        """;

    private sealed class RecordingNotifier : ILiveNotifier
    {
        public List<IReadOnlyCollection<Guid>?> AccessChanges { get; } = [];

        public Task InboxChangedAsync(Guid personId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InboxChangedAsync(IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken) => Task.CompletedTask;

        public void AccessChanged(IReadOnlyCollection<Guid>? personIds) => AccessChanges.Add(personIds);
    }

    private static ApiFactory NewFactory(RecordingNotifier? notifier = null) => new(
        FakeBlueprintValidator.Passing(),
        new ApiFactoryOptions
        {
            ConfigureServices = services =>
            {
                if (notifier is not null)
                {
                    services.AddSingleton<ILiveNotifier>(notifier);
                }
            },
        });

    [Test]
    public async Task Get_ReturnsTheProposalInTheContractShapeForTheSignedInPerson()
    {
        // Arrange
        using var factory = NewFactory();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.GetAsync(Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data, JsonNode.Parse(PendingJson)), Is.True, data?.ToJsonString());
            Assert.That(factory.Modules.MemberProposals.Calls.Single(), Is.EqualTo(("get", TestUsers.Member.ToActor(), FakeMemberProposals.ProposalId)));
        }
    }

    [Test]
    public async Task Apply_ReturnsTheAppliedProposalAndRecalculatesEveryChangedPersonsConnections()
    {
        // Arrange
        var notifier = new RecordingNotifier();
        using var factory = NewFactory(notifier);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(Route + "/apply", null);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["status"]?.GetValue<string>(), Is.EqualTo("applied"));
            Assert.That(data?["error"]?.GetValue<string>(), Does.Contain("其餘已經套用"));
            Assert.That(factory.Modules.MemberProposals.Calls.Single(), Is.EqualTo(("apply", TestUsers.Member.ToActor(), FakeMemberProposals.ProposalId)));
            Assert.That(notifier.AccessChanges, Has.Count.EqualTo(1));
            Assert.That(
                notifier.AccessChanges.Single(),
                Is.EquivalentTo(new[] { FakeMemberProposals.JoinerId, FakeMemberProposals.ChangerId, FakeMemberProposals.LeaverId }),
                "部分成功也算：變動的人都重算");
        }
    }

    [Test]
    public async Task Apply_Failure_NotifiesNobody()
    {
        // Arrange
        var notifier = new RecordingNotifier();
        using var factory = NewFactory(notifier);
        factory.Modules.MemberProposals.OnApply = _ => new Failure(FailureKind.Conflict, WorkErrorCodes.LastAdmin, "專案至少要留一位管理員。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(Route + "/apply", null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Conflict, WorkErrorCodes.LastAdmin);
            Assert.That(notifier.AccessChanges, Is.Empty);
        }
    }

    [Test]
    public async Task Dismiss_ReturnsTheDismissedProposalAndDoesNotNotify()
    {
        // Arrange
        var notifier = new RecordingNotifier();
        using var factory = NewFactory(notifier);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);

        // Act
        using var response = await client.PostAsync(Route + "/dismiss", null);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["status"]?.GetValue<string>(), Is.EqualTo("dismissed"));
            Assert.That(factory.Modules.MemberProposals.Calls.Single().Action, Is.EqualTo("dismiss"));
            Assert.That(notifier.AccessChanges, Is.Empty);
        }
    }

    [TestCase("GET", "", FailureKind.NotFound, AiErrorCodes.MemberProposalNotFound, HttpStatusCode.NotFound)]
    [TestCase("POST", "/apply", FailureKind.NotFound, AiErrorCodes.MemberProposalNotFound, HttpStatusCode.NotFound)]
    [TestCase("POST", "/apply", FailureKind.Conflict, AiErrorCodes.MemberProposalClosed, HttpStatusCode.Conflict)]
    [TestCase("POST", "/apply", FailureKind.Forbidden, WorkErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    [TestCase("POST", "/dismiss", FailureKind.NotFound, AiErrorCodes.MemberProposalNotFound, HttpStatusCode.NotFound)]
    [TestCase("POST", "/dismiss", FailureKind.Conflict, AiErrorCodes.MemberProposalClosed, HttpStatusCode.Conflict)]
    public async Task Failures_AreReturnedWithTheModulesCodeAndStatus(
        string method, string suffix, FailureKind kind, string code, HttpStatusCode status)
    {
        // Arrange
        using var factory = NewFactory();
        var failure = new Failure(kind, code, "原因說明");
        factory.Modules.MemberProposals.OnGet = _ => failure;
        factory.Modules.MemberProposals.OnApply = _ => failure;
        factory.Modules.MemberProposals.OnDismiss = _ => failure;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), Route + suffix));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, status, code);
    }

    [Test]
    public async Task Route_IdThatIsNotAGuid_Is404WithoutCallingTheModule()
    {
        // Arrange
        using var factory = NewFactory();
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/ai/member-proposals/not-a-guid");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(factory.Modules.MemberProposals.Calls, Is.Empty);
        }
    }

    [TestCase("GET", "")]
    [TestCase("POST", "/apply")]
    [TestCase("POST", "/dismiss")]
    public async Task EveryEndpoint_PersonalAccessToken_IsRefusedBecauseItsWebOnly(string method, string suffix)
    {
        // Arrange
        using var factory = NewFactory();
        var secret = factory.Modules.AccessTokens.Issue(
            TestUsers.Owner, PmTool.Identity.AccessTokenScope.ItemsRead, PmTool.Identity.AccessTokenScope.ItemsWrite,
            PmTool.Identity.AccessTokenScope.BlueprintRead, PmTool.Identity.AccessTokenScope.BlueprintWrite);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), Route + suffix);
        request.Headers.Authorization = new("Bearer", secret);

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.InsufficientScope);
        Assert.That(factory.Modules.MemberProposals.Calls, Is.Empty);
    }

    [Test]
    public async Task Get_NotSignedIn_Is401()
    {
        // Arrange
        using var factory = NewFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, Route));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}
