using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace PmTool.Cli.Import.Monday;

/// <summary>
/// 對應檔的 YAML → <see cref="MondayMapping"/>。不用反序列化器：要認簡寫（群組寫 skip、欄位直接寫欄位的 key）、
/// 擋不認得的鍵（打錯字不能默默被忽略，不然會少匯東西），而且每個錯誤都要帶行號。
/// 值留空（寫 null、~ 或什麼都不寫）的對應表項目視為還沒填，直接略過，plan 產生的草稿就是這樣。
/// </summary>
internal sealed class MondayMappingParser(string fileName)
{
    private static readonly string[] RootKeys = ["source", "dump", "people", "fallbackPerson", "assignee", "followCommenters", "includeArchived", "boards"];
    private static readonly string[] BoardKeys =
        ["statusColumn", "ownerColumn", "dueColumn", "timelineColumn", "priorityColumn", "priorities", "states", "fields", "linkField", "subitems", "stale", "titleRules", "groups"];
    private static readonly string[] SubitemKeys = ["type", "statusColumn", "ownerColumn", "dueColumn", "timelineColumn", "states"];
    private static readonly string[] GroupKeys = ["skip", "project", "type", "cycle", "states", "fields"];
    private static readonly string[] TitleRuleKeys = ["project", "match", "type", "fields"];
    private static readonly string[] FieldKeys = ["field", "values"];
    private static readonly string[] StaleKeys = ["before", "state"];
    private static readonly string[] CycleKeys = ["name", "start", "end"];
    private const string SkipWord = "skip";
    private const string MonthlyWord = "monthly";
    private const string DateFormat = "yyyy-MM-dd";

    public MondayMapping Parse(string yaml, string baseDirectory)
    {
        var root = ReadRoot(yaml);
        var map = AsMap(root, "（最上層）");
        CheckKeys(map, "（最上層）", RootKeys);
        var dump = Text(map, "dump") ?? throw Fail(map, "dump", "缺少 dump（fetch 輸出的資料夾）");
        return new MondayMapping
        {
            Source = Text(map, "source") ?? "monday",
            Dump = Path.GetFullPath(dump, baseDirectory),
            People = ParsePeople(Get(map, "people")),
            FallbackPerson = Text(map, "fallbackPerson"),
            Assignee = ParseAssignee(Get(map, "assignee")),
            FollowCommenters = Flag(map, "followCommenters", true),
            IncludeArchived = Flag(map, "includeArchived", false),
            Boards = ParseBoards(Get(map, "boards")),
        };
    }

    private YamlNode ReadRoot(string yaml)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException exception)
        {
            throw new CliException(ExitCodes.Invalid, $"對應檔 {fileName} 第 {exception.Start.Line} 行：不是合法的 YAML（{exception.Message}）");
        }

        return stream.Documents.Count == 0
            ? throw new CliException(ExitCodes.Invalid, $"對應檔 {fileName} 是空的。")
            : stream.Documents[0].RootNode;
    }

    private IReadOnlyDictionary<string, string?> ParsePeople(YamlNode? node)
    {
        var people = new Dictionary<string, string?>();
        foreach (var (key, value) in Pairs(Optional(node, "people")))
        {
            people[key] = IsBlank(value) ? null : ScalarText(value, $"people/{key}");
        }

        return people;
    }

    private AssigneeRule ParseAssignee(YamlNode? node)
    {
        if (node is null || IsBlank(node))
        {
            return new AssigneeRule();
        }

        var map = AsMap(node, "assignee");
        CheckKeys(map, "assignee", ["exclude"]);
        if (Get(map, "exclude") is not { } exclude || IsBlank(exclude))
        {
            return new AssigneeRule();
        }

        var list = exclude as YamlSequenceNode ?? throw Fail(exclude, "assignee/exclude", "要是一個清單（例如 [\"1001\"]）");
        return new AssigneeRule { Exclude = [.. list.Children.Select(item => ScalarText(item, "assignee/exclude"))] };
    }

    private IReadOnlyDictionary<string, BoardMapping> ParseBoards(YamlNode? node)
    {
        var boards = new Dictionary<string, BoardMapping>();
        foreach (var (key, value) in Pairs(Optional(node, "boards")))
        {
            boards[key] = ParseBoard(AsMap(value, $"boards/{key}"), $"boards/{key}");
        }

        return boards;
    }

    private BoardMapping ParseBoard(YamlMappingNode map, string path)
    {
        CheckKeys(map, path, BoardKeys);
        return new BoardMapping
        {
            StatusColumn = Text(map, "statusColumn"),
            OwnerColumn = Text(map, "ownerColumn"),
            DueColumn = Text(map, "dueColumn"),
            TimelineColumn = Text(map, "timelineColumn"),
            PriorityColumn = Text(map, "priorityColumn"),
            Priorities = StringMap(Get(map, "priorities"), $"{path}/priorities"),
            States = StringMap(Get(map, "states"), $"{path}/states"),
            Fields = ParseFields(Get(map, "fields"), $"{path}/fields"),
            LinkField = Text(map, "linkField"),
            Subitems = Get(map, "subitems") is { } subitems && !IsBlank(subitems) ? ParseSubitems(subitems, $"{path}/subitems") : null,
            Stale = Get(map, "stale") is { } stale && !IsBlank(stale) ? ParseStale(stale, $"{path}/stale") : null,
            TitleRules = ParseTitleRules(Get(map, "titleRules"), $"{path}/titleRules"),
            Groups = ParseGroups(Get(map, "groups"), $"{path}/groups"),
        };
    }

    private StaleRule ParseStale(YamlNode node, string path)
    {
        var map = AsMap(node, path);
        CheckKeys(map, path, StaleKeys);
        return new StaleRule(
            Date(map, path, "before"),
            Text(map, "state") ?? throw Fail(map, path, "缺少 state（改成的狀態 key，例如 canceled）"));
    }

    private IReadOnlyDictionary<string, FieldMapping> ParseFields(YamlNode? node, string path)
    {
        var fields = new Dictionary<string, FieldMapping>();
        foreach (var (key, value) in Pairs(Optional(node, path)))
        {
            // 值留空：plan 列出來讓人決定的欄，還沒填就當作不匯。
            if (IsBlank(value))
            {
                continue;
            }

            fields[key] = value is YamlScalarNode
                ? new FieldMapping { Field = ScalarText(value, $"{path}/{key}") }
                : ParseField(AsMap(value, $"{path}/{key}"), $"{path}/{key}");
        }

        return fields;
    }

    private FieldMapping ParseField(YamlMappingNode map, string path)
    {
        CheckKeys(map, path, FieldKeys);
        return new FieldMapping
        {
            Field = Text(map, "field") ?? throw Fail(map, path, "缺少 field（自訂欄位的 key）"),
            Values = StringMap(Get(map, "values"), $"{path}/values"),
        };
    }

    private SubitemMapping ParseSubitems(YamlNode node, string path)
    {
        var map = AsMap(node, path);
        CheckKeys(map, path, SubitemKeys);
        return new SubitemMapping
        {
            Type = Text(map, "type") ?? throw Fail(map, path, "缺少 type（子項目的類型 key）"),
            StatusColumn = Text(map, "statusColumn"),
            OwnerColumn = Text(map, "ownerColumn"),
            DueColumn = Text(map, "dueColumn"),
            TimelineColumn = Text(map, "timelineColumn"),
            States = Get(map, "states") is { } states && !IsBlank(states) ? StringMap(states, $"{path}/states") : null,
        };
    }

    private IReadOnlyDictionary<string, GroupMapping> ParseGroups(YamlNode? node, string path)
    {
        var groups = new Dictionary<string, GroupMapping>();
        foreach (var (key, value) in Pairs(Optional(node, path)))
        {
            groups[key] = ParseGroup(value, $"{path}/{key}");
        }

        return groups;
    }

    private GroupMapping ParseGroup(YamlNode node, string path)
    {
        if (IsBlank(node))
        {
            return new GroupMapping();
        }

        if (node is YamlScalarNode scalar)
        {
            return string.Equals(scalar.Value?.Trim(), SkipWord, StringComparison.OrdinalIgnoreCase)
                ? new GroupMapping { Skip = true }
                : throw Fail(node, path, $"簡寫只能寫 {SkipWord}；要匯的群組寫成 project 與 type 的設定");
        }

        var map = AsMap(node, path);
        CheckKeys(map, path, GroupKeys);
        return new GroupMapping
        {
            Skip = Flag(map, "skip", false),
            Project = Text(map, "project"),
            Type = Text(map, "type"),
            Cycle = Get(map, "cycle") is { } cycle && !IsBlank(cycle) ? ParseCycle(cycle, $"{path}/cycle") : null,
            States = Get(map, "states") is { } states && !IsBlank(states) ? StringMap(states, $"{path}/states") : null,
            Fields = StringMap(Get(map, "fields"), $"{path}/fields"),
        };
    }

    private IReadOnlyList<TitleRule> ParseTitleRules(YamlNode? node, string path)
    {
        if (node is null || IsBlank(node))
        {
            return [];
        }

        var list = node as YamlSequenceNode ?? throw Fail(node, path, "要是一個清單（- { match: [...], type: ... }）");
        return [.. list.Children.Select((rule, index) => ParseTitleRule(rule, $"{path}/{index + 1}"))];
    }

    private TitleRule ParseTitleRule(YamlNode node, string path)
    {
        var map = AsMap(node, path);
        CheckKeys(map, path, TitleRuleKeys);
        var match = Get(map, "match");
        if (match is not YamlSequenceNode sequence)
        {
            throw Fail(match ?? map, $"{path}/match", "缺少 match，或不是清單（例如 [漏洞, 資安]）");
        }

        var words = sequence.Children.Select(word => ScalarText(word, $"{path}/match").Trim()).ToArray();
        if (words.Length == 0 || words.Any(word => word.Length == 0))
        {
            throw Fail(match, $"{path}/match", "不能是空的清單，清單裡也不能有空的字");
        }

        var rule = new TitleRule
        {
            Project = Text(map, "project"),
            Match = words,
            Type = Text(map, "type"),
            Fields = StringMap(Get(map, "fields"), $"{path}/fields"),
        };
        return rule.Type is null && rule.Fields.Count == 0
            ? throw Fail(map, path, "要有 type 或 fields（符合之後要做什麼）")
            : rule;
    }

    private CycleMapping ParseCycle(YamlNode node, string path)
    {
        if (node is YamlScalarNode scalar)
        {
            return string.Equals(scalar.Value?.Trim(), MonthlyWord, StringComparison.OrdinalIgnoreCase)
                ? CycleMapping.MonthlyByCreation
                : throw Fail(node, path, $"簡寫只能寫 {MonthlyWord}（依項目建立的月份）；固定的週期寫成 {{ name, start, end }}");
        }

        var map = AsMap(node, path);
        CheckKeys(map, path, CycleKeys);
        var name = Text(map, "name") ?? throw Fail(map, path, "缺少 name（週期的名稱）");
        var start = Date(map, path, "start");
        var end = Date(map, path, "end");
        return end < start
            ? throw Fail(map, path, $"end（{end:yyyy-MM-dd}）不能早於 start（{start:yyyy-MM-dd}）")
            : new CycleMapping(name, start, end);
    }

    private DateOnly Date(YamlMappingNode map, string path, string key)
    {
        var node = Get(map, key) ?? throw Fail(map, path, $"缺少 {key}（日期，格式 {DateFormat}）");
        var text = ScalarText(node, $"{path}/{key}");
        return DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw Fail(node, $"{path}/{key}", $"「{text}」不是日期，格式要是 {DateFormat}（例如 2026-11-01）");
    }

    /// <summary>標籤 → key 的對應表；值留空的略過（還沒填）。</summary>
    private Dictionary<string, string> StringMap(YamlNode? node, string path)
    {
        var result = new Dictionary<string, string>();
        foreach (var (key, value) in Pairs(Optional(node, path)))
        {
            if (!IsBlank(value))
            {
                result[key] = ScalarText(value, $"{path}/{key}");
            }
        }

        return result;
    }

    private YamlMappingNode? Optional(YamlNode? node, string path) =>
        node is null || IsBlank(node) ? null : AsMap(node, path);

    private IEnumerable<(string Key, YamlNode Value)> Pairs(YamlMappingNode? map) =>
        map is null
            ? []
            : map.Children.Select(pair => (ScalarText(pair.Key, "（鍵）"), pair.Value));

    private static YamlNode? Get(YamlMappingNode map, string key) =>
        map.Children.Where(pair => pair.Key is YamlScalarNode scalar && scalar.Value == key).Select(pair => pair.Value).FirstOrDefault();

    private string? Text(YamlMappingNode map, string key)
    {
        var node = Get(map, key);
        return node is null || IsBlank(node) ? null : ScalarText(node, key).Trim() is { Length: > 0 } text ? text : null;
    }

    private bool Flag(YamlMappingNode map, string key, bool fallback)
    {
        var node = Get(map, key);
        if (node is null || IsBlank(node))
        {
            return fallback;
        }

        return ScalarText(node, key).Trim().ToLowerInvariant() switch
        {
            "true" => true,
            "false" => false,
            _ => throw Fail(node, key, "要寫 true 或 false"),
        };
    }

    private string ScalarText(YamlNode node, string path) =>
        node is YamlScalarNode scalar ? scalar.Value ?? string.Empty : throw Fail(node, path, "要是一個文字，不是清單或對應表");

    private YamlMappingNode AsMap(YamlNode node, string path) =>
        node as YamlMappingNode ?? throw Fail(node, path, "要是一個對應表（鍵: 值）");

    private void CheckKeys(YamlMappingNode map, string path, string[] allowed)
    {
        foreach (var pair in map.Children)
        {
            var name = (pair.Key as YamlScalarNode)?.Value ?? string.Empty;
            if (!allowed.Contains(name, StringComparer.Ordinal))
            {
                throw Fail(pair.Key, path, $"不認得的鍵「{name}」。這裡可以用：{string.Join("、", allowed)}");
            }
        }
    }

    private static bool IsBlank(YamlNode node) =>
        node is YamlScalarNode { Style: ScalarStyle.Plain, Value: null or "" or "~" or "null" or "Null" or "NULL" };

    private CliException Fail(YamlNode node, string path, string message) =>
        new(ExitCodes.Invalid, $"對應檔 {fileName} 第 {node.Start.Line} 行（{path}）：{message}");
}
