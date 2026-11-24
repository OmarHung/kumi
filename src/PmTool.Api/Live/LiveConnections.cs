using System.Collections.Immutable;

namespace PmTool.Api.Live;

/// <summary>一條登記中的即時連線。群組是目前已經加進去的，重新檢查時拿來和該有的比對。</summary>
public sealed class LiveConnection(
    string connectionId, Guid organizationId, Guid personId, string sessionToken, Action abort)
{
    // 加入與離開群組是跨 await 的，用一個鎖讓同一條連線的調整一次只有一個在做（連線剛建立時，hub 與重新檢查可能同時動它）。
    public SemaphoreSlim GroupLock { get; } = new(1, 1);

    public string ConnectionId { get; } = connectionId;

    public Guid OrganizationId { get; } = organizationId;

    public Guid PersonId { get; } = personId;

    /// <summary>工作階段的權杖，重新檢查時拿它確認工作階段還有效。只在記憶體裡，不寫進記錄。</summary>
    public string SessionToken { get; } = sessionToken;

    public ImmutableHashSet<string> Groups { get; set; } = [];

    public void Abort() => abort();
}

/// <summary>
/// 這個程序裡所有的即時連線，每條連線的人、工作階段與加入的群組。一個人最多 <see cref="MaxPerPerson"/> 條：
/// 每條連線都占一個長時間的請求與一份記憶體，沒有上限的話，一個帳號就能把程序的連線耗光。
/// 登記以連線編號為準，同一條連線登記兩次或移除不存在的連線都無妨。
/// </summary>
public sealed class LiveConnections
{
    public const int MaxPerPerson = 20;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, LiveConnection> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, int> _perPerson = [];

    /// <summary>登記；這個人已經有 <see cref="MaxPerPerson"/> 條就拒絕（回 false）。</summary>
    public bool TryAdd(LiveConnection connection)
    {
        lock (_gate)
        {
            if (_byId.ContainsKey(connection.ConnectionId))
            {
                return true;
            }

            var count = _perPerson.GetValueOrDefault(connection.PersonId);
            if (count >= MaxPerPerson)
            {
                return false;
            }

            _byId[connection.ConnectionId] = connection;
            _perPerson[connection.PersonId] = count + 1;
            return true;
        }
    }

    public void Remove(string connectionId)
    {
        lock (_gate)
        {
            if (!_byId.Remove(connectionId, out var removed))
            {
                return;
            }

            if (_perPerson[removed.PersonId] <= 1)
            {
                _perPerson.Remove(removed.PersonId);
            }
            else
            {
                _perPerson[removed.PersonId]--;
            }
        }
    }

    public int CountOf(Guid personId)
    {
        lock (_gate)
        {
            return _perPerson.GetValueOrDefault(personId);
        }
    }

    /// <summary>所有連線的數量。不複製整個字典，背景工作每一輪都要問。</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _byId.Count;
            }
        }
    }

    public bool IsFull(Guid personId) => CountOf(personId) >= MaxPerPerson;

    /// <summary>目前所有的連線；回傳的是複本，呼叫端可以慢慢處理。</summary>
    public IReadOnlyList<LiveConnection> Snapshot()
    {
        lock (_gate)
        {
            return [.. _byId.Values];
        }
    }
}
