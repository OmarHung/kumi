using PmTool.Cli.Api;

namespace PmTool.Cli.Import.Monday.Run;

/// <summary>
/// 匯入前的檢查與計畫（交接第 6.1、6.2 節）：對應檔對 dump、對伺服器上的專案，算出每個專案要送哪些列。
/// 不寫任何東西；所有問題收在 <see cref="ImportPlan.Issues"/>，一次說完。
/// </summary>
internal sealed class ImportPlanner(
    MondayMapping mapping, MondayDump dump, PersonDirectory people, RunOptions options, HistoryApi api, Notes issues, Notes reminders)
{
    private readonly HashSet<string> _checkedFields = [];
    private readonly Dictionary<string, IReadOnlySet<string>> _unavailable = [];
    private readonly Dictionary<(string Board, string Column), FieldUse> _fieldUse = [];
    private readonly Dictionary<StaleRule, int> _staleCounts = [];
    private readonly Dictionary<string, int> _ruleTypeCounts = [];
    private readonly Dictionary<string, int> _ruleFieldCounts = [];
    private readonly HashSet<string> _checkedRules = [];
    private readonly HashSet<(string Project, string Cycle)> _monthlyCycles = [];
    private int _monthlyItems;
    private readonly HashSet<string> _seenItems = new(StringComparer.Ordinal);

    public async Task<ImportPlan> BuildAsync()
    {
        var plan = new ImportPlan(people, issues, reminders) { SkippedFiles = dump.SkippedFiles };
        MappingChecks.Run(mapping, dump, plan.Issues, plan.Reminders);
        if (mapping.FallbackPerson is not null && people.Fallback is null)
        {
            plan.Issues.Add(NoteKinds.Person, $"fallbackPerson {mapping.FallbackPerson} 在這個伺服器上找不到。");
        }

        var groups = SelectGroups(plan.Reminders);
        await LoadProjectsAsync(groups, plan);
        foreach (var group in groups.Where(group => plan.Projects.ContainsKey(group.ProjectKey)))
        {
            PlanGroup(group, plan);
        }

        CheckMembers(plan);
        CheckFieldsExistSomewhere(plan);
        ReportStale(plan.Reminders);
        ReportRules(plan.Reminders);
        return plan;
    }

    /// <summary>欄位對應到的自訂欄位只在部分目標有是正常的；所有目標都沒有，多半是打錯字。</summary>
    private sealed class FieldUse(string field)
    {
        public string Field { get; } = field;

        public bool IsAvailableSomewhere { get; set; }

        public List<string> Targets { get; } = [];
    }

    private void CheckFieldsExistSomewhere(ImportPlan plan)
    {
        foreach (var ((board, column), use) in _fieldUse.Where(pair => !pair.Value.IsAvailableSomewhere))
        {
            plan.Issues.Add(
                NoteKinds.Field,
                $"看板 {board} 的欄 {column} 對應的欄位「{use.Field}」在所有目標（{string.Join("、", use.Targets)}）都沒有：藍圖裡沒有這個欄位，或沒有掛在類型上（打錯字？）。");
        }
    }

    private bool ShouldImport(string state) => state.ToLowerInvariant() switch
    {
        "deleted" => false,
        "archived" => mapping.IncludeArchived,
        _ => true,
    };

    private List<GroupWork> SelectGroups(Notes reminders)
    {
        var groups = new List<GroupWork>();
        foreach (var board in dump.Boards.Where(board => mapping.Boards.ContainsKey(board.Id)))
        {
            var boardMapping = mapping.Boards[board.Id];
            var importable = board.Items.Where(item => ShouldImport(item.State)).ToArray();
            var notImported = importable.Where(item => !boardMapping.Groups.ContainsKey(item.GroupId)).ToList();
            foreach (var groupId in boardMapping.Groups.Where(pair => !pair.Value.Skip && IsBlank(pair.Value)).Select(pair => pair.Key))
            {
                notImported.AddRange(importable.Where(item => item.GroupId == groupId));
            }

            ReportNotImported(board, notImported, reminders);

            foreach (var (groupId, groupMapping) in boardMapping.Groups)
            {
                var group = board.Groups.FirstOrDefault(candidate => candidate.Id == groupId);
                if (group is null || groupMapping.Skip || string.IsNullOrWhiteSpace(groupMapping.Project) || string.IsNullOrWhiteSpace(groupMapping.Type))
                {
                    continue;
                }

                var items = importable.Where(item => item.GroupId == groupId).ToArray();
                if (items.Length > 0)
                {
                    groups.Add(new GroupWork(board, boardMapping, group, groupMapping, items));
                }
            }
        }

        return groups;
    }

    private static bool IsBlank(GroupMapping group) => string.IsNullOrWhiteSpace(group.Project) && string.IsNullOrWhiteSpace(group.Type);

    /// <summary>沒有列在對應檔、或 project 與 type 都留白的群組：不匯，彙總成一則提醒（列出有項目的群組）。</summary>
    private static void ReportNotImported(MondayBoard board, List<MondayItem> items, Notes reminders)
    {
        var groups = items.GroupBy(item => item.GroupId).ToArray();
        if (groups.Length == 0)
        {
            return;
        }

        var names = groups.Select(group => $"「{board.Groups.FirstOrDefault(candidate => candidate.Id == group.Key)?.Title ?? group.Key}」（{group.Count()}）");
        reminders.Add(
            NoteKinds.Skipped,
            $"看板 {board.Id} 有 {groups.Length} 個群組沒有匯入（沒有列在對應檔裡，或 project 與 type 留白），共 {items.Count} 個項目：{string.Join("、", names)}。");
    }

    private async Task LoadProjectsAsync(IEnumerable<GroupWork> groups, ImportPlan plan)
    {
        foreach (var key in groups.Select(group => group.ProjectKey).Distinct(StringComparer.Ordinal))
        {
            try
            {
                var info = await api.ProjectAsync(key);
                ProjectChecks.CheckProject(info, plan.Issues);
                if (options.AddMembers && !info.HasRole(options.MemberRole))
                {
                    plan.Issues.Add(NoteKinds.Member, $"專案 {key} 的藍圖裡沒有角色「{options.MemberRole}」，--add-members 加不了成員（用 --member-role 換一個角色）。");
                }

                plan.Projects[key] = new ProjectPlan(info);
            }
            catch (CliException exception)
            {
                plan.Issues.Add(NoteKinds.Project, $"專案 {key}：{exception.Message}");
            }
        }
    }

    private void PlanGroup(GroupWork work, ImportPlan plan)
    {
        var project = plan.Projects[work.ProjectKey];
        RuleChecks.Check(work, project.Info, _checkedRules, plan.Issues);
        if (!ProjectChecks.TypeExists(project.Info, work.TypeKey, $"群組「{work.Title}」", plan.Issues))
        {
            return;
        }

        var unavailable = FieldsMissingFromTarget(work, project, plan);
        if (work.GroupMapping.Cycle is { Monthly: false } cycle)
        {
            AddCycle(project, cycle, plan.Reminders);
        }

        var builder = new ItemRowBuilder(mapping, people, project, options, plan.Issues, plan.Reminders);
        CheckStale(work.BoardMapping, project.Info, work.TypeKey, $"群組「{work.Title}」", plan.Issues);
        var settings = GroupSettings(work) with
        {
            Fields = work.BoardMapping.Fields.Where(pair => !unavailable.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value),
        };
        var subitemSettings = SubitemSettings(work, project, plan);
        foreach (var item in work.Items.Where(item => _seenItems.Add(item.Id)))
        {
            var cycleName = CycleNameFor(work, project, item);
            if (builder.Build(item, settings, null, cycleName) is not { } planned)
            {
                continue;
            }

            project.Items.Add(planned);
            CountMonthly(work, project, cycleName);
            RemindMissingFieldValues(work, item, unavailable, plan.Reminders);
            if (subitemSettings is null)
            {
                RemindSkippedSubitems(work, item, plan.Reminders);
                continue;
            }

            foreach (var subitem in item.Subitems.Where(subitem => ShouldImport(subitem.State) && _seenItems.Add(subitem.Id)))
            {
                if (builder.Build(subitem, subitemSettings, item.Id, cycleName) is { } plannedSubitem)
                {
                    project.Items.Add(plannedSubitem);
                    CountMonthly(work, project, cycleName);
                }
            }
        }

        MergeCounts(builder);
    }

    private void RemindSkippedSubitems(GroupWork work, MondayItem item, Notes reminders)
    {
        if (work.BoardMapping.Subitems is null)
        {
            var skipped = item.Subitems.Count(subitem => ShouldImport(subitem.State));
            reminders.AddMany(NoteKinds.Skipped, $"群組 {work.Title} 的子項目沒有匯入（對應檔沒有 subitems）", skipped);
        }
    }

    private void MergeCounts(ItemRowBuilder builder)
    {
        foreach (var (rule, count) in builder.StaleCounts)
        {
            _staleCounts[rule] = _staleCounts.GetValueOrDefault(rule) + count;
        }

        foreach (var (name, count) in builder.RuleTypeCounts)
        {
            _ruleTypeCounts[name] = _ruleTypeCounts.GetValueOrDefault(name) + count;
        }

        foreach (var (name, count) in builder.RuleFieldCounts)
        {
            _ruleFieldCounts[name] = _ruleFieldCounts.GetValueOrDefault(name) + count;
        }
    }

    /// <summary>
    /// 項目放進哪個週期：固定的週期是它的名稱；<c>cycle: monthly</c> 依項目在 monday 建立的月份（組織的時區），
    /// 專案裡還沒有那個月的週期就記下來（寫入時建立）。沒有 cycle 是 null。
    /// </summary>
    private string? CycleNameFor(GroupWork work, ProjectPlan project, MondayItem item)
    {
        if (work.GroupMapping.Cycle is not { } cycle)
        {
            return null;
        }

        if (!cycle.Monthly)
        {
            return cycle.Name;
        }

        var local = item.CreatedAt.ToOffset(ItemRowBuilder.OrganizationOffset);
        var month = MondayBoardPlanner.MonthCycle(local.Year, local.Month);
        AddCycle(project, month, reminders: null);
        return month.Name;
    }

    private void CountMonthly(GroupWork work, ProjectPlan project, string? cycleName)
    {
        if (work.GroupMapping.Cycle is { Monthly: true } && cycleName is not null)
        {
            _monthlyItems++;
            _monthlyCycles.Add((project.Info.Key, cycleName));
        }
    }

    /// <summary>依標題的規則與依建立月份的週期：彙總成幾則提醒（每種類型、每個欄位各一則），不是每個項目一則；乾跑也看得到。</summary>
    private void ReportRules(Notes reminders)
    {
        foreach (var (name, count) in _ruleTypeCounts)
        {
            reminders.Add(NoteKinds.TitleRule, $"依標題的規則：{count} 個項目的類型改成「{name}」");
        }

        foreach (var (name, count) in _ruleFieldCounts)
        {
            reminders.Add(NoteKinds.TitleRule, $"依標題的規則補了 {count} 個「{name}」");
        }

        if (_monthlyItems > 0)
        {
            reminders.Add(NoteKinds.Cycle, $"依建立的月份放進週期：{_monthlyItems} 個項目、{_monthlyCycles.Count} 個週期");
        }
    }

    /// <summary>stale 的狀態要在這個目標類型的流程裡（和狀態對應的檢查同一種寫法）。</summary>
    internal static void CheckStale(BoardMapping board, ProjectInfo info, string typeKey, string where, Notes issues)
    {
        if (board.Stale is { } stale && !info.StateKeys(typeKey).Contains(stale.State))
        {
            issues.Add(NoteKinds.State, $"專案 {info.Key} 的類型「{typeKey}」的流程裡沒有狀態「{stale.State}」（{where}的 stale.state）。");
        }
    }

    /// <summary>很久沒動的項目匯成取消：每個規則（日期與狀態）一則彙總，不是每個項目一則。</summary>
    private void ReportStale(Notes reminders)
    {
        foreach (var (rule, count) in _staleCounts.Where(pair => pair.Value > 0))
        {
            reminders.Add(
                NoteKinds.Stale,
                $"{count} 個很久沒動的項目（最後一次動早於 {rule.Before:yyyy-MM-dd}）匯成「取消」（狀態 {rule.State}）");
        }
    }

    /// <summary>這個專案與類型沒有的欄位（對應檔的欄編號）；同一組（專案、類型、看板）只檢查一次。</summary>
    private IReadOnlySet<string> FieldsMissingFromTarget(GroupWork work, ProjectPlan project, ImportPlan plan)
    {
        var key = $"{work.ProjectKey}|{work.TypeKey}|{work.Board.Id}";
        if (_unavailable.TryGetValue(key, out var known))
        {
            return known;
        }

        var missing = ProjectChecks.CheckFields(project.Info, work.TypeKey, work.BoardMapping, work.Board.Columns, plan.Issues);
        _unavailable[key] = missing;
        foreach (var (column, field) in work.BoardMapping.Fields)
        {
            if (!_fieldUse.TryGetValue((work.Board.Id, column), out var use))
            {
                _fieldUse[(work.Board.Id, column)] = use = new FieldUse(field.Field);
            }

            use.Targets.Add($"{work.ProjectKey} 的類型「{work.TypeKey}」");
            use.IsAvailableSomewhere |= !missing.Contains(column);
        }

        return missing;
    }

    private void RemindMissingFieldValues(GroupWork work, MondayItem item, IReadOnlySet<string> missing, Notes reminders)
    {
        foreach (var column in missing)
        {
            if (item.Values.FirstOrDefault(value => value.ColumnId == column) is { } value && HasValue(value))
            {
                reminders.Add(
                    NoteKinds.Field,
                    $"專案 {work.ProjectKey} 的類型「{work.TypeKey}」沒有欄位「{work.BoardMapping.Fields[column].Field}」，項目的值略過");
            }
        }
    }

    private static bool HasValue(MondayColumnValue value) =>
        !string.IsNullOrWhiteSpace(value.Text) || !string.IsNullOrWhiteSpace(value.Label) || value.PersonIds.Count > 0 || value.LinkedItems.Count > 0;

    /// <summary>同名週期在不同群組寫了不同的日期：用先出現的，但說一聲。</summary>
    private static void AddCycle(ProjectPlan project, CycleMapping cycle, Notes? reminders)
    {
        if (project.Cycles.TryGetValue(cycle.Name, out var first))
        {
            if (reminders is not null && (first.Start != cycle.Start || first.End != cycle.End))
            {
                reminders.Add(
                    NoteKinds.Value,
                    $"專案 {project.Info.Key} 的週期「{cycle.Name}」在對應檔裡有不同的日期（{first.Start:yyyy-MM-dd}～{first.End:yyyy-MM-dd} 和 {cycle.Start:yyyy-MM-dd}～{cycle.End:yyyy-MM-dd}），用先出現的");
            }

            return;
        }

        project.Cycles[cycle.Name] = cycle;
    }

    private static ItemSettings GroupSettings(GroupWork work) => new(
        work.TypeKey, work.Title, work.States, work.BoardMapping.StatusColumn, work.BoardMapping.OwnerColumn, work.BoardMapping.DueColumn,
        work.BoardMapping.TimelineColumn, work.BoardMapping.PriorityColumn, work.BoardMapping.Priorities, work.BoardMapping.Fields,
        work.BoardMapping.LinkField, work.BoardMapping.Stale,
        TitleRules.ForProject(work.BoardMapping.TitleRules, work.ProjectKey), work.GroupMapping.Fields);

    /// <summary>子項目的設定；對應檔沒有 subitems（子項目不匯），或子項目的類型在專案裡不存在時是 null。</summary>
    private ItemSettings? SubitemSettings(GroupWork work, ProjectPlan project, ImportPlan plan)
    {
        // 群組裡沒有要匯的子項目就不檢查子項目的類型：維運專案的群組沒有子項目，不該因為沒有「子任務」類型被擋下。
        var hasSubitems = work.Items.Any(item => ShouldImport(item.State) && item.Subitems.Any(subitem => ShouldImport(subitem.State)));
        if (work.BoardMapping.Subitems is not { } subitems
            || !hasSubitems
            || !ProjectChecks.TypeExists(project.Info, subitems.Type, $"群組「{work.Title}」的子項目", plan.Issues))
        {
            return null;
        }

        CheckStale(work.BoardMapping, project.Info, subitems.Type, $"群組「{work.Title}」的子項目", plan.Issues);
        if (_checkedFields.Add($"{work.ProjectKey}|{subitems.Type}|link"))
        {
            ProjectChecks.CheckFields(
                project.Info, subitems.Type, new BoardMapping { LinkField = work.BoardMapping.LinkField }, work.Board.SubitemColumns, plan.Issues);
        }

        return new ItemSettings(
            subitems.Type, $"{work.Title} 的子項目", subitems.States ?? work.States, subitems.StatusColumn, subitems.OwnerColumn,
            subitems.DueColumn, subitems.TimelineColumn, null, new Dictionary<string, string>(), new Dictionary<string, FieldMapping>(),
            work.BoardMapping.LinkField, work.BoardMapping.Stale);
    }

    /// <summary>需要是成員的人：有 --add-members 就由 run 加；沒有的話列出來，請使用者先在網頁上加。</summary>
    private void CheckMembers(ImportPlan plan)
    {
        if (options.AddMembers)
        {
            return;
        }

        foreach (var (key, project) in plan.Projects.Where(pair => pair.Value.MissingMembers.Count > 0))
        {
            var names = string.Join("、", project.MissingMembers.Select(people.NameOf));
            plan.Issues.Add(NoteKinds.Member, $"專案 {key} 要有這些人當成員：{names}（先在網頁上加進專案，或加 --add-members）。");
        }
    }
}
