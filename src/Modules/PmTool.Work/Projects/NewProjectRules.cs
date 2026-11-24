using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Common;
using PmTool.Work.Templates;
using static PmTool.Work.Common.TextRules;

namespace PmTool.Work.Projects;

/// <summary>開專案前檢查代號、名稱、模板。名稱去掉頭尾空白後才檢查與儲存。</summary>
internal static class NewProjectRules
{
    public const string KeyPath = "key";
    public const string NamePath = "name";
    public const string TemplatePath = "template";
    public const string DescriptionPath = "description";

    public static Result<CheckedProject> Check(NewProject project, TemplateCatalog templates)
    {
        var name = project.Name?.Trim() ?? string.Empty;
        var template = project.Template is null ? null : templates.Find(project.Template);
        var description = ProjectDescriptionChecks.Check(project.Description, DescriptionPath);
        var details = new[] { KeyProblem(project.Key), NameProblem(name), TemplateProblem(project.Template, template) }
            .OfType<FailureDetail>()
            .Concat(description.Failure?.Details ?? [])
            .ToArray();
        return details.Length > 0
            ? WorkFailures.Invalid(details)
            : new CheckedProject(project.Key, name, template!, description.Value);
    }

    /// <summary>改名用：和建立時同一套名稱規則，回去掉頭尾空白的名稱。</summary>
    public static Result<string> CheckName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        return NameProblem(trimmed) is { } problem ? WorkFailures.Invalid([problem]) : trimmed;
    }

    private static FailureDetail? KeyProblem(string? key) => ProjectKeys.IsValid(key)
        ? null
        : new FailureDetail(
            WorkErrorCodes.ProjectKeyInvalid,
            KeyPath,
            $"專案代號{Quote(key)}不合格式：要以大寫英文字母開頭，只能有大寫字母與數字，長度 2 到 10 個字元，例如 WEB。");

    private static FailureDetail? NameProblem(string name)
    {
        if (name.Length == 0)
        {
            return new FailureDetail(WorkErrorCodes.ProjectNameRequired, NamePath, "請填專案名稱。");
        }

        if (name.Length > WorkRules.MaxProjectNameLength)
        {
            return new FailureDetail(
                WorkErrorCodes.ProjectNameTooLong,
                NamePath,
                $"專案名稱最多 {WorkRules.MaxProjectNameLength} 個字，現在有 {name.Length} 個字。");
        }

        // 合約沒有「名稱含有不能用的字元」的代碼；這種名稱等於沒有可用的名稱，所以歸在 name_required。
        return HasForbiddenCharacters(name, allowLineBreaks: false)
            ? new FailureDetail(WorkErrorCodes.ProjectNameRequired, NamePath, "專案名稱不能有換行、控制字元或無效的字元。")
            : null;
    }

    private static FailureDetail? TemplateProblem(string? key, WorkTemplate? template) => template is not null
        ? null
        : new FailureDetail(WorkErrorCodes.TemplateNotFound, TemplatePath, $"沒有{Quote(key)}這個模板。");
}

/// <param name="Description">檢查過的專案說明；沒有要寫是 null。</param>
internal sealed record CheckedProject(string Key, string Name, WorkTemplate Template, string? Description);
