using System.Text.Json.Nodes;
using PmTool.Cli.Api;

namespace PmTool.Cli.Import.Monday.Run;

/// <summary>一組項目怎麼轉：群組的項目與子項目各有一份（欄的編號、狀態對應、類型不同）。</summary>
internal sealed record ItemSettings(
    string TypeKey,
    string GroupTitle,
    IReadOnlyDictionary<string, string> States,
    string? StatusColumn,
    string? OwnerColumn,
    string? DueColumn,
    string? TimelineColumn,
    string? PriorityColumn,
    IReadOnlyDictionary<string, string> Priorities,
    IReadOnlyDictionary<string, FieldMapping> Fields,
    string? LinkField,
    StaleRule? Stale = null,
    IReadOnlyList<TitleRule>? TitleRules = null,
    IReadOnlyDictionary<string, string>? DefaultFields = null);

/// <summary>把一個 monday 項目轉成 <c>history/items</c> 的一列（交接第 6.2、6.3 節）。問題與提醒記在傳進來的 <see cref="Notes"/>。</summary>
internal sealed class ItemRowBuilder(
    MondayMapping mapping, PersonDirectory people, ProjectPlan project, RunOptions options, Notes issues, Notes reminders)
{
    private const int MaxTitleLength = 300;
    private const int MaxFollowers = 50;
    private const string UntitledTitle = "（無標題）";

    // 組織的時區是 Asia/Taipei（沒有日光節約時間，固定 +08:00）：stale.before 是那個時區的一天，不是 UTC。
    internal static readonly TimeSpan OrganizationOffset = TimeSpan.FromHours(8);

    private static readonly string[] UnfinishedCategories = ["backlog", "not_started", "in_progress"];

    /// <summary>每個 stale 規則換成取消的項目數（報告彙總成一則提醒用）。</summary>
    public Dictionary<StaleRule, int> StaleCounts { get; } = [];

    /// <summary>依標題的規則改了類型的項目數：類型的名稱 → 項目數（報告彙總成一則提醒用）。</summary>
    public Dictionary<string, int> RuleTypeCounts { get; } = [];

    /// <summary>依標題的規則補了值的項目數：欄位的名稱 → 項目數。</summary>
    public Dictionary<string, int> RuleFieldCounts { get; } = [];

    public PlannedItem? Build(MondayItem item, ItemSettings settings, string? parentExternalId, string? cycleName)
    {
        settings = ApplyTypeRule(item, settings);
        var creator = people.Require(item.CreatorId, issues);
        var participants = RequireParticipants(item, creator);
        if (creator?.UsedFallback == true)
        {
            reminders.Add(NoteKinds.FallbackAuthor, $"{creator.OriginalName} 對不上這裡的人，建立的項目改用 fallbackPerson（{people.FallbackEmail}）");
        }

        // 人員有問題時也繼續檢查狀態、優先度、欄位：一次把這個項目的問題說完（只是不產生那一列）。
        var row = new JsonObject
        {
            ["externalId"] = item.Id,
            ["type"] = settings.TypeKey,
            ["title"] = Title(item),
        };
        if (creator is not null)
        {
            project.Required.Add(creator.Person.Id);
            row["createdBy"] = creator.Person.Id.ToString();
        }

        row["createdAt"] = HistoryApi.Moment(item.CreatedAt);
        AddState(row, item, settings);
        AddPriority(row, item, settings);
        AddDates(row, item, settings);
        AddPeople(row, item, settings);
        AddFields(row, item, settings);
        if (parentExternalId is not null)
        {
            row["parentExternalId"] = parentExternalId;
        }

        return creator is null || !participants
            ? null
            : new PlannedItem(item.Id, project.Info.Key, parentExternalId, row, cycleName, item, creator);
    }

    /// <summary>
    /// 類型 = 第一條符合而且有 type 的規則，否則群組的類型（子項目沒有規則，照 subitems.type）。
    /// 規則的類型在專案裡不存在時不換：檢查階段已經列成問題。
    /// </summary>
    private ItemSettings ApplyTypeRule(MondayItem item, ItemSettings settings)
    {
        if (settings.TitleRules is not { Count: > 0 } rules
            || TitleRules.TypeFor(rules, TitleRules.Normalize(item.Name)) is not { } type
            || type == settings.TypeKey
            || project.Info.ItemType(type) is null)
        {
            return settings;
        }

        var name = project.Info.TypeName(type);
        RuleTypeCounts[name] = RuleTypeCounts.GetValueOrDefault(name) + 1;
        return settings with { TypeKey = type };
    }

    /// <summary>留言的作者、項目檔案的上傳者也要署名、要是專案成員；對不上又沒有 fallbackPerson 就是問題。</summary>
    private bool RequireParticipants(MondayItem item, ResolvedPerson? creator)
    {
        var allResolved = creator is not null;
        foreach (var comment in CommentThreads.Flatten(item))
        {
            if (people.Require(comment.CreatorId, issues) is { } author)
            {
                project.Required.Add(author.Person.Id);
            }
            else
            {
                allResolved = false;
            }
        }

        foreach (var asset in item.Assets)
        {
            if ((people.Match(asset.UploadedById) ?? creator?.Person) is { } uploader)
            {
                project.Required.Add(uploader.Id);
            }
        }

        return allResolved;
    }

    private string Title(MondayItem item)
    {
        var title = MarkdownText.StripInvisible(item.Name).Trim();
        if (title.Length == 0)
        {
            reminders.Add(NoteKinds.Title, $"monday 的項目 {item.Id} 沒有名稱，標題用「{UntitledTitle}」");
            return UntitledTitle;
        }

        if (title.Length <= MaxTitleLength)
        {
            return title;
        }

        reminders.Add(NoteKinds.Title, $"標題超過 {MaxTitleLength} 字，截斷：{title[..30]}…");
        var cut = char.IsHighSurrogate(title[MaxTitleLength - 1]) ? MaxTitleLength - 1 : MaxTitleLength;
        return title[..cut].TrimEnd();
    }

    private void AddState(JsonObject row, MondayItem item, ItemSettings settings)
    {
        // 沒有狀態欄時 row 不帶狀態，伺服器用流程的起始狀態：視為還沒結束。
        string state;
        if (settings.StatusColumn is null)
        {
            state = string.Empty;
        }
        else if (MapState(item, settings, settings.StatusColumn) is { } mapped)
        {
            state = mapped;
        }
        else
        {
            return;
        }

        if (state.Length > 0)
        {
            row["state"] = state;
        }

        ApplyStale(row, item, settings, state);
    }

    /// <summary>很久沒動又還沒結束的項目改用 stale 的狀態；stale 的狀態不在流程裡時不換（檢查階段已經列成問題）。</summary>
    private void ApplyStale(JsonObject row, MondayItem item, ItemSettings settings, string state)
    {
        if (settings.Stale is not { } stale || !IsStale(item, stale))
        {
            return;
        }

        var info = project.Info;
        if (!info.StateKeys(settings.TypeKey).Contains(stale.State))
        {
            return;
        }

        var unfinished = state.Length == 0 || info.StateCategory(settings.TypeKey, state) is { } category && UnfinishedCategories.Contains(category);
        if (!unfinished)
        {
            return;
        }

        row["state"] = stale.State;
        StaleCounts[stale] = StaleCounts.GetValueOrDefault(stale) + 1;
    }

    private static bool IsStale(MondayItem item, StaleRule stale)
    {
        var cutoff = new DateTimeOffset(stale.Before.ToDateTime(TimeOnly.MinValue), OrganizationOffset);
        var lastTouched = item.Updates
            .SelectMany(update => update.Replies.Select(reply => reply.CreatedAt).Append(update.CreatedAt))
            .Append(item.CreatedAt)
            .Max();
        return lastTouched < cutoff;
    }

    /// <summary>狀態標籤 → 狀態 key；對不上或不在流程裡記成問題並回 null。</summary>
    private string? MapState(MondayItem item, ItemSettings settings, string statusColumn)
    {
        var label = Value(item, statusColumn)?.Label ?? string.Empty;
        if (!settings.States.TryGetValue(label, out var state))
        {
            var shown = label.Length == 0 ? "（空白）" : $"「{label}」";
            issues.Add(NoteKinds.State, $"群組「{settings.GroupTitle}」的狀態標籤{shown}沒有對應（對應檔的 states）。");
            return null;
        }

        if (!project.Info.StateKeys(settings.TypeKey).Contains(state))
        {
            issues.Add(NoteKinds.State, $"專案 {project.Info.Key} 的類型「{settings.TypeKey}」的流程裡沒有狀態「{state}」（標籤「{label}」對到它）。");
            return null;
        }

        return state;
    }

    private void AddPriority(JsonObject row, MondayItem item, ItemSettings settings)
    {
        if (settings.PriorityColumn is null || Value(item, settings.PriorityColumn)?.Label is not { Length: > 0 } label)
        {
            return;
        }

        if (settings.Priorities.TryGetValue(label, out var priority))
        {
            row["priority"] = priority.ToLowerInvariant();
        }
        else
        {
            reminders.Add(NoteKinds.Priority, $"優先度標籤「{label}」沒有對應，當成 none");
        }
    }

    private void AddDates(JsonObject row, MondayItem item, ItemSettings settings)
    {
        var due = settings.DueColumn is null || Value(item, settings.DueColumn) is not { } dueValue ? null : MondayValues.Date(dueValue);
        var (start, end) = settings.TimelineColumn is null || Value(item, settings.TimelineColumn) is not { } timeline
            ? (null, null)
            : MondayValues.Timeline(timeline);
        due ??= end;
        if (start is { } from && due is { } until && from > until)
        {
            reminders.Add(NoteKinds.Value, "開始日比到期日晚，略過開始日");
            start = null;
        }

        if (start is { } startDate)
        {
            row["startDate"] = HistoryApi.Date(startDate);
        }

        if (due is { } dueDate)
        {
            row["dueDate"] = HistoryApi.Date(dueDate);
        }
    }

    private void AddPeople(JsonObject row, MondayItem item, ItemSettings settings)
    {
        var (assignee, followers) = Pick(item, settings);
        if (assignee is not null)
        {
            row["assigneeId"] = assignee.Id.ToString();
            project.Required.Add(assignee.Id);
        }

        if (followers.Count > MaxFollowers)
        {
            reminders.Add(NoteKinds.Followers, $"項目「{item.Name.Trim()}」的關注者超過 {MaxFollowers} 位，只留前 {MaxFollowers} 位");
        }

        var kept = followers.Take(MaxFollowers).ToArray();
        if (kept.Length > 0)
        {
            row["followers"] = new JsonArray([.. kept.Select(person => (JsonNode?)JsonValue.Create(person.Id.ToString()))]);
            project.Required.UnionWith(kept.Select(person => person.Id));
        }
    }

    /// <summary>
    /// 負責人與關注的人（<see cref="AssigneeRule"/>）：負責人欄裡對得上、而且是專案成員（有 --add-members 就不必是）的人，
    /// 去掉 exclude（去掉之後沒人時還是用他），取在這個項目留言最多的，一樣多取欄位裡排前面的；
    /// 其他人和留過言的人（followCommenters）變成關注的人。
    /// </summary>
    private (ServerPerson? Assignee, IReadOnlyList<ServerPerson> Followers) Pick(MondayItem item, ItemSettings settings)
    {
        var owners = settings.OwnerColumn is null ? [] : Value(item, settings.OwnerColumn)?.PersonIds.Distinct().ToArray() ?? [];
        var eligible = new List<(string Id, ServerPerson Person)>();
        foreach (var id in owners)
        {
            if (Eligible(id) is { } person)
            {
                eligible.Add((id, person));
            }
            else
            {
                reminders.Add(NoteKinds.Person, $"負責人欄的「{people.MondayName(id)}」對不上這裡的專案成員，沒有當負責人或關注的人");
            }
        }

        var excluded = mapping.Assignee.Exclude.ToHashSet(StringComparer.Ordinal);
        var pool = eligible.Where(candidate => !excluded.Contains(candidate.Id)).ToList();
        if (pool.Count == 0)
        {
            pool = eligible;
        }

        var counts = CommentThreads.Flatten(item).Where(comment => comment.CreatorId is not null)
            .GroupBy(comment => comment.CreatorId!).ToDictionary(group => group.Key, group => group.Count());
        var assignee = pool.Select((candidate, index) => (candidate, index))
            .OrderByDescending(entry => counts.GetValueOrDefault(entry.candidate.Id))
            .ThenBy(entry => entry.index)
            .Select(entry => entry.candidate.Person)
            .FirstOrDefault();

        var commenters = mapping.FollowCommenters
            ? counts.Keys.Select(Eligible).OfType<ServerPerson>()
            : [];
        var followers = eligible.Select(candidate => candidate.Person).Concat(commenters)
            .Where(person => person.Id != assignee?.Id)
            .DistinctBy(person => person.Id)
            .ToArray();
        return (assignee, followers);
    }

    private ServerPerson? Eligible(string mondayId) =>
        people.Match(mondayId) is { } person && (options.AddMembers || project.Info.Members.Contains(person.Id)) ? person : null;

    private void AddFields(JsonObject row, MondayItem item, ItemSettings settings)
    {
        var fields = new JsonObject();
        foreach (var (columnId, field) in settings.Fields)
        {
            if (project.Info.Field(field.Field) is not { } definition || !project.Info.TypeHasField(settings.TypeKey, field.Field)
                || Value(item, columnId) is not { } value
                || ProjectInfo.FieldType(definition) is not { } type || !FieldConverter.IsSupported(type, value.Type))
            {
                continue;
            }

            var conversion = FieldConverter.Convert(field.Field, type, field, value, ProjectInfo.OptionKeys(definition).ToHashSet(), Eligible);
            if (conversion.Reminder is not null)
            {
                reminders.Add(NoteKinds.Value, conversion.Reminder);
            }

            if (conversion.Value is not null)
            {
                fields[field.Field] = conversion.Value;
            }

            if (conversion.PersonId is { } personId)
            {
                project.Required.Add(personId);
            }
        }

        AddDefaultFields(fields, item, settings);
        if (settings.LinkField is { } link && FieldConverter.IsWebUrl(item.Url) && project.Info.TypeHasField(settings.TypeKey, link))
        {
            fields[link] = item.Url;
        }

        if (fields.Count > 0)
        {
            row["fields"] = fields;
        }
    }

    /// <summary>
    /// monday 的欄沒有值的欄位，依序補群組的 fields、第一條符合的 titleRule（只對項目）。欄位不在這個項目最後的類型上就略過並提醒，
    /// 不讓一條規則改了類型之後，整個項目被伺服器拒絕。
    /// </summary>
    private void AddDefaultFields(JsonObject fields, MondayItem item, ItemSettings settings)
    {
        var fromGroup = settings.DefaultFields ?? new Dictionary<string, string>();
        var fromRules = settings.TitleRules is { Count: > 0 } rules
            ? TitleRules.FieldsFor(rules, TitleRules.Normalize(item.Name))
            : new Dictionary<string, string>();
        var wanted = fromGroup.Select(pair => (pair.Key, pair.Value, IsRule: false))
            .Concat(fromRules.Where(pair => !fromGroup.ContainsKey(pair.Key)).Select(pair => (pair.Key, pair.Value, IsRule: true)));
        foreach (var (key, text, isRule) in wanted.Where(entry => !fields.ContainsKey(entry.Key)))
        {
            if (project.Info.Field(key) is not { } definition || !project.Info.TypeHasField(settings.TypeKey, key))
            {
                reminders.Add(NoteKinds.Field, $"專案 {project.Info.Key} 的類型「{settings.TypeKey}」沒有欄位「{key}」，群組與標題規則的值略過");
                continue;
            }

            var conversion = FieldConverter.FromText(key, ProjectInfo.FieldType(definition) ?? string.Empty, text, ProjectInfo.OptionKeys(definition).ToHashSet());
            if (conversion.Value is null)
            {
                continue;
            }

            fields[key] = conversion.Value;
            if (isRule)
            {
                var name = project.Info.FieldName(key);
                RuleFieldCounts[name] = RuleFieldCounts.GetValueOrDefault(name) + 1;
            }
        }
    }

    private static MondayColumnValue? Value(MondayItem item, string columnId) =>
        item.Values.FirstOrDefault(value => value.ColumnId == columnId);
}
