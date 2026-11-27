using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.ItemRestoreProposals;

/// <summary>已刪除項目的假清單：一頁幾筆由測試決定（預設全部一頁），游標是下一頁的起點；其餘一律委託給助理測試用的假工作引擎。</summary>
internal sealed class FakeDeletedItems(FakeWorkItemService inner) : IWorkItemService
{
    public List<DeletedItemSummary> Deleted { get; } = [];

    public int PageSize { get; set; } = int.MaxValue;

    public int ListCalls { get; private set; }

    public Task<Result<DeletedItemPage>> ListDeletedAsync(Actor actor, string projectKey, DeletedItemQuery query, CancellationToken cancellationToken)
    {
        ListCalls++;
        var start = query.Cursor is null ? 0 : int.Parse(query.Cursor, System.Globalization.CultureInfo.InvariantCulture);
        var page = Deleted.Skip(start).Take(Math.Min(PageSize, query.Limit)).ToList();
        var next = start + page.Count;
        return Task.FromResult<Result<DeletedItemPage>>(
            new DeletedItemPage(page, next < Deleted.Count ? next.ToString(System.Globalization.CultureInfo.InvariantCulture) : null));
    }

    public Task<Result<WorkItem>> CreateAsync(Actor actor, string projectKey, NewWorkItem item, CancellationToken cancellationToken) =>
        inner.CreateAsync(actor, projectKey, item, cancellationToken);

    public Task<Result<WorkItem>> GetAsync(Actor actor, string itemKey, CancellationToken cancellationToken) => inner.GetAsync(actor, itemKey, cancellationToken);

    public Task<Result<WorkItem>> UpdateAsync(Actor actor, string itemKey, WorkItemPatch patch, CancellationToken cancellationToken) =>
        inner.UpdateAsync(actor, itemKey, patch, cancellationToken);

    public Task<Result<WorkItem>> TransitionAsync(Actor actor, string itemKey, string toState, CancellationToken cancellationToken) =>
        inner.TransitionAsync(actor, itemKey, toState, cancellationToken);

    public Task<Result<DeletedWorkItem>> DeleteAsync(Actor actor, string itemKey, CancellationToken cancellationToken) =>
        inner.DeleteAsync(actor, itemKey, cancellationToken);

    public Task<Result<WorkItem>> RestoreAsync(Actor actor, string itemKey, CancellationToken cancellationToken) =>
        inner.RestoreAsync(actor, itemKey, cancellationToken);

    public Task<Result<WorkItem>> ChangeTypeAsync(Actor actor, string itemKey, string type, CancellationToken cancellationToken) =>
        inner.ChangeTypeAsync(actor, itemKey, type, cancellationToken);

    public Task<Result<WorkItemPage>> ListAsync(Actor actor, string projectKey, WorkItemQuery query, CancellationToken cancellationToken) =>
        inner.ListAsync(actor, projectKey, query, cancellationToken);

    public Task<IReadOnlyList<WorkItem>> ListAssignedToMeAsync(Actor actor, CancellationToken cancellationToken) =>
        inner.ListAssignedToMeAsync(actor, cancellationToken);

    public Task<Result<WorkItemPage>> ListAcrossProjectsAsync(
        Actor actor, CrossProjectScope scope, WorkItemQuery query, CancellationToken cancellationToken) =>
        inner.ListAcrossProjectsAsync(actor, scope, query, cancellationToken);

    public Task<IReadOnlyList<ItemTypeSummary>> ListItemTypesAsync(Actor actor, CancellationToken cancellationToken) =>
        inner.ListItemTypesAsync(actor, cancellationToken);

    public Task<Result<IReadOnlyList<WorkItem>>> SearchAsync(Actor actor, string text, int limit, CancellationToken cancellationToken) =>
        inner.SearchAsync(actor, text, limit, cancellationToken);
}

/// <summary>批次復原的假物件：記下每次呼叫，結果由測試設定（預設全部復原成功）。</summary>
internal sealed class FakeRestoreBulk : IBulkItemService
{
    private readonly List<(Actor Actor, string ProjectKey, IReadOnlyList<string> Keys)> _restores = [];

    public Func<IReadOnlyList<string>, Result<BulkRestoreResult>>? OnRestore { get; set; }

    public IReadOnlyList<(Actor Actor, string ProjectKey, IReadOnlyList<string> Keys)> Restores => _restores;

    public Task<Result<BulkRestoreResult>> RestoreAsync(Actor actor, string projectKey, IReadOnlyList<string> itemKeys, CancellationToken cancellationToken)
    {
        _restores.Add((actor, projectKey, itemKeys));
        return Task.FromResult(OnRestore?.Invoke(itemKeys) ?? new BulkRestoreResult([.. itemKeys.Select(key => ItemSamples.Item(key))], []));
    }

    public Task<Result<BulkUpdateResult>> UpdateAsync(Actor actor, string projectKey, BulkItemUpdate update, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<BulkDeleteResult>> DeleteAsync(Actor actor, string projectKey, IReadOnlyList<string> itemKeys, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

/// <summary>復原建議測試的共同環境：助理的環境，加上已刪除項目的假清單與有記錄的批次復原；預設的使用者有刪除權限。</summary>
public abstract class RestoreTestBase
{
    private AssistantHarness _h = null!;

    internal AssistantHarness H => _h;

    internal FakeDeletedItems Deleted { get; private set; } = null!;

    internal FakeRestoreBulk Bulk { get; private set; } = null!;

    protected Actor Me => _h.Actor;

    internal IItemRestoreProposalCreator Creator => _h.Host.Get<IItemRestoreProposalCreator>();

    internal IItemRestoreProposalService Proposals => _h.Host.Get<IItemRestoreProposalService>();

    [SetUp]
    public async Task StartAsync()
    {
        Deleted = new FakeDeletedItems(new FakeWorkItemService());
        Bulk = new FakeRestoreBulk();
        _h = await AssistantHarness.StartAsync(configure: services =>
        {
            services.AddSingleton<IWorkItemService>(Deleted);
            services.AddSingleton<IBulkItemService>(Bulk);
        });
        Permissions(canDelete: true);
    }

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    protected void Permissions(bool canDelete)
    {
        var perms = new List<string>(WorkSamples.AllPermissions);
        if (canDelete)
        {
            perms.Add(WorkPermissions.ItemDelete);
        }

        _h.Projects.OnGet = (_, _) => WorkSamples.Detail(perms);
    }

    protected DeletedItemSummary AddDeleted(string key, string title = "已刪除的任務", string? parentKey = null, int daysAgo = 1)
    {
        var item = new DeletedItemSummary(
            key, "WEB", long.Parse(key[(key.IndexOf('-') + 1)..], System.Globalization.CultureInfo.InvariantCulture), "task", title, "todo",
            PmTool.Blueprint.Model.StateCategory.NotStarted, parentKey, Guid.NewGuid(), DateTimeOffset.UnixEpoch.AddDays(-daysAgo));
        Deleted.Deleted.Add(item);
        return item;
    }

    protected async Task<Result<ItemRestoreProposal>> CreateAsync(string? reason, params string[] keys) =>
        await Creator.CreateAsync(Me, new ItemRestoreProposalDraft("WEB", keys) { Reason = reason }, CancellationToken.None);

    protected async Task<ItemRestoreProposal> CreatedAsync(params string[] keys)
    {
        var created = await CreateAsync(null, keys);
        Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
        return created.Value;
    }

    protected static string IssueAt(Failure? failure, string path) => failure!.Details.Single(d => d.Path == path).Message;
}
