using PmTool.Abstractions;

namespace PmTool.Ai.Tests.Proposals;

/// <summary>
/// 六種建議的服務的記憶體版：只做讀與作廢（撤回與建議現況只用這兩個）。每份建議記著是誰的，別人讀、作廢一律找不到；
/// 不是等待中的作廢回「已經結束」，和真的服務一樣。<see cref="Reads"/> 記下每次讀，測試用來確認查詢次數。
/// </summary>
internal sealed class FakeProposalServices :
    IItemProposalService, IItemBatchProposalService, INewItemProposalService, IItemDeletionProposalService, IMemberProposalService, IProjectProposalService
{
    private readonly Dictionary<Guid, Guid> _owners = [];
    private readonly List<Guid> _reads = [];

    public Dictionary<Guid, ItemProposal> Items { get; } = [];

    public Dictionary<Guid, ItemBatchProposal> Batches { get; } = [];

    public Dictionary<Guid, NewItemProposal> NewItems { get; } = [];

    public Dictionary<Guid, ItemDeletionProposal> Deletions { get; } = [];

    public Dictionary<Guid, MemberProposal> Members { get; } = [];

    public Dictionary<Guid, ProjectProposal> Projects { get; } = [];

    public IReadOnlyList<Guid> Reads => _reads;

    /// <summary>讀建議時丟例外（模擬服務壞了）。</summary>
    public bool FailReads { get; set; }

    public Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.UnixEpoch;

    public Guid Add(Guid ownerPersonId, ItemProposal proposal) => Track(ownerPersonId, proposal.Id, () => Items[proposal.Id] = proposal);

    public Guid Add(Guid ownerPersonId, ItemBatchProposal proposal) => Track(ownerPersonId, proposal.Id, () => Batches[proposal.Id] = proposal);

    public Guid Add(Guid ownerPersonId, NewItemProposal proposal) => Track(ownerPersonId, proposal.Id, () => NewItems[proposal.Id] = proposal);

    public Guid Add(Guid ownerPersonId, ItemDeletionProposal proposal) => Track(ownerPersonId, proposal.Id, () => Deletions[proposal.Id] = proposal);

    public Guid Add(Guid ownerPersonId, MemberProposal proposal) => Track(ownerPersonId, proposal.Id, () => Members[proposal.Id] = proposal);

    public Guid Add(Guid ownerPersonId, ProjectProposal proposal) => Track(ownerPersonId, proposal.Id, () => Projects[proposal.Id] = proposal);

    private Guid Track(Guid owner, Guid id, Action store)
    {
        _owners[id] = owner;
        store();
        return id;
    }

    Task<Result<ItemProposal>> IItemProposalService.GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Read(Items, actor, proposalId, AiErrorCodes.ItemProposalNotFound);

    Task<Result<ItemProposal>> IItemProposalService.ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<Result<ItemProposal>> IItemProposalService.DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Dismiss(Items, actor, proposalId, p => p.Status == ItemProposalStatus.Pending, p => p with { Status = ItemProposalStatus.Dismissed, DecidedAt = Now() },
            AiErrorCodes.ItemProposalNotFound, AiErrorCodes.ItemProposalClosed);

    Task<Result<ItemBatchProposal>> IItemBatchProposalService.GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Read(Batches, actor, proposalId, AiErrorCodes.ItemBatchProposalNotFound);

    Task<Result<ItemBatchProposal>> IItemBatchProposalService.ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<Result<ItemBatchProposal>> IItemBatchProposalService.DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Dismiss(Batches, actor, proposalId, p => p.Status == ItemProposalStatus.Pending, p => p with { Status = ItemProposalStatus.Dismissed, DecidedAt = Now() },
            AiErrorCodes.ItemBatchProposalNotFound, AiErrorCodes.ItemBatchProposalClosed);

    Task<Result<NewItemProposal>> INewItemProposalService.GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Read(NewItems, actor, proposalId, AiErrorCodes.NewItemProposalNotFound);

    Task<Result<NewItemProposal>> INewItemProposalService.ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<Result<NewItemProposal>> INewItemProposalService.DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Dismiss(NewItems, actor, proposalId, p => p.Status == NewItemProposalStatus.Pending, p => p with { Status = NewItemProposalStatus.Dismissed, DecidedAt = Now() },
            AiErrorCodes.NewItemProposalNotFound, AiErrorCodes.NewItemProposalClosed);

    Task<Result<ItemDeletionProposal>> IItemDeletionProposalService.GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Read(Deletions, actor, proposalId, AiErrorCodes.ItemDeletionProposalNotFound);

    Task<Result<ItemDeletionProposal>> IItemDeletionProposalService.ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<Result<ItemDeletionProposal>> IItemDeletionProposalService.DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Dismiss(Deletions, actor, proposalId, p => p.Status == ItemProposalStatus.Pending, p => p with { Status = ItemProposalStatus.Dismissed, DecidedAt = Now() },
            AiErrorCodes.ItemDeletionProposalNotFound, AiErrorCodes.ItemDeletionProposalClosed);

    Task<Result<MemberProposal>> IMemberProposalService.GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Read(Members, actor, proposalId, AiErrorCodes.MemberProposalNotFound);

    Task<Result<MemberProposal>> IMemberProposalService.ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<Result<MemberProposal>> IMemberProposalService.DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Dismiss(Members, actor, proposalId, p => p.Status == MemberProposalStatus.Pending, p => p with { Status = MemberProposalStatus.Dismissed, DecidedAt = Now() },
            AiErrorCodes.MemberProposalNotFound, AiErrorCodes.MemberProposalClosed);

    Task<Result<ProjectProposal>> IProjectProposalService.GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Read(Projects, actor, proposalId, AiErrorCodes.ProjectProposalNotFound);

    Task<Result<ProjectProposal>> IProjectProposalService.ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<Result<ProjectProposal>> IProjectProposalService.DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Dismiss(Projects, actor, proposalId, p => p.Status == ProjectProposalStatus.Pending, p => p with { Status = ProjectProposalStatus.Dismissed, DecidedAt = Now() },
            AiErrorCodes.ProjectProposalNotFound, AiErrorCodes.ProjectProposalClosed);

    private Task<Result<T>> Read<T>(Dictionary<Guid, T> store, Actor actor, Guid id, string notFoundCode)
    {
        if (FailReads)
        {
            throw new InvalidOperationException("服務壞了。");
        }

        _reads.Add(id);
        Result<T> result = IsOwner(actor, id) && store.TryGetValue(id, out var found) ? found : NotFound<T>(notFoundCode);
        return Task.FromResult(result);
    }

    private Task<Result<T>> Dismiss<T>(
        Dictionary<Guid, T> store, Actor actor, Guid id, Func<T, bool> isPending, Func<T, T> dismiss, string notFoundCode, string closedCode)
    {
        if (!IsOwner(actor, id) || !store.TryGetValue(id, out var found))
        {
            return Task.FromResult(NotFound<T>(notFoundCode));
        }

        if (!isPending(found))
        {
            return Task.FromResult<Result<T>>(new Failure(FailureKind.Conflict, closedCode, "這份建議已經結束了。"));
        }

        return Task.FromResult<Result<T>>(store[id] = dismiss(found));
    }

    private bool IsOwner(Actor actor, Guid id) => _owners.TryGetValue(id, out var owner) && owner == actor.PersonId;

    private static Result<T> NotFound<T>(string code) => new Failure(FailureKind.NotFound, code, "找不到這份建議。");
}
