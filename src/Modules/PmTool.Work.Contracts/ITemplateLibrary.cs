using PmTool.Blueprint.Model;

namespace PmTool.Work;

/// <summary>
/// 內建的模板。<see cref="IProjectService.ListTemplates"/> 只有給人挑的摘要；這裡多一個整份藍圖，
/// 給 AI 助理參考「這個模板怎麼設」用。模板是公開的內容，不需要權限。
/// </summary>
public interface ITemplateLibrary
{
    /// <summary>和 <see cref="IProjectService.ListTemplates"/> 同一份清單。</summary>
    IReadOnlyList<TemplateSummary> List();

    /// <summary>模板的整份藍圖；沒有這個 key 回 null。</summary>
    ProjectBlueprint? FindBlueprint(string templateKey);
}
