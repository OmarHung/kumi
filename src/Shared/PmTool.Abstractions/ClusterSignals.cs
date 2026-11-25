using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace PmTool.Abstractions;

/// <summary>跨程序要通知對方的事：憑證作廢（快取要清）、權限變動（即時連線要重算）、收件匣有新東西（要推給連線）。</summary>
public enum ClusterSignalKind { CredentialsRevoked, AccessChanged, InboxChanged }

public sealed record ClusterSignal(ClusterSignalKind Kind)
{
    /// <summary>CredentialsRevoked 的帳號。</summary>
    public Guid? AccountId { get; init; }

    public IReadOnlyList<Guid> PersonIds { get; init; } = [];

    /// <summary>AccessChanged 的「所有人」。</summary>
    public bool AllPeople { get; init; }

    /// <summary>匯流排填入的發送者程序編號；接收端用它略過自己送的。</summary>
    public Guid Origin { get; init; }
}

/// <summary>不等、不丟例外。這個程序裡的接收端也會收到（本機分送），再送給其他程序。</summary>
public interface IClusterSignals
{
    void Publish(ClusterSignal signal);
}

/// <summary>沒有匯流排時的實作：什麼都不做。</summary>
public sealed class NullClusterSignals : IClusterSignals
{
    public static readonly NullClusterSignals Instance = new();

    public void Publish(ClusterSignal signal)
    {
    }
}

public interface IClusterSignalHandler
{
    Task HandleAsync(ClusterSignal signal, CancellationToken cancellationToken);

    /// <summary>接收的連線斷過又接上：中間的訊號可能漏了，把自己快取的整個作廢。</summary>
    Task ResynchronizeAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 訊號在 PostgreSQL LISTEN/NOTIFY 上的文字格式（第 1 版，snake_case 的 JSON）。
/// NOTIFY 的內容上限約 8000 位元組，所以一則最多帶 <see cref="MaxPersonIds"/> 個人。
/// </summary>
public static class ClusterSignalText
{
    public const string Channel = "pmtool_cluster";

    public const int MaxPersonIds = 150;

    private const int Version = 1;

    /// <summary>
    /// 一則訊號的文字。人數超過 <see cref="MaxPersonIds"/> 時：AccessChanged 改成「所有人」（多算一次沒有壞處）；
    /// InboxChanged 沒辦法放進一則，丟 <see cref="ArgumentException"/>，要用 <see cref="SerializeAll"/>。
    /// </summary>
    public static string Serialize(ClusterSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (signal.PersonIds.Count > MaxPersonIds && signal.Kind == ClusterSignalKind.InboxChanged)
        {
            throw new ArgumentException($"一則訊號最多 {MaxPersonIds} 個人，請改用 SerializeAll。", nameof(signal));
        }

        return Write(signal);
    }

    /// <summary>可能是好幾則：InboxChanged 的人數超過上限時，每 <see cref="MaxPersonIds"/> 個人一則；其他種類永遠一則。</summary>
    public static IReadOnlyList<string> SerializeAll(ClusterSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (signal.Kind != ClusterSignalKind.InboxChanged || signal.PersonIds.Count <= MaxPersonIds)
        {
            return [Write(signal)];
        }

        return signal.PersonIds
            .Chunk(MaxPersonIds)
            .Select(chunk => Write(signal with { PersonIds = chunk }))
            .ToList();
    }

    /// <summary>壞的 JSON、不認得的版本或種類、缺少必要的欄位、不是 GUID 的值，一律回 false，不丟例外。</summary>
    public static bool TryParse(string payload, [NotNullWhen(true)] out ClusterSignal? signal)
    {
        signal = null;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            signal = ParseRoot(document.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }

        return signal is not null;
    }

    private static string Write(ClusterSignal signal)
    {
        var overflow = signal.Kind == ClusterSignalKind.AccessChanged && signal.PersonIds.Count > MaxPersonIds;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v", Version);
            writer.WriteString("kind", KindName(signal.Kind));
            writer.WriteString("origin", signal.Origin);
            switch (signal.Kind)
            {
                case ClusterSignalKind.CredentialsRevoked:
                    writer.WriteString("account", signal.AccountId ?? Guid.Empty);
                    break;
                case ClusterSignalKind.AccessChanged:
                    WritePeople(writer, overflow ? [] : signal.PersonIds);
                    writer.WriteBoolean("all", signal.AllPeople || overflow);
                    break;
                default:
                    WritePeople(writer, signal.PersonIds);
                    break;
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WritePeople(Utf8JsonWriter writer, IReadOnlyList<Guid> people)
    {
        writer.WriteStartArray("people");
        foreach (var person in people)
        {
            writer.WriteStringValue(person);
        }

        writer.WriteEndArray();
    }

    private static ClusterSignal? ParseRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("v", out var version) || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var versionNumber) || versionNumber != Version
            || !TryReadKind(root, out var kind)
            || !TryReadGuid(root, "origin", out var origin))
        {
            return null;
        }

        switch (kind)
        {
            case ClusterSignalKind.CredentialsRevoked:
                return TryReadGuid(root, "account", out var account)
                    ? new ClusterSignal(kind) { AccountId = account, Origin = origin }
                    : null;
            case ClusterSignalKind.AccessChanged:
                if (!TryReadPeople(root, out var people) || !TryReadBool(root, "all", out var all))
                {
                    return null;
                }

                return new ClusterSignal(kind) { PersonIds = people, AllPeople = all, Origin = origin };
            default:
                return TryReadPeople(root, out var inboxPeople)
                    ? new ClusterSignal(kind) { PersonIds = inboxPeople, Origin = origin }
                    : null;
        }
    }

    private static bool TryReadKind(JsonElement root, out ClusterSignalKind kind)
    {
        kind = default;
        if (!root.TryGetProperty("kind", out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        switch (element.GetString())
        {
            case "credentials_revoked":
                kind = ClusterSignalKind.CredentialsRevoked;
                return true;
            case "access_changed":
                kind = ClusterSignalKind.AccessChanged;
                return true;
            case "inbox_changed":
                kind = ClusterSignalKind.InboxChanged;
                return true;
            default:
                return false;
        }
    }

    private static string KindName(ClusterSignalKind kind) => kind switch
    {
        ClusterSignalKind.CredentialsRevoked => "credentials_revoked",
        ClusterSignalKind.AccessChanged => "access_changed",
        _ => "inbox_changed",
    };

    private static bool TryReadGuid(JsonElement root, string name, out Guid value)
    {
        value = Guid.Empty;
        return root.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.String
            && element.TryGetGuid(out value);
    }

    private static bool TryReadBool(JsonElement root, string name, out bool value)
    {
        value = false;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = element.GetBoolean();
        return true;
    }

    private static bool TryReadPeople(JsonElement root, out IReadOnlyList<Guid> people)
    {
        people = [];
        if (!root.TryGetProperty("people", out var element) || element.ValueKind != JsonValueKind.Array
            || element.GetArrayLength() > MaxPersonIds)
        {
            return false;
        }

        var list = new List<Guid>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !item.TryGetGuid(out var id))
            {
                return false;
            }

            list.Add(id);
        }

        people = list;
        return true;
    }
}
