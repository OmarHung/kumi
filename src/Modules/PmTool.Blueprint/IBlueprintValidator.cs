using PmTool.Blueprint.Model;

namespace PmTool.Blueprint;

public sealed record BlueprintValidationResult(ValidationReport Report, ProjectBlueprint? Blueprint);

public sealed record ChangeSetValidationResult(ValidationReport Report, ChangeSetRequest? ChangeSet);

/// <summary>藍圖模組對外的入口。只有文件完全通過時，結果才會帶型別化的模型。</summary>
public interface IBlueprintValidator
{
    BlueprintValidationResult ValidateBlueprint(string? documentText, DocumentFormat format, ValidationMode mode = ValidationMode.Authoring);

    ChangeSetValidationResult ValidateChangeSet(string? documentText, DocumentFormat format, ValidationMode mode = ValidationMode.Authoring);
}
