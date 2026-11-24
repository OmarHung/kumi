using PmTool.Blueprint.Model;

namespace PmTool.Work.Templates;

/// <summary>把內建的模板目錄用合約的介面交出去，給 AI 助理讀。模板是公開的內容，不看權限。</summary>
internal sealed class TemplateLibrary(TemplateCatalog catalog) : ITemplateLibrary
{
    public IReadOnlyList<TemplateSummary> List() => catalog.Summaries;

    public ProjectBlueprint? FindBlueprint(string templateKey) => catalog.Find(templateKey)?.Blueprint;
}
