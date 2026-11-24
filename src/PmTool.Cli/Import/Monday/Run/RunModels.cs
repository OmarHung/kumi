using System.Text.Json.Nodes;

namespace PmTool.Cli.Import.Monday.Run;

/// <param name="DryRun">只檢查：做完檢查再請伺服器逐列檢查，不寫任何東西。</param>
/// <param name="AddMembers">缺的成員以 member 加進專案（不然列成問題）。</param>
/// <param name="MemberRole">--add-members 加進專案的角色（藍圖裡的角色 key）。</param>
internal sealed record RunOptions(bool DryRun, bool AddMembers, string MemberRole = RunOptions.DefaultMemberRole)
{
    public const string DefaultMemberRole = "editor";
}

/// <summary>要匯入的一個項目：送給伺服器的那一列（週期的編號等到寫入時才知道）、原始資料、建立的人。</summary>
internal sealed record PlannedItem(
    string ExternalId,
    string ProjectKey,
    string? ParentExternalId,
    JsonObject Row,
    string? CycleName,
    MondayItem Source,
    ResolvedPerson Creator);

/// <summary>一個專案要匯的東西：項目（上層在前）、要用的週期、需要是成員的人。</summary>
internal sealed class ProjectPlan(ProjectInfo info)
{
    public ProjectInfo Info { get; } = info;

    public List<PlannedItem> Items { get; } = [];

    /// <summary>週期的名字 → 對應檔裡的日期；專案裡沒有時 run 會建立。</summary>
    public Dictionary<string, CycleMapping> Cycles { get; } = [];

    /// <summary>作者、建立的人、上傳的人、負責人、關注的人：伺服器要求都是專案成員。</summary>
    public HashSet<Guid> Required { get; } = [];

    public IReadOnlyList<Guid> MissingMembers => [.. Required.Where(id => !Info.Members.Contains(id))];
}

internal sealed class ImportPlan(PersonDirectory people, Notes issues, Notes reminders)
{
    public PersonDirectory People { get; } = people;

    public Notes Issues { get; } = issues;

    public Notes Reminders { get; } = reminders;

    /// <summary>fetch 沒下載的檔案：asset 編號 → 原因。</summary>
    public IReadOnlyDictionary<string, string> SkippedFiles { get; init; } = new Dictionary<string, string>();

    /// <summary>專案代號 → 要匯的東西，照第一次出現的順序。</summary>
    public Dictionary<string, ProjectPlan> Projects { get; } = [];
}

/// <summary>一個群組實際要怎麼匯：看板、對應、專案與類型、狀態對應（看板的被群組的蓋過）。</summary>
internal sealed record GroupWork(
    MondayBoard Board,
    BoardMapping BoardMapping,
    MondayGroup Group,
    GroupMapping GroupMapping,
    IReadOnlyList<MondayItem> Items)
{
    public string Title => $"{Group.Title}（{Group.Id}）";

    public string ProjectKey => GroupMapping.Project!;

    public string TypeKey => GroupMapping.Type!;

    /// <summary>群組自己的狀態對應蓋過看板的：只寫了幾個標籤的群組，其他標籤照看板的。</summary>
    public IReadOnlyDictionary<string, string> States =>
        BoardMapping.States.Concat(GroupMapping.States ?? new Dictionary<string, string>())
            .GroupBy(pair => pair.Key).ToDictionary(group => group.Key, group => group.Last().Value);
}
