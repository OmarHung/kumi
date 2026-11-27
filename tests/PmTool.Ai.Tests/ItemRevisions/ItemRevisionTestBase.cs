using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.ItemRevisions;

/// <summary>批次刪除的假物件：記下每次呼叫，結果由測試設定（預設全部刪除成功）。</summary>
internal sealed class FakeBulkItemService : IBulkItemService
{
    private readonly List<(Actor Actor, string ProjectKey, IReadOnlyList<string> Keys)> _deletes = [];

    public Func<IReadOnlyList<string>, Result<BulkDeleteResult>>? OnDelete { get; set; }

    public IReadOnlyList<(Actor Actor, string ProjectKey, IReadOnlyList<string> Keys)> Deletes => _deletes;

    public Task<Result<BulkDeleteResult>> DeleteAsync(
        Actor actor, string projectKey, IReadOnlyList<string> itemKeys, CancellationToken cancellationToken)
    {
        _deletes.Add((actor, projectKey, itemKeys));
        return Task.FromResult(OnDelete?.Invoke(itemKeys)
            ?? new BulkDeleteResult([.. itemKeys.Select(key => new DeletedWorkItem(key, DateTimeOffset.UnixEpoch))], []));
    }

    public Task<Result<BulkUpdateResult>> UpdateAsync(Actor actor, string projectKey, BulkItemUpdate update, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<BulkRestoreResult>> RestoreAsync(
        Actor actor, string projectKey, IReadOnlyList<string> itemKeys, CancellationToken cancellationToken) => throw new NotSupportedException();
}

/// <summary>刪除與批次修改建議測試的共同環境：助理的環境加上一個有成員、有刪除權限的專案與幾個項目。</summary>
public abstract class ItemRevisionTestBase
{
    private AssistantHarness _h = null!;

    internal AssistantHarness H => _h;

    internal FakeBulkItemService Bulk => (FakeBulkItemService)_h.Host.Get<IBulkItemService>();

    protected Actor Me => _h.Actor;

    protected static readonly string OldRevision = DescriptionRevisions.Of("舊的說明");

    [SetUp]
    public async Task StartAsync()
    {
        _h = await AssistantHarness.StartAsync(configure: services => services.AddSingleton<IBulkItemService>(new FakeBulkItemService()));
        _h.Projects.OnGet = (_, _) => Detail();
        _h.WorkItems.Items["WEB-13"] = ItemSamples.Item("WEB-13", "第二個任務");
        _h.WorkItems.Items["WEB-14"] = ItemSamples.Item("WEB-14", "第三個任務");
    }

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    protected static ProjectDetail Detail(IReadOnlyList<string>? permissions = null) =>
        ItemSamples.DetailWithMember(permissions ?? [.. ItemSamples.EditorPermissions, WorkPermissions.ItemDelete]);

    protected static ItemFieldChange Change(string field, object? value) =>
        new(field, JsonSerializer.SerializeToElement(value));

    protected static string IssueAt(Failure? failure, string path) =>
        failure!.Details.Single(d => d.Path == path).Message;
}
