using System.Text.RegularExpressions;

namespace PmTool.Cli.Import.Monday.Run;

/// <summary>對應檔本身的檢查（不需要伺服器）：看板、群組、欄的編號在 dump 裡找得到，群組有 project 與 type，名字合規定。</summary>
internal static partial class MappingChecks
{
    // 和伺服器的規則一樣（HistorySource.Pattern）；先在本機擋，不必到每個請求都被拒絕才知道。
    [GeneratedRegex("^[a-z][a-z0-9_-]{1,29}$")]
    private static partial Regex SourcePattern();

    private static readonly string[] PriorityNames = ["none", "low", "medium", "high", "urgent"];

    public static void Run(MondayMapping mapping, MondayDump dump, Notes issues, Notes reminders)
    {
        if (!SourcePattern().IsMatch(mapping.Source))
        {
            issues.Add(NoteKinds.Mapping, $"source「{mapping.Source}」要小寫英文開頭，小寫英文、數字、_ 或 -，2 到 30 個字。");
        }

        var boards = dump.Boards.ToDictionary(board => board.Id);
        foreach (var (boardId, boardMapping) in mapping.Boards)
        {
            if (!boards.TryGetValue(boardId, out var board))
            {
                issues.Add(NoteKinds.Mapping, $"對應檔的看板 {boardId} 不在 dump 裡（dump 有 {string.Join("、", boards.Keys)}）。");
                continue;
            }

            CheckBoard(board, boardMapping, issues);
        }

        foreach (var board in dump.Boards.Where(board => !mapping.Boards.ContainsKey(board.Id)))
        {
            reminders.Add(NoteKinds.Skipped, $"dump 裡的看板 {board.Id}（{board.Name}）沒有列在對應檔裡，沒有匯入。");
        }
    }

    private static void CheckBoard(MondayBoard board, BoardMapping mapping, Notes issues)
    {
        var where = $"看板 {board.Id}";
        CheckColumn(issues, board.Columns, mapping.StatusColumn, "status", $"{where} 的 statusColumn");
        CheckColumn(issues, board.Columns, mapping.OwnerColumn, "people", $"{where} 的 ownerColumn");
        CheckColumn(issues, board.Columns, mapping.DueColumn, "date", $"{where} 的 dueColumn");
        CheckColumn(issues, board.Columns, mapping.TimelineColumn, "timeline", $"{where} 的 timelineColumn");
        CheckColumn(issues, board.Columns, mapping.PriorityColumn, "status", $"{where} 的 priorityColumn");
        foreach (var (label, priority) in mapping.Priorities.Where(pair => !PriorityNames.Contains(pair.Value)))
        {
            issues.Add(NoteKinds.Mapping, $"{where} 的優先度「{label}」對應到「{priority}」，要是 {string.Join("、", PriorityNames)} 其中之一。");
        }

        foreach (var columnId in mapping.Fields.Keys)
        {
            CheckColumn(issues, board.Columns, columnId, null, $"{where} 的 fields");
        }

        CheckSubitems(board, mapping, issues, where);
        foreach (var (groupId, group) in mapping.Groups)
        {
            CheckGroup(board, groupId, group, issues, where);
        }
    }

    private static void CheckSubitems(MondayBoard board, BoardMapping mapping, Notes issues, string where)
    {
        if (mapping.Subitems is not { } subitems)
        {
            return;
        }

        if (board.SubitemColumns.Count == 0)
        {
            issues.Add(NoteKinds.Mapping, $"{where} 的對應寫了 subitems，但 dump 裡這個看板沒有子項目的欄（重新 fetch，或拿掉 subitems）。");
            return;
        }

        var label = $"{where} 子項目的";
        CheckColumn(issues, board.SubitemColumns, subitems.StatusColumn, "status", label + " statusColumn");
        CheckColumn(issues, board.SubitemColumns, subitems.OwnerColumn, "people", label + " ownerColumn");
        CheckColumn(issues, board.SubitemColumns, subitems.DueColumn, "date", label + " dueColumn");
        CheckColumn(issues, board.SubitemColumns, subitems.TimelineColumn, "timeline", label + " timelineColumn");
    }

    private static void CheckGroup(MondayBoard board, string groupId, GroupMapping group, Notes issues, string where)
    {
        var title = board.Groups.FirstOrDefault(candidate => candidate.Id == groupId)?.Title;
        if (title is null)
        {
            issues.Add(NoteKinds.Mapping, $"{where} 的群組 {groupId} 不在 dump 裡。");
            return;
        }

        if (group.Skip)
        {
            return;
        }

        // project 與 type 都留白是「不匯」（只匯幾個群組時，不必把其他的一個一個改成 skip）；只填一個才是漏填。
        var hasProject = !string.IsNullOrWhiteSpace(group.Project);
        var hasType = !string.IsNullOrWhiteSpace(group.Type);
        if (hasProject && !hasType)
        {
            issues.Add(NoteKinds.Mapping, $"{where} 的群組「{title}」（{groupId}）有 project 沒有 type：項目的類型。");
        }
        else if (hasType && !hasProject)
        {
            issues.Add(NoteKinds.Mapping, $"{where} 的群組「{title}」（{groupId}）有 type 沒有 project：要匯進哪個專案，或把 type 也清掉。");
        }
    }

    /// <summary>欄的編號要在看板裡；<paramref name="type"/> 不是 null 時，種類也要對。沒填（null）不檢查。</summary>
    private static void CheckColumn(Notes issues, IReadOnlyList<MondayColumn> columns, string? columnId, string? type, string label)
    {
        if (columnId is null)
        {
            return;
        }

        var column = columns.FirstOrDefault(candidate => candidate.Id == columnId);
        if (column is null)
        {
            issues.Add(NoteKinds.Mapping, $"{label}：欄 {columnId} 不在 dump 裡。");
        }
        else if (type is not null && column.Type != type)
        {
            issues.Add(NoteKinds.Mapping, $"{label}：欄 {columnId}（{column.Title}）是 {column.Type}，要是 {type}。");
        }
    }
}
