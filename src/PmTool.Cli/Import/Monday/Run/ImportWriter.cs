using System.Text.Json.Nodes;
using PmTool.Cli.Api;

namespace PmTool.Cli.Import.Monday.Run;

/// <summary>
/// 照檢查過的計畫寫入（交接第 6.1、6.7 節）：週期 → 成員 → 每個專案的項目（上層在前）→ 檔案與留言。
/// 先用 lookup 問出哪些已經匯入過，只送新的，所以重跑什麼都不新增。乾跑做同樣的事，但不寫入，只請伺服器逐列檢查項目。
/// 每一步的結果即時記在 <see cref="ImportReport"/>，中途失敗時報告裡就是已經完成的部分。
/// </summary>
internal sealed class ImportWriter(
    HistoryApi api, ImportPlan plan, RunOptions options, ImportReport report, Action<string> progress)
{
    // 每列都在伺服器的交易裡，批次大了慢；200～500 列一批。
    private const int ItemBatchSize = 300;

    // 乾跑不寫入，後面的批次看不到前面批次的項目，所以一批放到伺服器的上限，盡量不拆。
    private const int DryRunBatchSize = 1_000;

    private readonly ImportState _state = new();
    private readonly Dictionary<(string Project, string Name), Guid> _cycleIds = [];

    public async Task RunAsync()
    {
        foreach (var key in plan.Projects.Keys)
        {
            report.Project(key);
        }

        await ClassifyItemsAsync();
        var skipped = await ArchivedProjects.SettleAsync(api, plan, _state, report);
        var active = plan.Projects.Values.Where(project => !skipped.Contains(project.Info.Key)).ToArray();
        await EnsureCyclesAsync(active);
        await EnsureMembersAsync(active);
        foreach (var project in active)
        {
            if (options.DryRun)
            {
                await CheckItemsOnServerAsync(project);
            }
            else
            {
                await ImportItemsAsync(project);
            }
        }

        var content = new ContentImporter(api, plan.People, plan, options, report, _state, progress);
        foreach (var project in active)
        {
            await content.ImportAsync(project);
        }
    }

    /// <summary>哪些項目已經匯入過：在這個專案裡的略過；在別的專案裡的（被搬到別的群組）也略過，報告說它在哪裡。</summary>
    private async Task ClassifyItemsAsync()
    {
        var known = await api.LookupAsync("item", plan.Projects.Values.SelectMany(project => project.Items.Select(item => item.ExternalId)));
        foreach (var project in plan.Projects.Values)
        {
            var tally = report.Project(project.Info.Key);
            foreach (var item in project.Items)
            {
                if (known.TryGetValue(item.ExternalId, out var imported) && imported.ProjectKey != project.Info.Key)
                {
                    Exclude(item);
                    tally.ItemsElsewhere.Add((item.ExternalId, imported.ItemKey));
                }
                else if (known.TryGetValue(item.ExternalId, out imported))
                {
                    _state.PreExisting.Add(item.ExternalId);
                    _state.ItemKeys[item.ExternalId] = imported.ItemKey;
                    tally.ItemsSkipped++;
                }
                else if (item.ParentExternalId is { } parent && _state.Excluded.Contains(parent))
                {
                    Exclude(item);
                    tally.ItemsSkipped++;
                    report.Reminders.Add(NoteKinds.Skipped, $"monday 的項目 {item.ExternalId} 的上層已經匯到別的專案，這個子項目也略過。");
                }
            }
        }
    }

    private void Exclude(PlannedItem item) => _state.Excluded.Add(item.ExternalId);

    private async Task EnsureCyclesAsync(IEnumerable<ProjectPlan> projects)
    {
        foreach (var project in projects.Where(project => project.Cycles.Count > 0))
        {
            var key = project.Info.Key;
            var existing = (await api.CyclesAsync(key)).GroupBy(cycle => cycle.Name).ToDictionary(group => group.Key, group => group.First());
            foreach (var (name, cycle) in project.Cycles)
            {
                if (existing.TryGetValue(name, out var found))
                {
                    _cycleIds[(key, name)] = found.Id;
                    continue;
                }

                report.Project(key).CyclesCreated.Add(name);
                if (!options.DryRun)
                {
                    _cycleIds[(key, name)] = (await api.CreateCycleAsync(key, cycle)).Id;
                }
            }
        }
    }

    private async Task EnsureMembersAsync(IEnumerable<ProjectPlan> projects)
    {
        foreach (var project in projects)
        {
            foreach (var personId in project.MissingMembers)
            {
                if (!options.DryRun)
                {
                    await api.AddMemberAsync(project.Info.Key, personId, options.MemberRole);
                    project.Info.Members.Add(personId);
                }

                report.Project(project.Info.Key).MembersAdded.Add(plan.People.NameOf(personId));
            }
        }
    }

    private IEnumerable<PlannedItem> NewItems(ProjectPlan project) =>
        project.Items.Where(item => !_state.PreExisting.Contains(item.ExternalId) && !_state.Excluded.Contains(item.ExternalId));

    private JsonNode RowOf(PlannedItem item)
    {
        var row = (JsonObject)item.Row.DeepClone();
        if (item.CycleName is { } name && _cycleIds.TryGetValue((item.ProjectKey, name), out var cycleId))
        {
            row["cycleId"] = cycleId.ToString();
        }

        return row;
    }

    private async Task ImportItemsAsync(ProjectPlan project)
    {
        var key = project.Info.Key;
        var tally = report.Project(key);
        var items = NewItems(project).ToArray();
        var done = 0;
        foreach (var batch in items.Chunk(ItemBatchSize))
        {
            var result = await api.ImportItemsAsync(key, batch.Select(RowOf), dryRun: false);
            foreach (var created in result.Created)
            {
                _state.ItemKeys[created.ExternalId] = created.ItemKey;
            }

            foreach (var skipped in result.Skipped)
            {
                _state.ItemKeys[skipped.ExternalId] = skipped.ItemKey;
            }

            tally.ItemsCreated += result.Created.Count;
            tally.ItemsSkipped += result.Skipped.Count;
            done += batch.Length;
            progress($"專案 {key}：項目 {done}/{items.Length}");
        }
    }

    /// <summary>乾跑：請伺服器逐列檢查（類型、欄位值、日期、上層…），把它的問題記成問題；項目數照新的項目算。</summary>
    private async Task CheckItemsOnServerAsync(ProjectPlan project)
    {
        var key = project.Info.Key;
        var items = NewItems(project).ToArray();
        report.Project(key).ItemsCreated += items.Length;
        if (project.MissingMembers.Count > 0)
        {
            // 成員要等正式跑的時候才加進去；現在伺服器會把他們當成「不是成員」，逐列的檢查沒有意義。
            report.Reminders.Add(NoteKinds.Member, $"專案 {key} 還有成員要加，伺服器的逐列檢查略過（正式跑會先加成員）。");
            return;
        }

        foreach (var batch in items.Chunk(DryRunBatchSize))
        {
            var ids = batch.Select(item => item.ExternalId).ToHashSet(StringComparer.Ordinal);
            var rows = batch.Select(item => StripUnreachableParent(RowOf(item), ids, _state.PreExisting));
            try
            {
                await api.ImportItemsAsync(key, rows, dryRun: true);
            }
            catch (CliException exception) when (exception.ExitCode == ExitCodes.Invalid)
            {
                AddServerIssues(key, batch, exception);
            }
        }
    }

    /// <summary>乾跑不會真的建立，所以前一批的上層下一批看不到；拿掉那個關係，只檢查其他欄位。</summary>
    private static JsonNode StripUnreachableParent(JsonNode row, HashSet<string> batchIds, IReadOnlySet<string> known)
    {
        if (row["parentExternalId"]?.GetValue<string>() is { } parent && !batchIds.Contains(parent) && !known.Contains(parent))
        {
            ((JsonObject)row).Remove("parentExternalId");
        }

        return row;
    }

    private void AddServerIssues(string projectKey, PlannedItem[] batch, CliException exception)
    {
        if (exception.Details.Count == 0)
        {
            report.Issues.Add(NoteKinds.Server, $"專案 {projectKey}：伺服器檢查沒有通過：{exception.Message}");
            return;
        }

        foreach (var (path, message) in exception.Details)
        {
            var parts = path.Split('/');
            var item = parts is ["rows", var index, ..] && int.TryParse(index, out var position) && position >= 0 && position < batch.Length
                ? batch[position]
                : null;
            var who = item is null ? string.Empty : $"項目「{item.Row["title"]}」（monday 的 {item.ExternalId}）";
            report.Issues.Add(NoteKinds.Server, $"專案 {projectKey}：{who}{message}（{path}）");
        }
    }
}
