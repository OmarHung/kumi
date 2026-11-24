using System.Text.Json.Nodes;

namespace PmTool.Cli.Import.Monday.Run;

/// <summary>一個專案這一次做了什麼（乾跑時是「會做什麼」）。寫入時一邊做一邊數，所以中途失敗也有已經完成的部分。</summary>
internal sealed class ProjectTally(string key)
{
    public string Key { get; } = key;

    public int ItemsCreated { get; set; }

    public int ItemsSkipped { get; set; }

    /// <summary>已經匯到別的專案的項目：外部編號與它現在的項目編號。</summary>
    public List<(string ExternalId, string ItemKey)> ItemsElsewhere { get; } = [];

    public int CommentsCreated { get; set; }

    public int CommentsSkipped { get; set; }

    public int AttachmentsCreated { get; set; }

    public int AttachmentsSkipped { get; set; }

    public int AttachmentsNotMoved { get; set; }

    public List<string> CyclesCreated { get; } = [];

    public List<string> MembersAdded { get; } = [];
}

internal static class ReportStatus
{
    public const string Invalid = "invalid";
    public const string DryRun = "dry_run";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

/// <summary>匯入的報告（交接第 6.8 節）：每個專案做了什麼，加上問題與提醒。可以印成文字，或寫成 JSON。</summary>
internal sealed class ImportReport(bool dryRun, Notes issues, Notes reminders)
{
    private const int ShownPerKind = 20;

    private readonly Dictionary<string, ProjectTally> _projects = [];

    public bool DryRun { get; } = dryRun;

    public string Status { get; set; } = ReportStatus.Completed;

    public string? Failure { get; set; }

    public Notes Issues { get; } = issues;

    public Notes Reminders { get; } = reminders;

    public IReadOnlyCollection<ProjectTally> Projects => _projects.Values;

    public ProjectTally Project(string key)
    {
        if (!_projects.TryGetValue(key, out var tally))
        {
            _projects[key] = tally = new ProjectTally(key);
        }

        return tally;
    }

    public string ToJsonText() => ToJson().ToJsonString(new System.Text.Json.JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    public JsonObject ToJson() => new()
    {
        ["dryRun"] = DryRun,
        ["status"] = Status,
        ["failure"] = Failure,
        ["projects"] = new JsonArray([.. Projects.Select(ProjectJson)]),
        ["issues"] = NotesJson(Issues),
        ["reminders"] = NotesJson(Reminders),
    };

    private static JsonNode ProjectJson(ProjectTally project) => new JsonObject
    {
        ["project"] = project.Key,
        ["items"] = new JsonObject
        {
            ["created"] = project.ItemsCreated,
            ["skipped"] = project.ItemsSkipped,
            ["elsewhere"] = new JsonArray([.. project.ItemsElsewhere.Select(item => (JsonNode)new JsonObject
            {
                ["externalId"] = item.ExternalId,
                ["itemKey"] = item.ItemKey,
            })]),
        },
        ["comments"] = new JsonObject { ["created"] = project.CommentsCreated, ["skipped"] = project.CommentsSkipped },
        ["attachments"] = new JsonObject
        {
            ["created"] = project.AttachmentsCreated,
            ["skipped"] = project.AttachmentsSkipped,
            ["notMoved"] = project.AttachmentsNotMoved,
        },
        ["cyclesCreated"] = new JsonArray([.. project.CyclesCreated.Select(name => (JsonNode?)JsonValue.Create(name))]),
        ["membersAdded"] = new JsonArray([.. project.MembersAdded.Select(name => (JsonNode?)JsonValue.Create(name))]),
    };

    private static JsonArray NotesJson(Notes notes) => new([.. notes.ToList().Select(note => (JsonNode)new JsonObject
    {
        ["kind"] = note.Kind,
        ["message"] = note.Message,
        ["count"] = note.Count,
    })]);

    public async Task WriteTextAsync(TextWriter output)
    {
        var verbs = DryRun ? ("會新增", "已經有") : ("新增", "略過");
        await output.WriteLineAsync(Status switch
        {
            ReportStatus.Invalid => "檢查沒有通過，什麼都沒有寫入。",
            ReportStatus.DryRun => "乾跑：檢查通過，沒有寫入任何東西（下面是真的跑會做的事）。",
            ReportStatus.Failed => $"匯入中途失敗，下面是已經完成的部分：{Failure}",
            _ => "匯入完成。",
        });
        foreach (var project in Projects)
        {
            await WriteProjectAsync(output, project, verbs);
        }

        await WriteNotesAsync(output, "問題", Issues);
        await WriteNotesAsync(output, "提醒", Reminders);
    }

    private static async Task WriteProjectAsync(TextWriter output, ProjectTally project, (string Created, string Skipped) verbs)
    {
        await output.WriteLineAsync($"專案 {project.Key}");
        await output.WriteLineAsync($"  項目：{verbs.Created} {project.ItemsCreated}，{verbs.Skipped} {project.ItemsSkipped}");
        foreach (var (externalId, itemKey) in project.ItemsElsewhere)
        {
            await output.WriteLineAsync($"    monday 的 {externalId} 已經在 {itemKey}（別的專案），這裡略過");
        }

        await output.WriteLineAsync($"  留言：{verbs.Created} {project.CommentsCreated}，{verbs.Skipped} {project.CommentsSkipped}");
        await output.WriteLineAsync($"  附件：{verbs.Created} {project.AttachmentsCreated}，{verbs.Skipped} {project.AttachmentsSkipped}，沒搬 {project.AttachmentsNotMoved}");
        if (project.CyclesCreated.Count > 0)
        {
            await output.WriteLineAsync($"  週期：{(verbs.Created == "新增" ? "建立" : "會建立")} {string.Join("、", project.CyclesCreated.Select(name => $"「{name}」"))}");
        }

        if (project.MembersAdded.Count > 0)
        {
            await output.WriteLineAsync($"  成員：{(verbs.Created == "新增" ? "加入" : "會加入")} {string.Join("、", project.MembersAdded)}");
        }
    }

    /// <summary>每一種最多印 20 筆，其餘的只說有幾筆（完整的在 --report 的 JSON 裡）。</summary>
    private static async Task WriteNotesAsync(TextWriter output, string title, Notes notes)
    {
        var all = notes.ToList();
        if (all.Count == 0)
        {
            return;
        }

        await output.WriteLineAsync($"{title}（{all.Count}）：");
        foreach (var kind in all.GroupBy(note => note.Kind))
        {
            foreach (var note in kind.Take(ShownPerKind))
            {
                await output.WriteLineAsync($"  ・{note.Message}{(note.Count > 1 ? $"（{note.Count} 次）" : string.Empty)}");
            }

            if (kind.Count() > ShownPerKind)
            {
                await output.WriteLineAsync($"  ・（{kind.Key} 還有 {kind.Count() - ShownPerKind} 筆沒有列出，完整的在 --report 的檔案裡）");
            }
        }
    }
}
