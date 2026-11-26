using System.Text.Json;
using PmTool.Blueprint.Model;

namespace PmTool.Ai.Staffing;

/// <summary>藍圖的項目類型整理成 <see cref="ProjectBriefItemType"/>。沒有 blueprint.read 的人拿到的精簡版藍圖也有這些資料。</summary>
internal static class BriefItemTypes
{
    public static IReadOnlyList<ProjectBriefItemType> From(ProjectBlueprint blueprint)
    {
        var fields = blueprint.Fields.ToDictionary(field => field.Key, StringComparer.Ordinal);
        return
        [
            .. blueprint.ItemTypes.Select(type =>
            {
                var workflow = blueprint.Workflows.FirstOrDefault(w => w.Key == type.Workflow);
                return new ProjectBriefItemType(
                    type.Key,
                    type.Name,
                    type.Parents,
                    workflow?.States.FirstOrDefault()?.Name ?? string.Empty,
                    [.. type.Fields.Where(fields.ContainsKey).Select(key => Field(fields[key], workflow))]);
            }),
        ];
    }

    // 藍圖的欄位沒有「必填」：必填是流程的轉換才要求（requireFields），這裡照實標出「有轉換要求它」，讓模型知道早點問。
    private static ProjectBriefField Field(FieldDefinition field, Workflow? workflow) => new(
        field.Key,
        field.Name,
        JsonNamingPolicy.SnakeCaseLower.ConvertName(field.Type.ToString()),
        workflow?.Transitions?.Any(t => t.RequireFields.Contains(field.Key)) == true,
        [.. field.Options?.Select(option => option.Key) ?? []]);
}
