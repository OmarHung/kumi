namespace PmTool.Cli.Import.Monday.Run;

/// <summary>對應檔對照伺服器上的專案：專案能不能寫、類型與欄位存在、欄位的種類收得下 monday 的欄。</summary>
internal static class ProjectChecks
{
    public static void CheckProject(ProjectInfo info, Notes issues)
    {
        // 封存的專案不在這裡擋：重跑時沒有新東西就略過，有新東西才在寫入之前停下（ArchivedProjects）。
        if (!info.CanManage)
        {
            issues.Add(NoteKinds.Project, $"你在專案 {info.Key} 沒有 project.manage 權限；匯入要替別人署名、指定過去的時間，只有專案的管理者做得到。");
        }
    }

    public static bool TypeExists(ProjectInfo info, string typeKey, string where, Notes issues)
    {
        if (info.ItemType(typeKey) is not null)
        {
            return true;
        }

        issues.Add(NoteKinds.Type, $"專案 {info.Key} 沒有項目類型「{typeKey}」（{where}）。");
        return false;
    }

    /// <summary>
    /// 自訂欄位與 linkField 對藍圖的檢查。回傳這個專案與類型「沒有」的欄位（對應檔的欄編號）：
    /// 藍圖裡沒有，或沒掛在這個類型上。沒有不一定是錯（「客戶」欄位可能只有維運專案有）：
    /// 呼叫端略過那些值並提醒，所有目標都沒有時才當成問題。
    /// </summary>
    public static IReadOnlySet<string> CheckFields(
        ProjectInfo info, string typeKey, BoardMapping mapping, IReadOnlyList<MondayColumn> columns, Notes issues)
    {
        var prefix = $"專案 {info.Key} 的類型「{typeKey}」";
        var unavailable = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (columnId, field) in mapping.Fields)
        {
            if (FieldDefinition(info, typeKey, field.Field) is not { } definition)
            {
                unavailable.Add(columnId);
                continue;
            }

            var column = columns.FirstOrDefault(candidate => candidate.Id == columnId);
            if (column is null)
            {
                continue;
            }

            var fieldType = ProjectInfo.FieldType(definition) ?? string.Empty;
            if (!FieldConverter.IsSupported(fieldType, column.Type))
            {
                issues.Add(NoteKinds.Field, $"{prefix}：欄位「{field.Field}」（{fieldType}）收不下 monday 的欄「{column.Title}」（{column.Type}）；它收：{FieldConverter.SupportedColumns(fieldType)}。");
            }

            CheckOptions(field, definition, prefix, issues);
        }

        if (mapping.LinkField is { } link)
        {
            if (FieldDefinition(info, typeKey, link) is not { } linkDefinition)
            {
                issues.Add(NoteKinds.Field, $"{prefix}：linkField「{link}」在藍圖裡沒有，或沒有掛在這個類型上。");
            }
            else if (ProjectInfo.FieldType(linkDefinition) != "url")
            {
                issues.Add(NoteKinds.Field, $"{prefix}：linkField「{link}」要是 url 欄位，它是 {ProjectInfo.FieldType(linkDefinition)}。");
            }
        }

        return unavailable;
    }

    /// <summary>欄位要存在、而且掛在這個類型上；找得到回藍圖裡的定義。</summary>
    private static System.Text.Json.JsonElement? FieldDefinition(ProjectInfo info, string typeKey, string field) =>
        info.Field(field) is { } definition && info.TypeHasField(typeKey, field) ? definition : null;

    private static void CheckOptions(FieldMapping field, System.Text.Json.JsonElement definition, string prefix, Notes issues)
    {
        if (ProjectInfo.FieldType(definition) is not ("single_select" or "multi_select"))
        {
            return;
        }

        var options = ProjectInfo.OptionKeys(definition);
        foreach (var (value, option) in field.Values.Where(pair => !options.Contains(pair.Value)))
        {
            issues.Add(NoteKinds.Field, $"{prefix}：欄位「{field.Field}」沒有選項「{option}」（對應檔把「{value}」對到它）。");
        }
    }
}
