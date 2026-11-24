using PmTool.Blueprint.Model;
using static PmTool.Blueprint.IssueText;

namespace PmTool.Blueprint.Validation.Rules;

/// <summary>規則 12 與 13：自動化。兩條規則各自依自動化的順序走一遍。</summary>
internal static class AutomationRules
{
    /// <summary>規則 12。每個自動化依 Schema 的屬性順序：trigger.source 的綁定、trigger.event、actions 是否為空。</summary>
    public static IEnumerable<ValidationIssue> CheckTriggers(ProjectBlueprint blueprint, BlueprintIndex index)
    {
        for (var a = 0; a < blueprint.Automations.Count; a++)
        {
            var automation = blueprint.Automations[a];
            var path = $"/automations/{a}";
            if (MissingBinding(automation, path, index) is { } bindingIssue)
            {
                yield return bindingIssue;
            }

            if (UnknownEvent(automation, path) is { } eventIssue)
            {
                yield return eventIssue;
            }

            if (automation.Actions.Count == 0)
            {
                yield return new ValidationIssue(
                    IssueCodes.AutomationNoActions, $"{path}/actions", $"自動化{Quote(automation.Key)}至少要有一個動作。");
            }
        }
    }

    /// <summary>規則 13。每個自動化先檢查所有條件，再檢查所有動作，各依列出的順序。</summary>
    public static IEnumerable<ValidationIssue> CheckConditionsAndActions(ProjectBlueprint blueprint, BlueprintIndex index)
    {
        for (var a = 0; a < blueprint.Automations.Count; a++)
        {
            var automation = blueprint.Automations[a];
            var path = $"/automations/{a}";
            var conditions = automation.Conditions
                .SelectMany((condition, c) => Condition(condition, $"{path}/conditions/{c}", index));
            var actions = automation.Actions
                .SelectMany((action, x) => AutomationActionRules.Check(action, $"{path}/actions/{x}", index));
            foreach (var issue in conditions.Concat(actions))
            {
                yield return issue;
            }
        }
    }

    private static ValidationIssue? MissingBinding(Automation automation, string path, BlueprintIndex index)
    {
        ConnectorType? required = automation.Trigger.Source switch
        {
            TriggerSource.Gitlab => ConnectorType.Gitlab,
            TriggerSource.Github => ConnectorType.Github,
            _ => null,
        };
        if (required is not { } connector || index.Connectors.Contains(connector))
        {
            return null;
        }

        var name = DocumentNames.Of(connector);
        return new ValidationIssue(
            IssueCodes.AutomationMissingBinding,
            $"{path}/trigger/source",
            $"自動化{Quote(automation.Key)}由 {name} 觸發，bindings 裡卻沒有 {name} 的整合綁定。");
    }

    // Schedule 與 Webhook 不在 KnownEvents 裡，事件名稱不限制。
    private static ValidationIssue? UnknownEvent(Automation automation, string path)
    {
        var trigger = automation.Trigger;
        if (!BlueprintVocabulary.KnownEvents.TryGetValue(trigger.Source, out var events) || events.Contains(trigger.Event))
        {
            return null;
        }

        var known = string.Join("、", events.Order(StringComparer.Ordinal));
        return new ValidationIssue(
            IssueCodes.AutomationUnknownEvent,
            $"{path}/trigger/event",
            $"自動化{Quote(automation.Key)}的事件{Quote(trigger.Event)}不是 {DocumentNames.Of(trigger.Source)} 認得的事件。認得的有：{known}。");
    }

    // 條件可以看 state、item_type 或選項型欄位。選項型欄位的值目前不檢查：沒有對應的問題代碼。
    private static IEnumerable<ValidationIssue> Condition(AutomationCondition condition, string path, BlueprintIndex index) =>
        condition.Field switch
        {
            BlueprintVocabulary.State => UnknownValues(
                condition.In, index.AllStates.Contains, path, IssueCodes.AutomationUnknownState, "不在任何流程裡的狀態"),
            BlueprintVocabulary.ItemType => UnknownValues(
                condition.In, index.IsItemType, path, IssueCodes.AutomationUnknownItemType, "沒有定義的類型"),
            var field when index.Fields.TryGetValue(field, out var definition) && definition.IsSelect => [],
            var field =>
            [
                new ValidationIssue(
                    IssueCodes.AutomationInvalidConditionField,
                    $"{path}/field",
                    $"條件不能看{Quote(field)}：只能用 state、item_type 或一個單選、多選欄位的 key。"),
            ],
        };

    private static IEnumerable<ValidationIssue> UnknownValues(
        IReadOnlyList<string> values, Func<string, bool> exists, string path, string code, string what)
    {
        for (var v = 0; v < values.Count; v++)
        {
            if (!exists(values[v]))
            {
                yield return new ValidationIssue(code, $"{path}/in/{v}", $"條件裡的{Quote(values[v])}是{what}。");
            }
        }
    }
}
