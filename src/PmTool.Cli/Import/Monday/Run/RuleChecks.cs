namespace PmTool.Cli.Import.Monday.Run;

/// <summary>titleRules 與群組 fields 對藍圖的檢查：類型存在、欄位掛在會套用的類型上、固定的值是那個欄位收得下的。</summary>
internal static class RuleChecks
{
    /// <summary>
    /// 檢查這個群組會用到的規則。沒給 project 的規則在每個會用到它的專案各檢查一次，用不到的專案不報；
    /// 沒給 type 的規則的欄位會套在群組的類型上。<paramref name="seen"/> 讓同一條規則在同一個專案與類型只檢查一次。
    /// </summary>
    public static void Check(GroupWork work, ProjectInfo info, ISet<string> seen, Notes issues)
    {
        var rules = work.BoardMapping.TitleRules;
        var groupTypeExists = info.ItemType(work.TypeKey) is not null;
        for (var index = 0; index < rules.Count; index++)
        {
            var rule = rules[index];
            var typeKey = rule.Type ?? work.TypeKey;
            if (!TitleRules.AppliesTo(rule, info.Key) || !seen.Add($"{work.Board.Id}|{info.Key}|{index}|{typeKey}"))
            {
                continue;
            }

            var where = $"看板 {work.Board.Id} 的 titleRules 第 {index + 1} 條";
            if (rule.Type is not null)
            {
                if (!ProjectChecks.TypeExists(info, rule.Type, where, issues))
                {
                    continue;
                }

                ImportPlanner.CheckStale(work.BoardMapping, info, rule.Type, where, issues);
            }
            else if (!groupTypeExists)
            {
                continue;
            }

            CheckValues(info, typeKey, rule.Fields, where, issues);
        }

        if (groupTypeExists)
        {
            CheckValues(info, work.TypeKey, work.GroupMapping.Fields, $"群組「{work.Title}」", issues);
        }
    }

    private static void CheckValues(ProjectInfo info, string typeKey, IReadOnlyDictionary<string, string> fields, string where, Notes issues)
    {
        var prefix = $"專案 {info.Key} 的類型「{typeKey}」";
        foreach (var (key, text) in fields)
        {
            if (info.Field(key) is not { } definition || !info.TypeHasField(typeKey, key))
            {
                issues.Add(NoteKinds.Field, $"{prefix}沒有欄位「{key}」：藍圖裡沒有，或沒有掛在這個類型上（{where}的 fields）。");
                continue;
            }

            var conversion = FieldConverter.FromText(key, ProjectInfo.FieldType(definition) ?? string.Empty, text, ProjectInfo.OptionKeys(definition).ToHashSet());
            if (conversion.Reminder is { } problem)
            {
                issues.Add(NoteKinds.Field, $"{prefix}：{problem}（{where}的 fields）。");
            }
        }
    }
}
