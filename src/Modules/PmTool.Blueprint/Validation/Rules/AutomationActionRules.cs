using PmTool.Blueprint.Model;
using static PmTool.Blueprint.IssueText;

namespace PmTool.Blueprint.Validation.Rules;

/// <summary>
/// 規則 13 的動作部分。先依 Schema 的屬性順序（to、field、value、message、url）檢查參數：
/// 自己的參數沒帶，回報在動作本身；別人的參數帶了，回報在那個參數。最後才檢查自己參數的值。
/// </summary>
internal static class AutomationActionRules
{
    // 第 0 版每個參數只屬於一種動作。
    private static readonly ActionParameter[] Parameters =
    [
        new("to", ActionType.Transition, a => a.To),
        new("field", ActionType.SetField, a => a.Field),
        new("value", ActionType.SetField, a => a.Value),
        new("message", ActionType.Notify, a => a.Message),
        new("url", ActionType.Webhook, a => a.Url),
    ];

    public static IEnumerable<ValidationIssue> Check(AutomationAction action, string path, BlueprintIndex index)
    {
        foreach (var issue in ParameterIssues(action, path))
        {
            yield return issue;
        }

        if (ValueIssue(action, path, index) is { } valueIssue)
        {
            yield return valueIssue;
        }
    }

    private static IEnumerable<ValidationIssue> ParameterIssues(AutomationAction action, string path)
    {
        var type = DocumentNames.Of(action.Type);
        foreach (var parameter in Parameters)
        {
            var isOwn = parameter.Owner == action.Type;
            var isPresent = parameter.Read(action) is not null;
            if (isOwn && !isPresent)
            {
                yield return new ValidationIssue(
                    IssueCodes.AutomationActionParameters, path, $"{type} 動作必須帶 {parameter.Name}。");
            }
            else if (!isOwn && isPresent)
            {
                yield return new ValidationIssue(
                    IssueCodes.AutomationActionParameters,
                    $"{path}/{parameter.Name}",
                    $"{type} 動作不能帶 {parameter.Name}，那是 {DocumentNames.Of(parameter.Owner)} 動作的參數。");
            }
        }
    }

    // 只檢查屬於這種動作的參數；帶錯的參數已經回報過，它的值對不對沒有意義。
    // SetField 只能改自訂欄位：狀態要走流程的轉換才會套用角色與必填的限制，其他核心欄位也各有自己的規則。
    private static ValidationIssue? ValueIssue(AutomationAction action, string path, BlueprintIndex index) => action switch
    {
        { Type: ActionType.Transition, To: { } to } when !index.AllStates.Contains(to) => new ValidationIssue(
            IssueCodes.AutomationUnknownState, $"{path}/to", $"動作要轉到的狀態{Quote(to)}不在任何流程裡。"),
        { Type: ActionType.SetField, Field: { } field } when !index.Fields.ContainsKey(field) => new ValidationIssue(
            IssueCodes.AutomationUnknownField,
            $"{path}/field",
            $"動作要設定的欄位{Quote(field)}沒有在 fields 裡定義；set_field 只能設定自訂欄位。"),
        { Type: ActionType.Webhook, Url: { } url } when !StrictText.IsAbsoluteHttpUrl(url) => new ValidationIssue(
            IssueCodes.AutomationInvalidUrl, $"{path}/url", $"網址{Quote(url)}不是絕對的 http 或 https 網址。"),
        _ => null,
    };

    private sealed record ActionParameter(string Name, ActionType Owner, Func<AutomationAction, string?> Read);
}
