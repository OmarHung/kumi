using PmTool.Blueprint.Model;
using static PmTool.Blueprint.IssueText;

namespace PmTool.Blueprint.Validation.Rules;

/// <summary>規則 4：自訂角色不能和內建角色同名。依角色順序。</summary>
internal static class RoleRules
{
    public static IEnumerable<ValidationIssue> Check(ProjectBlueprint blueprint)
    {
        for (var i = 0; i < blueprint.Roles.Count; i++)
        {
            var key = blueprint.Roles[i].Key;
            if (BlueprintVocabulary.BuiltInRoles.Contains(key))
            {
                yield return new ValidationIssue(
                    IssueCodes.RoleBuiltInConflict,
                    $"/roles/{i}/key",
                    $"自訂角色{Quote(key)}和內建角色同名。內建角色不必也不能在 roles 裡宣告。");
            }
        }
    }
}
