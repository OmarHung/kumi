using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace PmTool.Api.Live;

/// <summary>一條登記中的即時連線。群組是目前已經加進去的，重新檢查時拿來和該有的比對。</summary>
public sealed class LiveConnection(
    string connectionId, Guid organizationId, Guid personId, Guid accountId, string sessionToken, Action abort)
{
    /// <summary>每條連線最多排幾批還沒寫的訊息；超過就略過。</summary>
    public const int MaxBacklog = 8;

    private readonly Lock _sendGate = new();
    private readonly Queue<IReadOnlyList<LiveDelivery>> _backlog = new();
    private bool _draining;
    private int _aborted;
    // 加入與離開群組是跨 await 的，用一個鎖讓同一條連線的調整一次只有一個在做（連線剛建立時，hub 與重新檢查可能同時動它）。
    public SemaphoreSlim GroupLock { get; } = new(1, 1);

    public string ConnectionId { get; } = connectionId;

    public Guid OrganizationId { get; } = organizationId;

    public Guid PersonId { get; } = personId;

    /// <summary>登入的帳號。作廢憑證的訊號只帶帳號，靠它找出受影響的連線。</summary>
    public Guid AccountId { get; } = accountId;

    /// <summary>工作階段的權杖，重新檢查時拿它確認工作階段還有效。只在記憶體裡，不寫進記錄。</summary>
    public string SessionToken { get; } = sessionToken;

    public ImmutableHashSet<string> Groups { get; set; } = [];

    /// <summary>目前送出的工作（逐則寫出排著的訊息）。還沒完成就表示對方沒在讀（或很慢）。</summary>
    public Task? PendingSend { get; private set; }

    /// <summary>目前正在寫的那一則是什麼時候開始寫的；一直沒前進就表示這條連線卡住了。</summary>
    public DateTimeOffset? SendingSince { get; private set; }

    /// <summary>排隊的訊息塞滿過、有的被略過了：網頁漏了推送，只能靠斷開重連來補。</summary>
    public bool IsBacklogged { get; private set; }

    public void Abort() => abort();

    /// <summary>
    /// 第一次叫用回 true，之後都是 false：斷開要走完整個流程才會從登記裡拿掉，
    /// 這段時間裡每一輪都會再看到它，不能每次都記一筆警告、再斷開一次。
    /// </summary>
    public bool TryMarkAborted() => Interlocked.Exchange(ref _aborted, 1) == 0;

    /// <summary>
    /// 交一批訊息給這條連線。沒有在送：啟動送出（<paramref name="startDrain"/>）並回 <see cref="SendStart.Started"/>；
    /// 上一批還在送：接在它後面（最多 <see cref="MaxBacklog"/> 批，健康的連線只是一瞬間的重疊，不能丟掉後到的）回 <see cref="SendStart.Queued"/>；
    /// 排滿了：略過這一批並標成送不完，正在寫的那一則超過 <paramref name="timeout"/> 就回 <see cref="SendStart.TimedOut"/>（呼叫端要斷開它），否則回 <see cref="SendStart.Busy"/>。
    /// </summary>
    public SendStart TryEnqueue(IReadOnlyList<LiveDelivery> batch, Func<Task> startDrain, TimeSpan timeout, DateTimeOffset now, out Task? pending)
    {
        lock (_sendGate)
        {
            pending = null;
            if (_draining)
            {
                if (_backlog.Count < MaxBacklog)
                {
                    _backlog.Enqueue(batch);
                    return SendStart.Queued;
                }

                IsBacklogged = true;
                return now - SendingSince >= timeout ? SendStart.TimedOut : SendStart.Busy;
            }

            _draining = true;
            _backlog.Enqueue(batch);
            SendingSince = now;
            PendingSend = pending = startDrain();
            return SendStart.Started;
        }
    }

    /// <summary>送出的工作拿下一批；沒有了就結束送出（和排隊在同一個鎖裡，不會漏掉剛排進來的）。</summary>
    public bool TryTakeNext([NotNullWhen(true)] out IReadOnlyList<LiveDelivery>? batch)
    {
        lock (_sendGate)
        {
            if (_backlog.TryDequeue(out batch))
            {
                return true;
            }

            _draining = false;
            return false;
        }
    }

    /// <summary>開始寫一則。</summary>
    public void MarkWriting(DateTimeOffset now)
    {
        lock (_sendGate)
        {
            SendingSince = now;
        }
    }

    /// <summary>送出失敗（連線多半已經關了）：排著的不送了，之後有新的再重新開始。</summary>
    public void AbandonBacklog()
    {
        lock (_sendGate)
        {
            _backlog.Clear();
            _draining = false;
        }
    }
}

public enum SendStart { Started, Queued, Busy, TimedOut }

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

    /// <summary>目前加進這個群組的連線；回傳的是複本。</summary>
    public IReadOnlyList<LiveConnection> InGroup(string group)
    {
        lock (_gate)
        {
            return [.. _byId.Values.Where(c => c.Groups.Contains(group))];
        }
    }

    /// <summary>目前所有的連線；回傳的是複本，呼叫端可以慢慢處理。</summary>
    public IReadOnlyList<LiveConnection> Snapshot()
    {
        lock (_gate)
        {
            return [.. _byId.Values];
        }
    }
}
