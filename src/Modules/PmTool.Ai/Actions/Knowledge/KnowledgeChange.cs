using System.Text.Json;
using System.Text.Json.Serialization;

namespace PmTool.Ai.Actions.Knowledge;

/// <summary>知識庫建議裡的動作名稱。字串是對外合約（工具的參數、存在建議裡的整理過的狀態），不能改名。</summary>
internal static class KnowledgeActions
{
    public const string CreatePage = "create_page";

    public const string UpdatePage = "update_page";

    public const string MovePage = "move_page";

    public const string DeletePage = "delete_page";

    public const string RestorePage = "restore_page";

    public const string SetRestriction = "set_restriction";

    public const string CreateSpace = "create_space";

    public const string UpdateSpace = "update_space";

    public const string ArchiveSpace = "archive_space";

    public const string UnarchiveSpace = "unarchive_space";

    /// <summary>每種動作能給的屬性（不在裡面的屬性一律擋下，拼錯名稱時模型才知道）。</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Properties = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [CreatePage] = ["action", "spaceKey", "title", "body", "parentNumber", "beforeNumber", "ref", "parentRef"],
        [UpdatePage] = ["action", "spaceKey", "number", "title", "body", "expectedRevision"],
        [MovePage] = ["action", "spaceKey", "number", "parentNumber", "beforeNumber", "parentRef"],
        [DeletePage] = ["action", "spaceKey", "number"],
        [RestorePage] = ["action", "spaceKey", "number"],
        [SetRestriction] = ["action", "spaceKey", "number", "people"],
        [CreateSpace] = ["action", "key", "name", "description", "projectKey"],
        [UpdateSpace] = ["action", "spaceKey", "name", "description"],
        [ArchiveSpace] = ["action", "spaceKey"],
        [UnarchiveSpace] = ["action", "spaceKey"],
    };
}

/// <summary>
/// 建議裡的一個動作（整理過、檢查過的）。整份存成建議的狀態，套用時一步一步讀回來做；
/// 所以名稱與屬性是合約，欄位只加不改。
/// </summary>
internal sealed record KnowledgeChange(string Action)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string? SpaceKey { get; init; }

    public int? Number { get; init; }

    public string? Ref { get; init; }

    public string? ParentRef { get; init; }

    public int? ParentNumber { get; init; }

    public int? BeforeNumber { get; init; }

    public string? Title { get; init; }

    public string? Body { get; init; }

    public long? ExpectedRevision { get; init; }

    /// <summary>限制的名單；null 或空的是拿掉限制。</summary>
    public IReadOnlyList<Guid>? People { get; init; }

    /// <summary>create_space 的代號。</summary>
    public string? Key { get; init; }

    public string? Name { get; init; }

    public string? Description { get; init; }

    public string? ProjectKey { get; init; }
}
