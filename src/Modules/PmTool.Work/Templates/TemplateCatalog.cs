using System.Collections.Frozen;
using System.Reflection;
using PmTool.Blueprint;
using PmTool.Blueprint.Model;
using PmTool.Work.Blueprints;

namespace PmTool.Work.Templates;

/// <param name="Version">模板自己的版本，寫在模板的 metadata.basedOn.version；開專案時記進專案的 basedOn。</param>
/// <param name="Examples">範例項目，已經驗證過；模板沒有範例檔時是空的。</param>
internal sealed record WorkTemplate(string Key, ProjectBlueprint Blueprint, string Version, IReadOnlyList<ExampleItem> Examples)
{
    /// <summary>範例的週期與里程碑；項目的 cycle、milestone 用 ref 指到它們。</summary>
    public IReadOnlyList<ExampleCycle> Cycles { get; init; } = [];

    public IReadOnlyList<ExampleMilestone> Milestones { get; init; } = [];

    public ExampleSet ExampleSet => new(Cycles, Milestones, Examples);

    public TemplateSummary ToSummary() => new(
        Key, Blueprint.Metadata.Name, Blueprint.Metadata.Description, Previews(), ParentPreviews(), [.. Blueprint.Fields.Select(f => f.Name)], Examples.Count);

    // 沒有類型在用的流程不會出現在專案裡，預覽也就不列。
    private List<TemplateWorkflowPreview> Previews() =>
        [.. Blueprint.Workflows
            .Select(w => new TemplateWorkflowPreview(
                w.Name,
                [.. Blueprint.ItemTypes.Where(t => t.Workflow == w.Key).Select(t => t.Name)],
                [.. w.States.Where(s => s.Category != StateCategory.Canceled).Select(s => s.Name)],
                [.. w.States.Where(s => s.Category == StateCategory.Canceled).Select(s => s.Name)])
            {
                StateCategories = [.. w.States.Where(s => s.Category != StateCategory.Canceled).Select(s => s.Category)],
            })
            .Where(p => p.ItemTypeNames.Count > 0)];

    private List<TemplateParentPreview> ParentPreviews()
    {
        var names = Blueprint.ItemTypes.ToDictionary(t => t.Key, t => t.Name, StringComparer.Ordinal);
        return [.. Blueprint.ItemTypes
            .Where(t => t.Parents.Count > 0)
            .Select(t => new TemplateParentPreview(t.Name, [.. t.Parents.Select(key => names[key])]))];
    }
}

/// <summary>內嵌在組件裡的模板。建立時就全部讀進來並驗證，壞掉的模板讓第一次使用就失敗，不會開出壞的專案。</summary>
internal sealed class TemplateCatalog
{
    private const string ResourcePrefix = "PmTool.Work.Templates.";
    private const string ResourceSuffix = ".blueprint.yaml";
    private const string ExamplesSuffix = ".examples.yaml";

    private readonly FrozenDictionary<string, WorkTemplate> _templates;

    public TemplateCatalog(IBlueprintValidator validator)
        : this(LoadAll(validator))
    {
    }

    /// <summary>用現成的模板建目錄。測試用來放進沒有內嵌的模板。</summary>
    internal TemplateCatalog(IEnumerable<WorkTemplate> loaded)
    {
        var templates = loaded.ToArray();
        _templates = templates.ToFrozenDictionary(t => t.Key, StringComparer.Ordinal);
        Summaries = [.. templates.Select(t => t.ToSummary())];
    }

    /// <summary>依 key 排序，清單的順序才固定。</summary>
    public IReadOnlyList<TemplateSummary> Summaries { get; }

    public WorkTemplate? Find(string key) => _templates.GetValueOrDefault(key);

    private static WorkTemplate[] LoadAll(IBlueprintValidator validator)
    {
        var assembly = typeof(TemplateCatalog).Assembly;
        var templates = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                && name.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(name => Load(assembly, name, validator))
            .ToArray();

        // 內嵌是用萬用字元設定的，路徑對不到時建置不會失敗，只會少了模板。
        return templates.Length > 0
            ? templates
            : throw new InvalidOperationException("組件裡沒有任何內嵌的模板，請確認 Templates/*.blueprint.yaml 有內嵌進去。");
    }

    private static WorkTemplate Load(Assembly assembly, string resourceName, IBlueprintValidator validator)
    {
        var key = resourceName[ResourcePrefix.Length..^ResourceSuffix.Length];
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"讀不到內嵌的模板 {resourceName}。");
        using var reader = new StreamReader(stream);
        var result = validator.ValidateBlueprint(reader.ReadToEnd(), DocumentFormat.Yaml);
        var blueprint = result.Blueprint ?? throw new InvalidOperationException(
            $"模板 {key} 沒有通過驗證：{string.Join("；", result.Report.Issues.Take(5).Select(i => $"{i.Path} {i.Message}"))}");
        if (blueprint.Metadata.Key != key)
        {
            throw new InvalidOperationException($"模板檔 {key} 的 metadata.key 是 {blueprint.Metadata.Key}，兩者要一樣。");
        }

        var version = blueprint.Metadata.BasedOn?.Version
            ?? throw new InvalidOperationException($"模板 {key} 要在 metadata.basedOn.version 寫明模板的版本。");
        var examples = LoadExamples(assembly, key, blueprint);
        return new WorkTemplate(key, blueprint, version, examples.Items) { Cycles = examples.Cycles, Milestones = examples.Milestones };
    }

    /// <summary>範例檔可以沒有；有就整份驗證，壞了和藍圖一樣讓第一次使用就失敗。</summary>
    private static ExampleSet LoadExamples(Assembly assembly, string key, ProjectBlueprint blueprint)
    {
        using var stream = assembly.GetManifestResourceStream($"{ResourcePrefix}{key}{ExamplesSuffix}");
        if (stream is null)
        {
            return ExampleSet.Empty;
        }

        using var reader = new StreamReader(stream);
        // 相對日期只在建立專案時才換成真的日期；這裡用今天驗證形狀與範圍，結果和哪一天無關。
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = ExampleReader.Read(key, reader.ReadToEnd(), ProjectSchema.From(blueprint), today);
        return result.Problems.Count == 0
            ? new ExampleSet(result.Cycles, result.Milestones, result.Items)
            : throw new InvalidOperationException(string.Join("；", result.Problems.Take(5)));
    }
}
