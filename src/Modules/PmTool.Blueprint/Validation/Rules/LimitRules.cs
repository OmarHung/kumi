using PmTool.Blueprint.Model;
using static PmTool.Blueprint.IssueText;

namespace PmTool.Blueprint.Validation.Rules;

/// <summary>數量上限。順序：欄位、各流程的狀態（依流程順序）、類型、自動化。</summary>
internal static class LimitRules
{
    public static IEnumerable<ValidationIssue> Check(ProjectBlueprint blueprint)
    {
        if (blueprint.Fields.Count > BlueprintVocabulary.MaxFields)
        {
            yield return Exceeded("/fields", "欄位", blueprint.Fields.Count, BlueprintVocabulary.MaxFields);
        }

        for (var i = 0; i < blueprint.Workflows.Count; i++)
        {
            var workflow = blueprint.Workflows[i];
            if (workflow.States.Count > BlueprintVocabulary.MaxStatesPerWorkflow)
            {
                yield return Exceeded(
                    $"/workflows/{i}/states",
                    $"流程{Quote(workflow.Key)}的狀態",
                    workflow.States.Count,
                    BlueprintVocabulary.MaxStatesPerWorkflow);
            }
        }

        if (blueprint.ItemTypes.Count > BlueprintVocabulary.MaxItemTypes)
        {
            yield return Exceeded("/itemTypes", "類型", blueprint.ItemTypes.Count, BlueprintVocabulary.MaxItemTypes);
        }

        if (blueprint.Automations.Count > BlueprintVocabulary.MaxAutomations)
        {
            yield return Exceeded("/automations", "自動化", blueprint.Automations.Count, BlueprintVocabulary.MaxAutomations);
        }
    }

    private static ValidationIssue Exceeded(string path, string subject, int count, int limit) =>
        new(IssueCodes.LimitExceeded, path, $"{subject}有 {count} 個，超過上限 {limit} 個。");
}
