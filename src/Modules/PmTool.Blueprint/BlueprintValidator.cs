using PmTool.Blueprint.Documents;
using PmTool.Blueprint.Model;
using PmTool.Blueprint.Schema;
using PmTool.Blueprint.Validation;

namespace PmTool.Blueprint;

/// <summary>驗證的三個階段依序進行：讀檔、結構、語意。前一階段有問題就停，不往下做。</summary>
public sealed class BlueprintValidator : IBlueprintValidator
{
    private static readonly string[] RequiredSchemaIds = [SchemaIds.ProjectBlueprint, SchemaIds.ChangeSet];

    private readonly SchemaRegistry _schemas;

    private BlueprintValidator(SchemaRegistry schemas) => _schemas = schemas;

    public static BlueprintValidator CreateDefault() => Create(SchemaRegistry.Load(EmbeddedSchemas.ReadAll()));

    /// <summary>
    /// Schema 是用萬用字元內嵌進組件的，路徑對不到時建置不會失敗，只會少內嵌。
    /// 所以建立時就確認用得到的兩份都在，不要等到第一份文件進來才發現。
    /// </summary>
    internal static BlueprintValidator Create(SchemaRegistry schemas)
    {
        var missing = RequiredSchemaIds.Where(schemaId => !schemas.Contains(schemaId)).ToArray();
        return missing.Length == 0
            ? new BlueprintValidator(schemas)
            : throw new InvalidOperationException(
                $"少了必要的 Schema：{string.Join("、", missing)}。請確認建置時 schemas/blueprint/v0 的檔案有內嵌進組件。");
    }

    public BlueprintValidationResult ValidateBlueprint(string? documentText, DocumentFormat format, ValidationMode mode = ValidationMode.Authoring)
    {
        var (report, blueprint) = Validate<ProjectBlueprint>(
            documentText, format, SchemaIds.ProjectBlueprint, value => SemanticValidator.Validate(value, mode));
        return new BlueprintValidationResult(report, blueprint);
    }

    public ChangeSetValidationResult ValidateChangeSet(string? documentText, DocumentFormat format, ValidationMode mode = ValidationMode.Authoring)
    {
        var (report, changeSet) = Validate<ChangeSetRequest>(
            documentText, format, SchemaIds.ChangeSet, value => SemanticValidator.ValidateChangeSet(value, mode));
        return new ChangeSetValidationResult(report, changeSet);
    }

    private (ValidationReport Report, T? Value) Validate<T>(
        string? documentText,
        DocumentFormat format,
        string schemaId,
        Func<T, IReadOnlyList<ValidationIssue>> validateSemantics)
        where T : class
    {
        var read = DocumentReader.Read(documentText, format);
        if (!read.Succeeded)
        {
            return (ValidationReport.From(read.Issues), null);
        }

        var structuralIssues = StructuralValidator.Validate(read.Root, schemaId, _schemas);
        if (structuralIssues.Count > 0)
        {
            return (ValidationReport.From(structuralIssues), null);
        }

        var value = ModelBinder.Bind<T>(read.Root!);
        var semanticIssues = validateSemantics(value);
        return (ValidationReport.From(semanticIssues), semanticIssues.Count == 0 ? value : null);
    }
}
