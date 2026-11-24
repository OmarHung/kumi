using PmTool.Abstractions;
using PmTool.Blueprint.Model;
using PmTool.Work.Access;
using PmTool.Work.Common;
using PmTool.Work.Persistence;
using static PmTool.Work.Common.TextRules;

namespace PmTool.Work.Items;

/// <summary>一列檢查合格之後要建立的東西。</summary>
internal sealed record CheckedImportRow(ItemType ItemType, ItemChanges Changes, WorkflowState? State);

/// <summary>匯入的檢查：請求本身、需要的權限、每一列。每一列的規則就是新增項目的規則（<see cref="ItemRules"/>）。</summary>
internal static class ItemImportRules
{
    /// <summary>一次最多回報幾筆問題：一千列各有好幾個問題時，回應不能跟著無限變大。</summary>
    public const int MaxDetails = 500;

    public const string RowsPath = "rows";

    public static Failure? CheckRequest(ItemImport import)
    {
        if (import.Rows is null || import.Rows.Count == 0)
        {
            return WorkFailures.Invalid(WorkErrorCodes.ImportInvalid, RowsPath, "沒有要匯入的項目。");
        }

        return import.Rows.Count > ItemImport.MaxRows
            ? WorkFailures.Invalid(
                WorkErrorCodes.ImportInvalid, RowsPath, $"一次最多匯入 {ItemImport.MaxRows} 列，這次有 {import.Rows.Count} 列。請分成幾份。")
            : null;
    }

    /// <summary>
    /// 有任何一列指定了不是流程起始狀態的狀態，就需要 project.manage：直接設定狀態會跳過轉換規則與角色限制。
    /// 類型不認得或狀態不在流程裡的列不在這裡判斷（它們之後會被當成那一列的問題），所以只看認得的類型。
    /// 狀態是空字串或純空白時當成沒給（試算表的空格常常是這樣送來的）：不要求 project.manage，那一列也沒有狀態的問題。
    /// </summary>
    public static bool NeedsManage(ProjectAccess access, ItemImport import) =>
        import.Rows.Any(row =>
            !string.IsNullOrWhiteSpace(row?.State)
            && row.State is { } state
            && row.Item?.Type is { } type
            && access.Schema.ItemTypes.TryGetValue(type, out var itemType)
            && access.Schema.WorkflowOf(itemType).States[0].Key != state);

    public static Failure NeedsManageFailure() =>
        WorkFailures.Forbidden("匯入時指定項目的狀態，需要管理專案的權限；請拿掉狀態那一欄，或請專案管理員來匯入。");

    /// <summary>檢查一列。不合格時回問題，路徑已經加上 rows/列的索引/。</summary>
    public static async Task<(CheckedImportRow? Row, IReadOnlyList<FailureDetail> Problems)> CheckRowAsync(
        WorkDbContext db, ProjectAccess access, ImportedItem? row, int index, CancellationToken cancellationToken)
    {
        if (row?.Item is not { } item)
        {
            return (null, [Problem(index, null, WorkErrorCodes.ImportInvalid, "這一列是空的。")]);
        }

        if (item.Type is null || !access.Schema.ItemTypes.TryGetValue(item.Type, out var itemType))
        {
            return (null, [Problem(index, CoreFields.Type, WorkErrorCodes.ItemUnknownType, $"這個專案沒有{Quote(item.Type)}這種項目類型。")]);
        }

        var changes = await ItemRules.CheckAsync(db, access, itemType, null, ItemChanges.AsPatch(item), cancellationToken);
        var problems = changes.Failure is { } failure
            ? [.. failure.Details.Select(d => d with { Path = Path(index, d.Path) })]
            : new List<FailureDetail>();
        var state = StateOf(access, itemType, row.State, index, problems);
        return problems.Count > 0 ? (null, problems) : (new CheckedImportRow(itemType, changes.Value, state), []);
    }

    /// <summary>把問題整理成一個失敗：代碼固定是 item.import_invalid，細項最多 <see cref="MaxDetails"/> 筆，截斷時說明還有幾筆。</summary>
    /// <param name="noun">沒有建立的是什麼（歷史匯入的留言用「留言」）。</param>
    public static Failure ToFailure(IReadOnlyList<FailureDetail> problems, string noun = "項目")
    {
        var shown = problems.Take(MaxDetails).ToArray();
        var message = $"匯入的內容有 {problems.Count} 個問題，一個{noun}都沒有建立。第一個：{shown[0].Message}";
        if (problems.Count > shown.Length)
        {
            message += $"（只列出前 {shown.Length} 個，還有 {problems.Count - shown.Length} 個沒有列出）";
        }

        return new Failure(FailureKind.Invalid, WorkErrorCodes.ImportInvalid, message) { Details = shown };
    }

    private static WorkflowState? StateOf(ProjectAccess access, ItemType itemType, string? key, int index, List<FailureDetail> problems)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var workflow = access.Schema.WorkflowOf(itemType);
        var found = workflow.States.FirstOrDefault(s => s.Key == key);
        if (found is null)
        {
            var known = string.Join("、", workflow.States.Select(s => Quote(s.Key)));
            problems.Add(Problem(index, "state", WorkErrorCodes.ImportInvalid, $"{Quote(itemType.Name)}的流程沒有{Quote(key)}這個狀態。可以用的狀態：{known}。"));
        }

        return found;
    }

    private static FailureDetail Problem(int index, string? field, string code, string message) =>
        new(code, Path(index, field), message);

    private static string Path(int index, string? field) => field is null ? $"{RowsPath}/{index}" : $"{RowsPath}/{index}/{field}";
}
