using System.Globalization;
using System.Text.RegularExpressions;

namespace PmTool.Cli.Import.Monday;

/// <summary>plan 的一個看板：欄、狀態、優先度、其他欄位、群組的草稿（<see cref="MondayPlanner"/> 的一部分）。</summary>
internal static partial class MondayBoardPlanner
{
    private const int MaxHintValues = 15;
    private const string DeletedState = "deleted";

    /// <summary>自訂欄位收得下的 monday 欄種類（見第 6.3 節）；其他的種類 run 不支援，plan 不列。</summary>
    private static readonly string[] SupportedTypes =
        ["numbers", "text", "long_text", "link", "date", "timeline", "status", "dropdown", "board_relation", "people", "checkbox"];

    private static readonly Dictionary<string, string> KnownPriorities = new(StringComparer.OrdinalIgnoreCase)
    {
        ["critical"] = "urgent", ["urgent"] = "urgent", ["high"] = "high", ["medium"] = "medium", ["low"] = "low",
        ["緊急"] = "urgent", ["高"] = "high", ["中"] = "medium", ["低"] = "low",
    };

    public static BoardMapping Draft(MondayBoard board, MappingAnnotations notes, DateTimeOffset now)
    {
        var path = MappingAnnotations.Key("boards", board.Id);
        var items = board.Items.Where(item => item.State != DeletedState).ToArray();
        var subitems = items.SelectMany(item => item.Subitems).Where(item => item.State != DeletedState).ToArray();
        notes.Note(path, $"{board.Name}（{items.Length} 個項目）");

        var priority = board.Columns.FirstOrDefault(column => column.Type == "status" && IsPriorityTitle(column.Title));
        var status = board.Columns.FirstOrDefault(column => column.Type == "status" && column != priority);
        var owner = board.Columns.FirstOrDefault(column => column.Type == "people");
        var due = board.Columns.FirstOrDefault(column => column.Type == "date");
        var timeline = board.Columns.FirstOrDefault(column => column.Type == "timeline");
        foreach (var (key, column) in new[] { ("statusColumn", status), ("ownerColumn", owner), ("dueColumn", due), ("timelineColumn", timeline), ("priorityColumn", priority) })
        {
            notes.Note(MappingAnnotations.Key(path, key), column is null ? "這個看板沒有這種欄" : $"{column.Title}（{column.Type}）");
        }

        var used = new[] { status, owner, due, timeline, priority }.OfType<MondayColumn>().Select(column => column.Id).ToHashSet();
        var draft = new BoardMapping
        {
            StatusColumn = status?.Id,
            OwnerColumn = owner?.Id,
            DueColumn = due?.Id,
            TimelineColumn = timeline?.Id,
            PriorityColumn = priority?.Id,
            Priorities = DraftPriorities(priority, items, path, notes),
            States = DraftStates(board, status, items, subitems, path, notes),
            Fields = DraftFields(board, used, items, path, notes),
            Groups = DraftGroups(board, items, path, notes),
        };
        DescribeLinkField(board, path, notes);
        DescribeStale(path, now, notes);
        DescribeSubitems(board, items, subitems, path, notes);
        return draft;
    }

    private const int StaleDraftDays = 90;

    /// <summary>
    /// stale 的範例（註解掉，使用者取消註解才生效）：很久沒動、還沒結束的項目匯成「取消」。
    /// 日期取「現在往前 90 天」那個月的 1 號，讓整個月以前的都算舊的。
    /// </summary>
    private static void DescribeStale(string path, DateTimeOffset now, MappingAnnotations notes)
    {
        var cutoff = now.AddDays(-StaleDraftDays);
        var before = new DateOnly(cutoff.Year, cutoff.Month, 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var key = MappingAnnotations.Key(path, "stale");
        notes.Before(key, "很久沒動、還沒結束的項目改用這個狀態（不然舊的待辦全擠進「我的工作」的「已過期」）；要用就取消下面的註解");
        notes.Before(key, "最後一次動（建立、留言、回覆裡最晚的）早於 before 這一天（組織的時區，當天不算）的項目才會換；已經完成、取消的不換");
        notes.Before(key, "stale:");
        notes.Before(key, $"  before: {before}");
        notes.Before(key, "  state: canceled");
    }

    private static Dictionary<string, string> DraftPriorities(MondayColumn? column, MondayItem[] items, string path, MappingAnnotations notes)
    {
        var map = new Dictionary<string, string>();
        if (column is null)
        {
            return map;
        }

        var counts = Count(items.Select(item => LabelOf(item, column.Id)));
        notes.Note(MappingAnnotations.Key(path, "priorities"), "優先度的標籤 → none、low、medium、high、urgent；留白的當成 none 並提醒");
        foreach (var label in OrderedLabels(column.Labels, counts).Where(label => label.Length > 0))
        {
            map[label] = KnownPriorities.GetValueOrDefault(label.Trim(), string.Empty);
            notes.Note(MappingAnnotations.Key(path, "priorities", label), $"{counts[label]} 個項目");
        }

        return map;
    }

    /// <summary>狀態的對應：看板與子項目共用一張表（子項目沒另外給時用它）。值一律留白，由人決定每個標籤是哪個狀態。</summary>
    private static Dictionary<string, string> DraftStates(
        MondayBoard board, MondayColumn? status, MondayItem[] items, MondayItem[] subitems, string path, MappingAnnotations notes)
    {
        var map = new Dictionary<string, string>();
        var subStatus = board.SubitemColumns.FirstOrDefault(column => column.Type == "status" && !IsPriorityTitle(column.Title));
        var itemCounts = status is null ? [] : Count(items.Select(item => LabelOf(item, status.Id)));
        var subCounts = subStatus is null ? [] : Count(subitems.Select(item => LabelOf(item, subStatus.Id)));
        if (status is null && subStatus is null)
        {
            return map;
        }

        notes.Note(MappingAnnotations.Key(path, "states"), "標籤（項目數）→ 狀態的 key（專案藍圖裡的）；空的標籤用 \"\"");
        var known = (status?.Labels ?? []).Concat(subStatus?.Labels ?? []).Distinct().ToArray();
        var seen = itemCounts.Keys.Concat(subCounts.Keys).Distinct().ToArray();
        foreach (var label in known.Where(seen.Contains).Concat(seen.Where(label => label.Length > 0 && !known.Contains(label))).Concat(seen.Where(label => label.Length == 0)))
        {
            map[label] = string.Empty;
            var counts = new List<string>();
            if (itemCounts.TryGetValue(label, out var itemCount))
            {
                counts.Add($"{itemCount} 個項目");
            }

            if (subCounts.TryGetValue(label, out var subs))
            {
                counts.Add($"{subs} 個子項目");
            }

            notes.Note(MappingAnnotations.Key(path, "states", label), string.Join("、", counts));
        }

        return map;
    }

    private static Dictionary<string, FieldMapping> DraftFields(
        MondayBoard board, HashSet<string> used, MondayItem[] items, string path, MappingAnnotations notes)
    {
        var fields = new Dictionary<string, FieldMapping>();
        foreach (var column in board.Columns.Where(column => !used.Contains(column.Id) && SupportedTypes.Contains(column.Type)))
        {
            var key = MappingAnnotations.Key(path, "fields", column.Id);
            fields[column.Id] = new FieldMapping { Field = string.Empty };
            notes.Note(key, $"{column.Title}（{column.Type}）→ 要匯就填自訂欄位的 key");
            var hint = ValueHint(column, items);
            if (hint is not null)
            {
                notes.Before(key, $"{column.Title} 在 monday 上出現的值：{hint}。單選、多選要用 values 對到選項（見 internal/HANDOFF-monday-import.md 第 5 節）");
            }
        }

        return fields;
    }

    /// <summary>狀態與關聯項目的值有名稱可以對選項；其他種類（數字、日期、文字）不需要。</summary>
    private static string? ValueHint(MondayColumn column, MondayItem[] items)
    {
        var names = column.Type switch
        {
            "status" => items.Select(item => LabelOf(item, column.Id)),
            "board_relation" => items.SelectMany(item => item.Values.Where(value => value.ColumnId == column.Id).SelectMany(value => value.LinkedItems.Select(linked => linked.Name))),
            _ => null,
        };
        if (names is null)
        {
            return null;
        }

        var counts = Count(names.Where(name => name.Length > 0)).OrderByDescending(pair => pair.Value).Take(MaxHintValues).ToArray();
        return counts.Length == 0 ? null : string.Join("、", counts.Select(pair => $"{pair.Key}（{pair.Value}）"));
    }

    private static void DescribeLinkField(MondayBoard board, string path, MappingAnnotations notes)
    {
        var key = MappingAnnotations.Key(path, "linkField");
        notes.Note(key, "每個項目寫上 monday 原連結的 url 欄位的 key；不寫就留白");
        var skipped = board.Columns.Where(column => !SupportedTypes.Contains(column.Type) && column.Type != "subtasks" && column.Type != "name").ToArray();
        if (skipped.Length > 0)
        {
            notes.Before(key, "monday 的這些欄 kumi 沒支援（不會匯）：" + string.Join("、", skipped.Select(column => $"{column.Title}（{column.Type}）")));
        }
    }

    /// <summary>子項目只寫成註解：要匯的人取消註解並填 type（沒有 type 的 subitems 讀不進來，草稿要能直接讀）。</summary>
    private static void DescribeSubitems(MondayBoard board, MondayItem[] items, MondayItem[] subitems, string path, MappingAnnotations notes)
    {
        if (subitems.Length == 0 && board.SubitemColumns.Count == 0)
        {
            return;
        }

        var key = MappingAnnotations.Key(path, "groups");
        var cols = board.SubitemColumns;
        notes.Before(key, $"這個看板有子項目（{subitems.Length} 個，在 {items.Count(item => item.Subitems.Count > 0)} 個項目底下）。要匯的話，取消下面的註解並填 type（子項目的類型 key）：");
        notes.Before(key, "subitems:");
        notes.Before(key, "  type:");
        notes.Before(key, $"  statusColumn: {FirstId(cols, "status", status: true)}");
        notes.Before(key, $"  ownerColumn: {FirstId(cols, "people")}");
        notes.Before(key, $"  dueColumn: {FirstId(cols, "date")}");
        notes.Before(key, $"  timelineColumn: {FirstId(cols, "timeline")}");
    }

    private static string FirstId(IReadOnlyList<MondayColumn> columns, string type, bool status = false) =>
        columns.FirstOrDefault(column => column.Type == type && (!status || !IsPriorityTitle(column.Title)))?.Id ?? string.Empty;

    private static Dictionary<string, GroupMapping> DraftGroups(MondayBoard board, MondayItem[] items, string path, MappingAnnotations notes)
    {
        var groups = new Dictionary<string, GroupMapping>();
        foreach (var group in board.Groups)
        {
            groups[group.Id] = new GroupMapping { Cycle = GuessCycle(group.Title) };
            var count = items.Count(item => item.GroupId == group.Id);
            var key = MappingAnnotations.Key(path, "groups", group.Id);
            notes.Note(key, $"{group.Title}（{count} 個）");
            notes.Note(MappingAnnotations.Key(key, "project"), "專案代號（要先在網頁上建好）");
            notes.Note(MappingAnnotations.Key(key, "type"), "項目的類型 key");
        }

        return groups;
    }

    /// <summary>標題像「2026年11月…」的群組是維運的月份：週期是那個月的第一天到最後一天。</summary>
    internal static CycleMapping? GuessCycle(string title)
    {
        var match = MonthPattern().Match(title);
        if (!match.Success
            || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var month)
            || month is < 1 or > 12 || year is < 1900 or > 2999)
        {
            return null;
        }

        return MonthCycle(year, month);
    }

    /// <summary>
    /// 一個月的週期：名稱「2026 年 11 月」、第一天到最後一天。草稿猜的週期與 <c>cycle: monthly</c> 依建立月份放的週期共用這個，
    /// 名稱才會一致，兩邊的項目才進同一個週期。
    /// </summary>
    internal static CycleMapping MonthCycle(int year, int month)
    {
        var start = new DateOnly(year, month, 1);
        return new CycleMapping($"{year} 年 {month} 月", start, start.AddMonths(1).AddDays(-1));
    }

    private static bool IsPriorityTitle(string title) =>
        title.Contains("priority", StringComparison.OrdinalIgnoreCase) || title.Contains("優先", StringComparison.Ordinal);

    private static string LabelOf(MondayItem item, string columnId) =>
        item.Values.FirstOrDefault(value => value.ColumnId == columnId)?.Label ?? string.Empty;

    private static Dictionary<string, int> Count(IEnumerable<string> values)
    {
        var counts = new Dictionary<string, int>();
        foreach (var value in values)
        {
            counts[value] = counts.GetValueOrDefault(value) + 1;
        }

        return counts;
    }

    /// <summary>設定裡定義的標籤（照 index）先、其餘出現過的接著、空的最後；只列有項目用到的。</summary>
    private static IEnumerable<string> OrderedLabels(IReadOnlyList<string> defined, Dictionary<string, int> counts) =>
        defined.Where(counts.ContainsKey)
            .Concat(counts.Keys.Where(label => label.Length > 0 && !defined.Contains(label)))
            .Concat(counts.Keys.Where(label => label.Length == 0));

    [GeneratedRegex(@"(\d{4})\s*年\s*(\d{1,2})\s*月")]
    private static partial Regex MonthPattern();
}
