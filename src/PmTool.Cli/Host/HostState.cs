using System.Text.Json;
using System.Text.Json.Serialization;
using PmTool.Cli.Settings;

namespace PmTool.Cli.Host;

/// <summary>某個引擎在 Kumi 完成的那一次登入：編號（每次登記帶著）與當時登入帳號的指紋（見 <see cref="HostEngineProbe"/>）。</summary>
internal sealed record HostLoginRecord(Guid LoginId, string? Fingerprint);

/// <summary>
/// worker 的狀態檔內容。<see cref="Credential"/>（機器憑證）是機密：不印、不進記錄、不進引擎子程序的環境，
/// 記錄與測試失敗的輸出都會呼叫 ToString，所以遮起來。
/// </summary>
internal sealed record HostState
{
    public string? Server { get; init; }

    public Guid? AgentId { get; init; }

    public string? AgentName { get; init; }

    /// <summary>第一次產生後一直沿用（重新配對也不換）。</summary>
    public string? MachineKey { get; init; }

    public string? Credential { get; init; }

    /// <summary>引擎種類（claude_code、codex）→ 目前的登入。</summary>
    public IReadOnlyDictionary<string, HostLoginRecord> Logins { get; init; } = new Dictionary<string, HostLoginRecord>();

    [JsonIgnore]
    public bool IsPaired => !string.IsNullOrEmpty(Server) && !string.IsNullOrEmpty(Credential) && AgentId is not null;

    public HostState WithLogin(string kind, HostLoginRecord? login)
    {
        var logins = new Dictionary<string, HostLoginRecord>(Logins);
        if (login is null)
        {
            logins.Remove(kind);
        }
        else
        {
            logins[kind] = login;
        }

        return this with { Logins = logins };
    }

    public override string ToString() => $"HostState {{ Server = {Server}, AgentId = {AgentId}, Credential = *** }}";
}

/// <summary>
/// 狀態檔 <c>host-agent.json</c>：資料夾是 <c>KUMI_HOST_STATE_DIR</c>（預設 <c>~/.local/state/kumi-host/</c>），權限 700、檔案 600，
/// 先寫暫存檔再換名。<c>run</c>（服務）與 <c>pair</c>／<c>login</c>（在同一個容器裡另外執行）會同時碰它，所以每次改都重讀再寫。
/// </summary>
internal sealed class HostStateStore(string directory)
{
    public const string FileName = "host-agent.json";
    public const string DirectoryVariable = "KUMI_HOST_STATE_DIR";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // 同一個程序裡的寫入排隊；不同程序（run 與 login）之間靠「每次重讀再寫」與換名的原子性，衝突的窗口只有幾毫秒。
    private readonly object _gate = new();

    public string Path { get; } = System.IO.Path.Combine(directory, FileName);

    public static HostStateStore FromEnvironment(Func<string, string?> environment)
    {
        var configured = environment(DirectoryVariable);
        var path = string.IsNullOrWhiteSpace(configured)
            ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state", "kumi-host")
            : configured;
        return new HostStateStore(path);
    }

    /// <summary>檔案不存在是空的狀態；讀不懂丟 <see cref="CliException"/>（服務遇到時記一行、等人處理，不結束）。</summary>
    public HostState Load()
    {
        if (!File.Exists(Path))
        {
            return new HostState();
        }

        try
        {
            return JsonSerializer.Deserialize<HostState>(File.ReadAllText(Path), Options) ?? new HostState();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new CliException(ExitCodes.Error, $"狀態檔 {Path} 讀不懂或讀不到。要重新配對的話，刪掉它再執行 kumi host pair。");
        }
    }

    /// <summary>重讀、套用、寫回；回寫入後的狀態。</summary>
    public HostState Update(Func<HostState, HostState> change)
    {
        lock (_gate)
        {
            var next = change(Load());
            ConfigStore.WriteAtomically(Path, JsonSerializer.Serialize(next, Options));
            return next;
        }
    }

    /// <summary>機器編號第一次用到時產生並存起來，之後一直沿用。</summary>
    public string EnsureMachineKey() =>
        Update(state => state.MachineKey is { Length: > 0 } ? state : state with { MachineKey = Guid.NewGuid().ToString("N") }).MachineKey!;
}
