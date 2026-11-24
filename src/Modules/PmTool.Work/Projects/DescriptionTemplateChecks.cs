using PmTool.Abstractions;
using PmTool.Work.Common;
using PmTool.Work.Persistence;
using static PmTool.Work.Common.TextRules;

namespace PmTool.Work.Projects;

/// <summary>說明格式的名稱與內容檢查，以及它專屬的失敗。</summary>
internal static class DescriptionTemplateChecks
{
    public const string NamePath = "name";
    public const string BodyPath = "body";

    /// <summary>名稱去掉頭尾空白、內容換行統一；名稱與內容的問題一次回齊。</summary>
    public static Result<CheckedTemplate> Check(DescriptionTemplateInput input)
    {
        var name = input.Name?.Trim() ?? string.Empty;
        var body = ProjectDescriptionChecks.Check(input.Body, BodyPath, "說明格式的內容");
        var details = new List<FailureDetail>();
        if (NameProblem(name) is { } nameProblem)
        {
            details.Add(nameProblem);
        }

        if (body.Failure is { } bodyFailure)
        {
            details.AddRange(bodyFailure.Details);
        }
        else if (body.Value is null)
        {
            details.Add(new FailureDetail(WorkErrorCodes.DescriptionTemplateBodyRequired, BodyPath, "請填說明格式的內容。"));
        }

        return details.Count > 0
            ? WorkFailures.Invalid(details)
            : new CheckedTemplate(name, body.Value!, input.IsDefault);
    }

    public static Failure NotFound() =>
        new(FailureKind.NotFound, WorkErrorCodes.DescriptionTemplateNotFound, "找不到這份說明格式。");

    public static Failure NameTaken(string name) => new(
        FailureKind.Conflict,
        WorkErrorCodes.DescriptionTemplateNameTaken,
        $"已經有一份叫{Quote(name)}的說明格式了（不分大小寫，內建的也算），請換一個名稱。");

    public static Failure LimitReached() => new(
        FailureKind.Conflict,
        WorkErrorCodes.DescriptionTemplateLimitReached,
        $"組織最多 {ProjectDescriptionRules.MaxTemplatesPerOrganization} 份說明格式，請先刪掉用不到的。");

    /// <summary>
    /// 寫入撞到唯一索引時轉成合約的失敗，不是索引的錯誤回 null（照樣丟出去）。同名索引是 name_taken；
    /// 預設的索引是排隊之後正常路徑碰不到的防線，真的撞到代表有寫入繞過了排隊，回「忙碌、請重試」。
    /// </summary>
    public static Failure? ConflictFor(Exception exception, string name)
    {
        if (PostgresErrors.IsUniqueViolation(exception, WorkDbContext.DescriptionTemplateNameIndex))
        {
            return NameTaken(name);
        }

        return PostgresErrors.IsUniqueViolation(exception, WorkDbContext.DescriptionTemplateDefaultIndex)
            ? new Failure(FailureKind.Conflict, WorkErrorCodes.ChangeSetBusy, "說明格式正有人在修改；這次什麼都沒有改，請稍後再試。")
            : null;
    }

    private static FailureDetail? NameProblem(string name)
    {
        if (name.Length > ProjectDescriptionRules.MaxTemplateNameLength)
        {
            return new FailureDetail(
                WorkErrorCodes.DescriptionTemplateNameTooLong,
                NamePath,
                $"說明格式的名稱最多 {ProjectDescriptionRules.MaxTemplateNameLength} 個字，現在有 {name.Length} 個字。");
        }

        // 合約沒有「名稱含有不能用的字元」的代碼；這種名稱等於沒有可用的名稱，所以歸在 name_required（和專案名稱一樣）。
        return name.Length == 0 || HasUnsafeNameCharacters(name)
            ? new FailureDetail(WorkErrorCodes.DescriptionTemplateNameRequired, NamePath, "請填說明格式的名稱，不能有換行、控制字元或看不見的字元。")
            : null;
    }
}

internal sealed record CheckedTemplate(string Name, string Body, bool IsDefault);
