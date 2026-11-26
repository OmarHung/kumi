using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Live;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>AI 助理的新專案建議端點：形狀、錯誤碼、建立成功後通知即時連線、只能用 Cookie。規則（誰能建立、順序）在 AI 模組的測試。</summary>
[TestFixture]
public sealed class ProjectProposalEndpointTests
{
    private static readonly string Route = $"/v1/ai/project-proposals/{FakeProjectProposals.ProposalId}";

    private const string PendingJson = """
        {"id":"55555555-5555-5555-5555-555555555555","key":"WEB","name":"網站改版","template":"simple_tasks","templateName":"簡單任務",
         "includeExamples":false,"description":"## 目標\n改版","status":"pending","createdAt":"2026-11-26T03:00:00Z",
         "decidedAt":null,"createdProjectKey":null}
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
            Assert.That(factory.Modules.ProjectProposals.Calls.Single(), Is.EqualTo(("get", TestUsers.Member.ToActor(), FakeProjectProposals.ProposalId)));
        }
    }

    [Test]
    public async Task Apply_ReturnsTheAppliedProposalWithTheCreatedProjectAndNotifiesEveryLiveConnection()
    {
        // Arrange
        var notifier = new RecordingNotifier();
        using var factory = NewFactory(notifier);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(Route + "/apply", null);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["status"]?.GetValue<string>(), Is.EqualTo("applied"));
            Assert.That(data?["decidedAt"]?.GetValue<string>(), Is.EqualTo("2026-11-26T03:05:00Z"));
            Assert.That(data?["createdProjectKey"]?.GetValue<string>(), Is.EqualTo("WEB"));
            Assert.That(factory.Modules.ProjectProposals.Calls.Single(), Is.EqualTo(("apply", TestUsers.Owner.ToActor(), FakeProjectProposals.ProposalId)));
            Assert.That(notifier.AccessChanges, Is.EqualTo(new IReadOnlyCollection<Guid>?[] { null }), "和建立專案一樣：所有人的連線都重算");
        }
    }

    [Test]
    public async Task Apply_Failure_KeepsTheProposalPendingAndNotifiesNobody()
    {
        // Arrange
        var notifier = new RecordingNotifier();
        using var factory = NewFactory(notifier);
        factory.Modules.ProjectProposals.OnApply = _ => new Failure(FailureKind.Conflict, WorkErrorCodes.ProjectKeyTaken, "代號已經有人用了。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(Route + "/apply", null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Conflict, WorkErrorCodes.ProjectKeyTaken);
            Assert.That(notifier.AccessChanges, Is.Empty);
        }
    }

    [Test]
    public async Task Apply_PassesATokenThatCannotBeCancelled()
    {
        // Arrange：建立專案不跟著請求取消（建到一半關掉頁面，建議的狀態也要存下來）。
        using var factory = NewFactory();
        CancellationToken? seen = null;
        factory.Modules.ProjectProposals.OnApplyToken = token => seen = token;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(Route + "/apply", null);

        // Assert
        Assert.That(seen?.CanBeCanceled, Is.False);
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
            Assert.That(factory.Modules.ProjectProposals.Calls.Single().Action, Is.EqualTo("dismiss"));
            Assert.That(notifier.AccessChanges, Is.Empty);
        }
    }

    [TestCase("GET", "", FailureKind.NotFound, AiErrorCodes.ProjectProposalNotFound, HttpStatusCode.NotFound)]
    [TestCase("POST", "/apply", FailureKind.NotFound, AiErrorCodes.ProjectProposalNotFound, HttpStatusCode.NotFound)]
    [TestCase("POST", "/apply", FailureKind.Conflict, AiErrorCodes.ProjectProposalClosed, HttpStatusCode.Conflict)]
    [TestCase("POST", "/apply", FailureKind.Forbidden, WorkErrorCodes.Forbidden, HttpStatusCode.Forbidden)]
    [TestCase("POST", "/apply", FailureKind.Invalid, WorkErrorCodes.ProjectKeyInvalid, HttpStatusCode.UnprocessableEntity)]
    [TestCase("POST", "/dismiss", FailureKind.NotFound, AiErrorCodes.ProjectProposalNotFound, HttpStatusCode.NotFound)]
    [TestCase("POST", "/dismiss", FailureKind.Conflict, AiErrorCodes.ProjectProposalClosed, HttpStatusCode.Conflict)]
    public async Task Failures_AreReturnedWithTheModulesCodeAndStatus(
        string method, string suffix, FailureKind kind, string code, HttpStatusCode status)
    {
        // Arrange
        using var factory = NewFactory();
        var failure = new Failure(kind, code, "原因說明");
        factory.Modules.ProjectProposals.OnGet = _ => failure;
        factory.Modules.ProjectProposals.OnApply = _ => failure;
        factory.Modules.ProjectProposals.OnDismiss = _ => failure;
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
        using var response = await client.GetAsync("/v1/ai/project-proposals/not-a-guid");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(factory.Modules.ProjectProposals.Calls, Is.Empty);
        }
    }

    [TestCase("GET", "")]
    [TestCase("POST", "/apply")]
    [TestCase("POST", "/dismiss")]
    public async Task EveryEndpoint_PersonalAccessToken_IsRefusedBecauseItsWebOnly(string method, string suffix)
    {
        // Arrange
        using var factory = NewFactory();
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Owner, [.. PmTool.Identity.AccessTokenScopes.Grantable]);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), Route + suffix);
        request.Headers.Authorization = new("Bearer", secret);

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.InsufficientScope);
        Assert.That(factory.Modules.ProjectProposals.Calls, Is.Empty);
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
