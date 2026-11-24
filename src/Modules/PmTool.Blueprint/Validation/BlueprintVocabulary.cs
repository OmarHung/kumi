using System.Collections.Frozen;
using PmTool.Blueprint.Model;

namespace PmTool.Blueprint.Validation;

/// <summary>語意驗證用到的固定詞彙與上限。這些值不寫在 JSON Schema 裡，由伺服器負責檢查。</summary>
internal static class BlueprintVocabulary
{
    public const string KeyPattern = "^[a-z][a-z0-9_]{1,39}$";
    public const string CurrencyCodePattern = "^[A-Z]{3}$";

    public const string State = "state";
    public const string Assignee = "assignee";
    public const string ItemType = "item_type";
    public const string Parent = "parent";

    public const int MaxFields = 100;
    public const int MaxStatesPerWorkflow = 30;
    public const int MaxItemTypes = 20;
    public const int MaxAutomations = 50;

    /// <summary>
    /// 內建在每個工作項目上的欄位，自訂欄位不能同名。說明與上層項目也在這裡：
    /// 活動紀錄用同一個名字指稱核心欄位與自訂欄位，撞名就分不出改的是哪一個。
    /// </summary>
    public static readonly FrozenSet<string> CoreFields = new[]
    {
        "title", "description", State, Assignee, "priority", "start_date", "due_date", "estimate", Parent, ItemType,
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// 新寫入的藍圖不能用的欄位名稱：核心欄位，加上項目上的週期與里程碑。活動紀錄與事件用 cycle、milestone 指稱它們。
    /// 不併進 <see cref="CoreFields"/>：那一份也管讀回已存的藍圖與流程規則，已存著用了這兩個名字的藍圖會讀不回來。
    /// </summary>
    public static readonly FrozenSet<string> ReservedFieldKeys = CoreFields.Concat(["cycle", "milestone"]).ToFrozenSet(StringComparer.Ordinal);

    public static readonly FrozenSet<string> BuiltInRoles = new[]
    {
        "admin", "editor", "contributor", "commenter", "viewer",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>各觸發來源目前認得的事件。Schedule 與 Webhook 的事件名稱不限制。</summary>
    public static readonly FrozenDictionary<TriggerSource, FrozenSet<string>> KnownEvents =
        new Dictionary<TriggerSource, FrozenSet<string>>
        {
            [TriggerSource.Item] = new[] { "item.created", "item.updated", "item.transitioned" }.ToFrozenSet(StringComparer.Ordinal),
            [TriggerSource.Gitlab] = new[] { "merge_request.opened", "merge_request.merged", "merge_request.closed" }.ToFrozenSet(StringComparer.Ordinal),
            [TriggerSource.Github] = new[] { "pull_request.opened", "pull_request.merged", "pull_request.closed" }.ToFrozenSet(StringComparer.Ordinal),
        }.ToFrozenDictionary();
}
