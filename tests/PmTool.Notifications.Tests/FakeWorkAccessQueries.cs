using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Notifications.Tests;

/// <summary>
/// 測試用的存取查詢：誰看得到哪個專案由測試設定。通知模組不自己判斷這件事，所以不需要真的工作引擎。
/// </summary>
internal sealed class FakeWorkAccessQueries : IWorkAccessQueries
{
    private readonly Dictionary<Guid, HashSet<Guid>> _readersByProject = [];

    private readonly HashSet<Guid> _poisonedProjects = [];
    private readonly Dictionary<Guid, Exception> _failingProjects = [];
    private readonly Dictionary<Guid, HashSet<Guid>> _ownOnlyByProject = [];
    private readonly Dictionary<Guid, HashSet<Guid>> _hiddenItemsByPerson = [];
    private int _filterCalls;

    /// <summary>設定了就丟出去，模擬工作引擎出錯。</summary>
    public Exception? FailWith { get; set; }

    /// <summary><see cref="FilterReadersAsync"/> 被呼叫了幾次。</summary>
    public int FilterCalls => Volatile.Read(ref _filterCalls);

    /// <summary>這個專案的查詢一律出錯，模擬一筆處理不了的事件。</summary>
    public void Poison(Guid projectId)
    {
        lock (_poisonedProjects)
        {
            _poisonedProjects.Add(projectId);
        }
    }

    /// <summary>這個專案的查詢一律丟出指定的例外（例如連線逾時這類暫時性的錯誤）。</summary>
    public void FailProject(Guid projectId, Exception exception)
    {
        lock (_poisonedProjects)
        {
            _failingProjects[projectId] = exception;
        }
    }

    /// <summary>讓出錯的專案恢復正常。</summary>
    public void Heal(Guid projectId)
    {
        lock (_poisonedProjects)
        {
            _poisonedProjects.Remove(projectId);
            _failingProjects.Remove(projectId);
        }
    }

    public void Allow(Guid projectId, params Guid[] personIds)
    {
        lock (_readersByProject)
        {
            if (!_readersByProject.TryGetValue(projectId, out var readers))
            {
                _readersByProject[projectId] = readers = [];
            }

            readers.UnionWith(personIds);
        }
    }

    /// <summary>這些人在專案只看得到自己負責或建立的項目（還是專案的讀者）。</summary>
    public void AllowOwnOnly(Guid projectId, params Guid[] personIds)
    {
        Allow(projectId, personIds);
        lock (_readersByProject)
        {
            if (!_ownOnlyByProject.TryGetValue(projectId, out var ownOnly))
            {
                _ownOnlyByProject[projectId] = ownOnly = [];
            }

            ownOnly.UnionWith(personIds);
        }
    }

    /// <summary>這個人現在看不到這個項目（例如負責人換掉了）：收件匣用。</summary>
    public void HideItem(Guid personId, Guid itemId)
    {
        lock (_readersByProject)
        {
            if (!_hiddenItemsByPerson.TryGetValue(personId, out var hidden))
            {
                _hiddenItemsByPerson[personId] = hidden = [];
            }

            hidden.Add(itemId);
        }
    }

    public Task<IReadOnlySet<Guid>> FilterItemReadersAsync(
        Guid organizationId, Guid projectId, ItemOwnership item, IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken)
    {
        var readers = FilterReadersAsync(organizationId, projectId, personIds, cancellationToken).GetAwaiter().GetResult();
        lock (_readersByProject)
        {
            var ownOnly = _ownOnlyByProject.GetValueOrDefault(projectId) ?? [];
            return Task.FromResult<IReadOnlySet<Guid>>(
                readers.Where(id => !ownOnly.Contains(id) || id == item.CreatedBy || id == item.AssigneeId).ToHashSet());
        }
    }

    public Task<IReadOnlySet<Guid>> ReadableItemIdsAsync(Actor actor, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken)
    {
        lock (_readersByProject)
        {
            var hidden = _hiddenItemsByPerson.GetValueOrDefault(actor.PersonId) ?? [];
            return Task.FromResult<IReadOnlySet<Guid>>(itemIds.Where(id => !hidden.Contains(id)).ToHashSet());
        }
    }

    /// <summary>這個人現在只看得到自己項目的專案：<see cref="AllowOwnOnly"/> 設定的。</summary>
    public Task<IReadOnlySet<Guid>> OwnOnlyProjectIdsAsync(Actor actor, CancellationToken cancellationToken)
    {
        lock (_readersByProject)
        {
            return Task.FromResult<IReadOnlySet<Guid>>(
                _ownOnlyByProject.Where(pair => pair.Value.Contains(actor.PersonId)).Select(pair => pair.Key).ToHashSet());
        }
    }

    public void Revoke(Guid projectId, Guid personId)
    {
        lock (_readersByProject)
        {
            _readersByProject.GetValueOrDefault(projectId)?.Remove(personId);
        }
    }

    public Task<Result<ItemReference>> ResolveItemAsync(Actor actor, string itemKey, CancellationToken cancellationToken) =>
        throw new NotSupportedException("通知模組不解析項目編號，那是 API 的事。");

    public Task<IReadOnlySet<Guid>> FilterReadersAsync(
        Guid organizationId, Guid projectId, IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _filterCalls);
        if (FailWith is { } failure)
        {
            throw failure;
        }

        lock (_poisonedProjects)
        {
            if (_failingProjects.TryGetValue(projectId, out var specific))
            {
                throw specific;
            }

            if (_poisonedProjects.Contains(projectId))
            {
                throw new InvalidOperationException($"專案 {projectId} 的查詢出錯（測試設定）。");
            }
        }

        lock (_readersByProject)
        {
            var readers = _readersByProject.GetValueOrDefault(projectId) ?? [];
            return Task.FromResult<IReadOnlySet<Guid>>(personIds.Where(readers.Contains).ToHashSet());
        }
    }

    public Task<IReadOnlySet<Guid>> ReadableProjectIdsAsync(Actor actor, CancellationToken cancellationToken)
    {
        lock (_readersByProject)
        {
            return Task.FromResult<IReadOnlySet<Guid>>(
                _readersByProject.Where(pair => pair.Value.Contains(actor.PersonId)).Select(pair => pair.Key).ToHashSet());
        }
    }
}
