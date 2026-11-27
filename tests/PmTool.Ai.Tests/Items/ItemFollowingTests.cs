using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Notifications;
using PmTool.Work;

namespace PmTool.Ai.Tests.Items;

/// <summary>修改建議的 following 欄位：建立時的檢查與權限、套用時在留言之後呼叫通知模組、失敗的處理。</summary>
[TestFixture]
public sealed class ItemFollowingTests
{
    private static readonly Guid ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ItemId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private AssistantHarness _h = null!;
    private RecordingSubscriptions _subscriptions = null!;
    private Actor Me => _h.Actor;

    [SetUp]
    public async Task StartAsync()
    {
        _subscriptions = new RecordingSubscriptions();
        _h = await AssistantHarness.StartAsync(configure: services => services
            .AddSingleton<IItemSubscriptionService>(_subscriptions)
            .AddSingleton<IWorkAccessQueries>(new ResolvingAccess()));
        _h.Projects.OnGet = (_, _) => ItemSamples.DetailWithMember();
    }

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    private static ItemFieldChange Change(string field, object? value) => new(field, JsonSerializer.SerializeToElement(value));

    private Task<Result<ItemProposal>> CreateAsync(IReadOnlyList<ItemFieldChange> changes, string? comment = null) =>
        _h.Host.Get<IItemProposalCreator>().CreateAsync(Me, new ItemProposalDraft("WEB", "WEB-12", changes, comment), CancellationToken.None);

    private async Task<ItemProposal> CreatedAsync(IReadOnlyList<ItemFieldChange> changes, string? comment = null)
    {
        var created = await CreateAsync(changes, comment);
        Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message + string.Join("；", created.Failure?.Details.Select(d => d.Message) ?? []));
        return created.Value;
    }

    private Task<Result<ItemProposal>> ApplyAsync(Guid id) => _h.Host.Get<IItemProposalService>().ApplyAsync(Me, id, CancellationToken.None);

    [Test]
    public async Task Create_FollowingMustBeABoolean()
    {
        // Act
        var result = await CreateAsync([Change("following", "yes")]);

        // Assert
        Assert.That(result.Failure?.Details.Single().Message, Does.Contain("true").And.Contain("false"));
    }

    [Test]
    public async Task Create_OnlyFollowing_NeedsNoUpdatePermissionAndNeedsNoOtherChange()
    {
        // Arrange
        _h.Projects.OnGet = (_, _) => ItemSamples.DetailWithMember();

        // Act
        var proposal = await CreatedAsync([Change("following", true)]);

        // Assert
        Assert.That(proposal.Changes.Single().Field, Is.EqualTo("following"));
    }

    [Test]
    public async Task Apply_FollowingAfterTheCommentWithTheResolvedProjectAndItemIds()
    {
        // Arrange
        var proposal = await CreatedAsync([Change("title", "新標題"), Change("following", true)], "我來跟");

        // Act
        var applied = await ApplyAsync(proposal.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Value.Status, Is.EqualTo(ItemProposalStatus.Applied));
            Assert.That(_h.WorkItems.Updates.Single().Patch.Fields, Is.Empty, "following 不是項目的欄位，不進 patch");
            Assert.That(_h.Activity.Comments.Single().Item3, Is.EqualTo("我來跟"));
            Assert.That(_subscriptions.Calls.Single(), Is.EqualTo((Me, ProjectId, ItemId, true)));
        }
    }

    [Test]
    public async Task Apply_OnlyUnfollowing_CallsTheSubscriptionAndNothingElse()
    {
        // Arrange
        var proposal = await CreatedAsync([Change("following", false)]);

        // Act
        var applied = await ApplyAsync(proposal.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Value.Status, Is.EqualTo(ItemProposalStatus.Applied));
            Assert.That(_h.WorkItems.Updates, Is.Empty);
            Assert.That(_subscriptions.Calls.Single().Following, Is.False);
        }
    }

    [Test]
    public async Task Apply_WhenFollowingIsTheFirstStepAndIsRefused_StaysPendingAndReturnsTheFailure()
    {
        // Arrange
        var proposal = await CreatedAsync([Change("following", true)]);
        _subscriptions.Failure = new Failure(FailureKind.Conflict, NotificationErrorCodes.NotProjectMember, "不是專案成員。");

        // Act
        var applied = await ApplyAsync(proposal.Id);
        var read = await _h.Host.Get<IItemProposalService>().GetAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Failure?.Code, Is.EqualTo(NotificationErrorCodes.NotProjectMember));
            Assert.That(read.Value.Status, Is.EqualTo(ItemProposalStatus.Pending));
        }
    }

    [Test]
    public async Task Apply_WhenFollowingIsRefusedAfterTheFieldsWereWritten_ReportsItAsNotApplied()
    {
        // Arrange
        var proposal = await CreatedAsync([Change("title", "新標題"), Change("following", true)]);
        _subscriptions.Failure = new Failure(FailureKind.Conflict, NotificationErrorCodes.NotProjectMember, "不是專案成員。");

        // Act
        var applied = await ApplyAsync(proposal.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Value.Status, Is.EqualTo(ItemProposalStatus.Applied));
            Assert.That(applied.Value.Error, Does.Contain("關注"));
        }
    }

    private sealed class RecordingSubscriptions : IItemSubscriptionService
    {
        public Failure? Failure { get; set; }

        public List<(Actor Actor, Guid ProjectId, Guid ItemId, bool Following)> Calls { get; } = [];

        public Task<ItemSubscription> GetAsync(Actor actor, Guid projectId, Guid itemId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<ItemSubscription>> SetAsync(Actor actor, Guid projectId, Guid itemId, bool following, CancellationToken cancellationToken)
        {
            Calls.Add((actor, projectId, itemId, following));
            return Task.FromResult<Result<ItemSubscription>>(
                Failure is { } failure ? failure : new ItemSubscription(following, SubscriptionReason.Manual, CanFollow: true));
        }
    }

    private sealed class ResolvingAccess : IWorkAccessQueries
    {
        public Task<Result<ItemReference>> ResolveItemAsync(Actor actor, string itemKey, CancellationToken cancellationToken) =>
            Task.FromResult<Result<ItemReference>>(new ItemReference(ProjectId, "WEB", ItemId, itemKey, "標題"));

        public Task<IReadOnlySet<Guid>> FilterReadersAsync(Guid organizationId, Guid projectId, IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlySet<Guid>> ReadableProjectIdsAsync(Actor actor, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlySet<Guid>> FilterItemReadersAsync(
            Guid organizationId, Guid projectId, ItemOwnership item, IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlySet<Guid>> ReadableItemIdsAsync(Actor actor, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlySet<Guid>> OwnOnlyProjectIdsAsync(Actor actor, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, ProjectAccessSummary>> VisibleProjectsAsync(Actor actor, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProjectAccessSummary?> FindVisibleProjectAsync(Actor actor, string projectKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, ProjectAccessSummary>> FindVisibleProjectsAsync(
            Actor actor, IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
