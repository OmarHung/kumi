using System.Globalization;
using System.Text;

namespace PmTool.Cli.Import.Monday;

/// <summary>
/// <see cref="MondayMapping"/> → YAML。自己寫而不用序列化器：要照人看得懂的順序、帶註解，
/// 而且空的值要寫成留白（草稿裡「這裡要填」的地方）。字串一律在需要時加引號，所以 monday 給的名稱不會變成 YAML 的語法。
/// </summary>
internal sealed class MondayMappingWriter(MappingAnnotations annotations)
{
    private const int CommentColumn = 34;
    private const int Indent = 2;
    private static readonly string[] ReservedWords = ["null", "true", "false", "yes", "no", "on", "off", "y", "n", "~"];

    private readonly List<string> _lines = [];

    public string Write(MondayMapping mapping)
    {
        foreach (var line in annotations.Header)
        {
            _lines.Add($"# {MappingAnnotations.Clean(line)}");
        }

        Line(0, $"source: {Scalar(mapping.Source)}", null);
        Line(0, $"dump: {Scalar(mapping.Dump)}", null);
        WritePeople(mapping);
        Blank("fallbackPerson", 0, mapping.FallbackPerson, string.Empty);
        Line(0, "assignee:", null);
        Line(Indent, $"exclude: [{string.Join(", ", mapping.Assignee.Exclude.Select(Scalar))}]", Trailing("assignee"));
        Line(0, $"followCommenters: {Flag(mapping.FollowCommenters)}", Trailing("followCommenters"));
        Line(0, $"includeArchived: {Flag(mapping.IncludeArchived)}", Trailing("includeArchived"));
        WriteBoards(mapping);
        return string.Join('\n', _lines) + "\n";
    }

    private void WritePeople(MondayMapping mapping)
    {
        Line(0, mapping.People.Count == 0 ? "people: {}" : "people:", Trailing("people"));
        foreach (var (id, email) in mapping.People)
        {
            Above(MappingAnnotations.Key("people", id), Indent);
            Line(Indent, $"{Scalar(id)}: {(email is null ? "null" : Scalar(email))}", Trailing("people", id));
        }
    }

    private void WriteBoards(MondayMapping mapping)
    {
        Line(0, mapping.Boards.Count == 0 ? "boards: {}" : "boards:", null);
        foreach (var (id, board) in mapping.Boards)
        {
            var path = MappingAnnotations.Key("boards", id);
            Above(path, Indent);
            Line(Indent, $"{Scalar(id)}:", Trailing("boards", id));
            WriteBoard(board, path);
        }
    }

    private void WriteBoard(BoardMapping board, string path)
    {
        const int level = Indent * 2;
        Blank("statusColumn", level, board.StatusColumn, path);
        Blank("ownerColumn", level, board.OwnerColumn, path);
        Blank("dueColumn", level, board.DueColumn, path);
        Blank("timelineColumn", level, board.TimelineColumn, path);
        Blank("priorityColumn", level, board.PriorityColumn, path);
        WriteMap("priorities", level, board.Priorities, path);
        WriteMap("states", level, board.States, path);
        WriteFields(board, path);
        Blank("linkField", level, board.LinkField, path);
        WriteStale(board.Stale, level, path);
        WriteTitleRules(board.TitleRules, level);
        if (board.Subitems is { } subitems)
        {
            WriteSubitems(subitems, path);
        }

        WriteGroups(board, path);
    }

    /// <summary>沒有 stale 時什麼都不寫，只放 plan 的草稿附的（註解掉的）範例；有的話寫成兩行設定。</summary>
    private void WriteStale(StaleRule? stale, int level, string path)
    {
        var key = MappingAnnotations.Key(path, "stale");
        Above(key, level);
        if (stale is null)
        {
            return;
        }

        Line(level, "stale:", Trailing(key));
        Line(level + Indent, $"before: {Date(stale.Before)}", null);
        Line(level + Indent, $"state: {Scalar(stale.State)}", null);
    }

    private void WriteTitleRules(IReadOnlyList<TitleRule> rules, int level)
    {
        if (rules.Count == 0)
        {
            return;
        }

        Line(level, "titleRules:", null);
        foreach (var rule in rules)
        {
            var parts = new List<string>();
            if (rule.Project is not null)
            {
                parts.Add($"project: {Scalar(rule.Project)}");
            }

            parts.Add($"match: [{string.Join(", ", rule.Match.Select(Scalar))}]");
            if (rule.Type is not null)
            {
                parts.Add($"type: {Scalar(rule.Type)}");
            }

            if (rule.Fields.Count > 0)
            {
                parts.Add($"fields: {FlowMap(rule.Fields)}");
            }

            Line(level + Indent, $"- {{ {string.Join(", ", parts)} }}", null);
        }
    }

    private static string FlowMap(IReadOnlyDictionary<string, string> map) =>
        $"{{ {string.Join(", ", map.Select(pair => $"{Scalar(pair.Key)}: {Scalar(pair.Value)}"))} }}";

    private void WriteFields(BoardMapping board, string path)
    {
        if (board.Fields.Count == 0)
        {
            return;
        }

        var fieldsPath = MappingAnnotations.Key(path, "fields");
        Above(fieldsPath, Indent * 2);
        Line(Indent * 2, "fields:", null);
        foreach (var (column, field) in board.Fields)
        {
            var columnPath = MappingAnnotations.Key(fieldsPath, column);
            Above(columnPath, Indent * 3);
            if (field.Values.Count == 0)
            {
                Line(Indent * 3, $"{Scalar(column)}:{(field.Field.Length == 0 ? string.Empty : " " + Scalar(field.Field))}", Trailing(columnPath));
                continue;
            }

            Line(Indent * 3, $"{Scalar(column)}:", Trailing(columnPath));
            Line(Indent * 4, $"field: {Scalar(field.Field)}", null);
            WriteMap("values", Indent * 4, field.Values, columnPath);
        }
    }

    private void WriteSubitems(SubitemMapping subitems, string path)
    {
        var subPath = MappingAnnotations.Key(path, "subitems");
        const int level = Indent * 3;
        Above(subPath, Indent * 2);
        Line(Indent * 2, "subitems:", Trailing(subPath));
        Line(level, $"type: {Scalar(subitems.Type)}", null);
        Blank("statusColumn", level, subitems.StatusColumn, subPath);
        Blank("ownerColumn", level, subitems.OwnerColumn, subPath);
        Blank("dueColumn", level, subitems.DueColumn, subPath);
        Blank("timelineColumn", level, subitems.TimelineColumn, subPath);
        if (subitems.States is { } states)
        {
            WriteMap("states", level, states, subPath);
        }
    }

    private void WriteGroups(BoardMapping board, string path)
    {
        var groupsPath = MappingAnnotations.Key(path, "groups");
        Above(groupsPath, Indent * 2);
        Line(Indent * 2, board.Groups.Count == 0 ? "groups: {}" : "groups:", null);
        foreach (var (id, group) in board.Groups)
        {
            var groupPath = MappingAnnotations.Key(groupsPath, id);
            Above(groupPath, Indent * 3);
            if (group.Skip)
            {
                Line(Indent * 3, $"{Scalar(id)}: skip", Trailing(groupPath));
                continue;
            }

            Line(Indent * 3, $"{Scalar(id)}:", Trailing(groupPath));
            const int level = Indent * 4;
            Blank("project", level, group.Project, groupPath);
            Blank("type", level, group.Type, groupPath);
            if (group.Cycle is { Monthly: true })
            {
                Line(level, "cycle: monthly", Trailing(groupPath, "cycle"));
            }
            else if (group.Cycle is { } cycle)
            {
                Line(level, $"cycle: {{ name: {Scalar(cycle.Name)}, start: {Date(cycle.Start)}, end: {Date(cycle.End)} }}", Trailing(groupPath, "cycle"));
            }

            if (group.States is { } states)
            {
                WriteMap("states", level, states, groupPath);
            }

            if (group.Fields.Count > 0)
            {
                Line(level, $"fields: {FlowMap(group.Fields)}", Trailing(groupPath, "fields"));
            }
        }
    }

    /// <summary>「鍵: 值」；值是空的就只寫「鍵:」留白（草稿裡要人填的地方）。</summary>
    private void Blank(string name, int level, string? value, string path)
    {
        var key = path.Length == 0 ? name : MappingAnnotations.Key(path, name);
        Above(key, level);
        Line(level, string.IsNullOrEmpty(value) ? $"{name}:" : $"{name}: {Scalar(value)}", Trailing(key));
    }

    /// <summary>標籤 → key 的對應表，一行一個（才有地方放註解）；空的對應表不寫。值是空字串的寫成留白。</summary>
    private void WriteMap(string name, int level, IReadOnlyDictionary<string, string> map, string path)
    {
        if (map.Count == 0)
        {
            return;
        }

        var mapPath = MappingAnnotations.Key(path, name);
        Above(mapPath, level);
        Line(level, $"{name}:", Trailing(mapPath));
        foreach (var (label, value) in map)
        {
            var entryPath = MappingAnnotations.Key(mapPath, label);
            Above(entryPath, level + Indent);
            Line(level + Indent, $"{Quote(label)}:{(value.Length == 0 ? string.Empty : " " + Scalar(value))}", Trailing(entryPath));
        }
    }

    private void Above(string key, int level)
    {
        if (annotations.Above.TryGetValue(key, out var lines))
        {
            foreach (var line in lines)
            {
                _lines.Add($"{new string(' ', level)}# {MappingAnnotations.Clean(line)}");
            }
        }
    }

    private string? Trailing(params string[] key) =>
        annotations.Trailing.TryGetValue(MappingAnnotations.Key(key), out var comment) ? MappingAnnotations.Clean(comment).Trim() : null;

    private void Line(int level, string content, string? comment)
    {
        var text = new string(' ', level) + content;
        if (!string.IsNullOrEmpty(comment))
        {
            text = text.PadRight(CommentColumn - 2) + "  # " + comment;
        }

        _lines.Add(text);
    }

    private static string Flag(bool value) => value ? "true" : "false";

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// 純英數與少數符號的字串直接寫；其他（空白、中文、冒號、#、引號、像數字或布林的）一律用雙引號包起來。
    /// 數字樣的也加引號：不加的話，別的 YAML 工具會把 "1001" 讀成整數。
    /// </summary>
    internal static string Scalar(string value)
    {
        var plain = value.Length > 0
            && (char.IsAsciiLetter(value[0]) || value[0] == '_')
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.' or '@' or '/')
            && !ReservedWords.Contains(value, StringComparer.OrdinalIgnoreCase);
        return plain ? value : Quote(value);
    }

    internal static string Quote(string value)
    {
        var builder = new StringBuilder("\"");
        foreach (var character in value)
        {
            builder.Append(character switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when char.IsControl(character) || character is MappingAnnotations.LineSeparator or MappingAnnotations.ParagraphSeparator => $"\\u{(int)character:X4}",
                _ => character.ToString(),
            });
        }

        return builder.Append('"').ToString();
    }
}
