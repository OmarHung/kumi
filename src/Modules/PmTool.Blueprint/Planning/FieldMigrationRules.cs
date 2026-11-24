using PmTool.Blueprint.Model;
using PmTool.Blueprint.Validation;
using static PmTool.Blueprint.IssueText;

namespace PmTool.Blueprint.Planning;

/// <summary>
/// 護欄：拿掉欄位要先確認資料可以封存；改欄位型別只允許有定義的轉換，可能轉不過去的要寫明怎麼處理。
/// 另外兩種修改一樣會讓既有的值失去意義，變更集卻還沒有地方交代，所以先擋下：金額欄位換幣別、選項型欄位拿掉選項。
/// </summary>
internal static class FieldMigrationRules
{
    private const string DropHintsPath = "/migrations/dropFields";
    private const string ConvertHintsPath = "/migrations/convertFields";
    private const string NewFieldsPath = "/blueprint/fields";

    public static MigrationOutcome Check(ProjectBlueprint? current, ChangeSetRequest changeSet)
    {
        var migrations = changeSet.Migrations ?? new MigrationHints();
        var currentFields = current?.Fields ?? [];
        return MigrationOutcome.Combine(
            CheckRemovedFields(currentFields, changeSet.Blueprint.Fields, migrations.DropFields),
            CheckChangedFields(currentFields, changeSet.Blueprint.Fields, migrations.ConvertFields));
    }

    private static MigrationOutcome CheckRemovedFields(
        IReadOnlyList<FieldDefinition> current, IReadOnlyList<FieldDefinition> next, IReadOnlyList<string> hints)
    {
        var remaining = next.Select(field => field.Key).ToHashSet(StringComparer.Ordinal);
        var currentPositions = KeyPositions.First(current, field => field.Key);
        return HintMatcher.Match(
            current.Select(field => field.Key).Distinct(StringComparer.Ordinal).Where(key => !remaining.Contains(key)),
            hints,
            hint => hint,
            (field, _) => new FieldDropImpact(field),
            field => new ValidationIssue(
                IssueCodes.PlanFieldDropRequired,
                DropHintsPath,
                $"欄位{NameOf(current[currentPositions[field]])}不在新的藍圖裡了，但沒有確認它的資料可以封存；請把它列在 migrations.dropFields。"),
            (index, _) => new ValidationIssue(
                IssueCodes.PlanUnusedMigration,
                $"{DropHintsPath}/{index}",
                $"這條指示對不到任何修改：欄位{Quote(hints[index])}原本就不在目前的藍圖裡，或前面已經列過一次。"));
    }

    /// <summary>兩版都有的欄位：型別、幣別、選項有沒有改到會影響既有值的地方。依新藍圖的欄位順序。</summary>
    private static MigrationOutcome CheckChangedFields(
        IReadOnlyList<FieldDefinition> current, IReadOnlyList<FieldDefinition> next, IReadOnlyList<FieldConversion> hints)
    {
        var currentPositions = KeyPositions.First(current, field => field.Key);
        var nextPositions = KeyPositions.First(next, field => field.Key);
        var hintPositions = KeyPositions.First(hints, hint => hint.Field);
        var outcomes = new List<MigrationOutcome>();
        var used = new HashSet<int>();
        for (var index = 0; index < next.Count; index++)
        {
            var after = next[index];
            if (nextPositions[after.Key] != index || !currentPositions.TryGetValue(after.Key, out var position))
            {
                continue;
            }

            var before = current[position];
            var path = $"{NewFieldsPath}/{index}";
            if (before.Type != after.Type)
            {
                // 欄位確實換了型別，寫給它的指示就不算寫錯，即使這個轉換用不到失敗時的處理方式。
                var hint = hintPositions.TryGetValue(after.Key, out var hintPosition) ? hints[hintPosition] : null;
                used.UnionWith(hint is null ? [] : [hintPosition]);
                outcomes.Add(ConvertType(before, after, hint, path));
            }

            outcomes.Add(MigrationOutcome.Of(CurrencyChange(before, after, path).Concat(RemovedOptions(before, after, path))));
        }

        outcomes.Add(MigrationOutcome.Of(HintMatcher.Unused(hints.Count, used).Select(index => new ValidationIssue(
            IssueCodes.PlanUnusedMigration,
            $"{ConvertHintsPath}/{index}",
            $"這條指示對不到任何修改：欄位{Quote(hints[index].Field)}的型別沒有改變，或前面已經有一條相同的指示。"))));
        return MigrationOutcome.Combine(outcomes);
    }

    private static MigrationOutcome ConvertType(FieldDefinition before, FieldDefinition after, FieldConversion? hint, string path)
    {
        var change = $"欄位{NameOf(before)}從 {DocumentNames.Of(before.Type)} 改成 {DocumentNames.Of(after.Type)}";
        return FieldConversions.Classify(before.Type, after.Type) switch
        {
            ConversionSafety.Lossless =>
                MigrationOutcome.Of(new FieldConversionImpact(after.Key, before.Type, after.Type, OnFailure: null)),
            ConversionSafety.NeedsFailurePolicy when hint is not null =>
                MigrationOutcome.Of(new FieldConversionImpact(after.Key, before.Type, after.Type, hint.OnFailure)),
            ConversionSafety.NeedsFailurePolicy =>
                MigrationOutcome.Of(new ValidationIssue(
                    IssueCodes.PlanFieldConversionRequired,
                    ConvertHintsPath,
                    $"{change}，既有的值不一定轉得過去；請在 migrations.convertFields 寫明轉不過去時怎麼處理。")),
            _ =>
                MigrationOutcome.Of(new ValidationIssue(
                    IssueCodes.PlanFieldConversionNotSupported,
                    $"{path}/type",
                    $"不支援{change}。要換成這個型別，請新增一個欄位，再把原本的欄位拿掉。")),
        };
    }

    private static IEnumerable<ValidationIssue> CurrencyChange(FieldDefinition before, FieldDefinition after, string path)
    {
        if (before.Type == FieldType.Currency && after.Type == FieldType.Currency && before.Currency != after.Currency)
        {
            yield return new ValidationIssue(
                IssueCodes.PlanFieldConversionNotSupported,
                $"{path}/currency",
                $"金額欄位{NameOf(before)}不能把幣別從{Quote(before.Currency)}換成{Quote(after.Currency)}：既有的金額不會跟著換算。"
                + "要換幣別，請新增一個欄位。");
        }
    }

    private static IEnumerable<ValidationIssue> RemovedOptions(FieldDefinition before, FieldDefinition after, string path)
    {
        if (!before.IsSelect || !after.IsSelect || before.Options is null || after.Options is null)
        {
            yield break;
        }

        var remaining = after.Options.Select(option => option.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var removed in before.Options.DistinctBy(option => option.Key, StringComparer.Ordinal).Where(option => !remaining.Contains(option.Key)))
        {
            yield return new ValidationIssue(
                IssueCodes.PlanOptionRemovalNotSupported,
                $"{path}/options",
                $"欄位{NameOf(before)}的選項{QuoteName(removed.Name, removed.Key)}被拿掉了，但可能還有項目選了它，"
                + "而變更集目前沒有地方寫明這些項目要改成哪個選項。請先保留這個選項。");
        }
    }

    /// <summary>欄位用目前的名稱稱呼：兩版都有的欄位，使用者現在在畫面上看到的是目前的名稱。</summary>
    private static string NameOf(FieldDefinition field) => QuoteName(field.Name, field.Key);
}
