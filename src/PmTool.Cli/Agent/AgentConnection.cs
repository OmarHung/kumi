namespace PmTool.Cli.Agent;

/// <summary>本機代理和伺服器之間的連線狀態；字串會寫進鎖的資訊檔與 <c>kumi agent status --json</c>，是合約。</summary>
internal static class AgentConnectionState
{
    /// <summary>剛啟動，第一個請求還沒有結果（連不上時可能卡在這裡一分多鐘才逾時）。</summary>
    public const string Connecting = "connecting";

    public const string Connected = "connected";

    /// <summary>連不上、伺服器 5xx 或 429：不結束，退避之後一直重試。</summary>
    public const string Retrying = "retrying";
}

/// <param name="Since">進入這個狀態的時間；一直重試時是第一次失敗的時間，才看得出卡了多久。</param>
/// <param name="Error">最近一次失敗的原因；不是重試中時是 null。</param>
internal sealed record AgentConnection(string State, DateTimeOffset Since, string? Error);

/// <summary>
/// 記下連線的變化，交給 <paramref name="write"/>（寫進鎖的資訊檔，給 <c>kumi agent status</c> 讀；回傳有沒有寫成）。
/// 背景程序開機時連不上（例如要 VPN）會一直安靜地重試，沒有這個的話使用者只看得到「執行中」或「沒有」。
/// 狀態沒變就不寫：連得上的時候每一兩秒登記一次，不必每次都動檔案。沒寫成（Windows 上 status 正在讀時換檔會失敗）
/// 就不算寫過，下一次再寫，不然 status 會一直停在舊的狀態。
/// </summary>
internal sealed class ConnectionTracker(Func<AgentConnection, bool> write, Func<DateTimeOffset> now)
{
    /// <summary>原因太長時截斷：資訊檔與 status 只要看得懂原因，不需要整段回應。</summary>
    public const int MaxErrorLength = 300;

    private readonly object _gate = new();
    private AgentConnection? _current;
    private AgentConnection? _written;

    public void Connecting() => Update(_ => new AgentConnection(AgentConnectionState.Connecting, now(), null));

    public void Failed(string error)
    {
        var reason = error.Length > MaxErrorLength ? error[..(MaxErrorLength - 1)] + "…" : error;
        Update(current => current is { State: AgentConnectionState.Retrying }
            ? current with { Error = reason }
            : new AgentConnection(AgentConnectionState.Retrying, now(), reason));
    }

    public void Succeeded() => Update(current => current is { State: AgentConnectionState.Connected }
        ? current
        : new AgentConnection(AgentConnectionState.Connected, now(), null));

    private void Update(Func<AgentConnection?, AgentConnection> next)
    {
        lock (_gate)
        {
            _current = next(_current);
            if (_current == _written)
            {
                return;
            }

            if (write(_current))
            {
                _written = _current;
            }
        }
    }
}
