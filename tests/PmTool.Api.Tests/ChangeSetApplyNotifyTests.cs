using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Api.Live;
using PmTool.Api.Tests.Support;
using PmTool.Blueprint;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>套用變更集之後通知成員重算即時連線：這一步出問題不影響已經成功的回應，也不會全站重算。</summary>
[TestFixture]
public sealed class ChangeSetApplyNotifyTests
{
    private const string ApplyRoute = "/v1/projects/WEB/changesets/4/apply";

    private sealed class RecordingNotifier : ILiveNotifier
    {
        public List<IReadOnlyCollection<Guid>?> AccessChanges { get; } = [];

        public Task InboxChangedAsync(Guid personId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InboxChangedAsync(IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken) => Task.CompletedTask;

        public void AccessChanged(IReadOnlyCollection<Guid>? personIds) => AccessChanges.Add(personIds);
    }

    private static ApiFactory NewFactory(RecordingNotifier notifier) => new(
        FakeBlueprintValidator.Passing(),
        new ApiFactoryOptions { ConfigureServices = services => services.AddSingleton<ILiveNotifier>(notifier) });

    [Test]
    public async Task Apply_TheMemberLookupThrows_StillRespondsWithTheAppliedChangeSetAndNotifiesNobody()
    {
        // Arrange
        var notifier = new RecordingNotifier();
        using var factory = NewFactory(notifier);
        factory.Modules.Projects.OnGet = (_, _) => throw new TimeoutException("成員清單暫時讀不到");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(ApplyRoute, null);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["changeSet"]?["status"]?.GetValue<string>(), Is.EqualTo("applied"));
            Assert.That(notifier.AccessChanges, Is.Empty);
        }
    }

    [Test]
    public async Task Apply_TheMemberLookupFails_StillRespondsAndDoesNotRecalculateEveryone()
    {
        // Arrange
        var notifier = new RecordingNotifier();
        using var factory = NewFactory(notifier);
        factory.Modules.Projects.OnGet = (_, _) => new Failure(FailureKind.NotFound, "project.not_found", "找不到專案。");
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(ApplyRoute, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(notifier.AccessChanges, Is.Empty);
        }
    }

    [Test]
    public async Task Apply_TheMembersAreKnown_NotifiesExactlyThoseMembers()
    {
        // Arrange
        var notifier = new RecordingNotifier();
        using var factory = NewFactory(notifier);
        var member = Guid.CreateVersion7();
        factory.Modules.Projects.OnGet = (_, key) => new ProjectDetail(
            TestData.Project(key), TestData.Blueprint(), [new ProjectMember(member, "editor")], [WorkPermissions.ItemRead]);
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Owner);

        // Act
        using var response = await client.PostAsync(ApplyRoute, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(notifier.AccessChanges, Has.Count.EqualTo(1));
            Assert.That(notifier.AccessChanges[0], Is.EquivalentTo(new[] { member }));
        }
    }
}
