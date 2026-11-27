using PmTool.Abstractions;
using PmTool.Ai.PlanningProposals;
using PmTool.Work;

namespace PmTool.Ai.ProjectChangeProposals;

/// <summary>改專案建議的檢查：規則和 <c>RenameAsync</c>、<c>SetAsync</c> 一樣，每個問題一個細項（路徑是欄位名稱），一次回齊。</summary>
internal static class ProjectChangeChecker
{
    /// <summary>整理過、真的有變的內容：沒變的欄位是 null／沒有值。</summary>
    internal sealed record Checked(string? Name, bool? Archived, bool ChangesDescription, string? Description);

    public static Result<Checked> Check(ProjectChangeProposalDraft draft, ProjectSnapshot current)
    {
        var issues = new List<FailureDetail>();
        var name = CheckName(draft.Name, issues);
        var description = CheckDescription(draft.Description, current, issues);
        if (!HasAnyGiven(draft))
        {
            issues.Add(Issue(string.Empty, "名稱、封存、說明至少要給一個。"));
        }

        var same = new List<string>();
        var nameChanged = draft.Name is not null && name is not null && name != current.Name;
        if (draft.Name is not null && name is not null && !nameChanged)
        {
            same.Add("name");
        }

        var archivedChanged = draft.Archived is { } archived && archived != current.Archived;
        if (draft.Archived is not null && !archivedChanged)
        {
            same.Add("archived");
        }

        var descriptionChanged = draft.Description.HasValue && description.Valid && description.Text != Normalize(current.Description);
        if (draft.Description.HasValue && description.Valid && !descriptionChanged)
        {
            same.Add("description");
        }

        AddSameIssues(same, nameChanged || archivedChanged || descriptionChanged, issues);
        if (issues.Count > 0)
        {
            return new Failure(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "改專案的建議不合規定，沒有建立。") { Details = issues };
        }

        return new Checked(nameChanged ? name : null, archivedChanged ? draft.Archived : null, descriptionChanged, descriptionChanged ? description.Text : null);
    }

    // 全部都和現在一樣：一個沒有路徑的問題；只有部分一樣：每個指到自己的欄位，模型才知道哪個多餘。
    private static void AddSameIssues(List<string> same, bool anyChanged, List<FailureDetail> issues)
    {
        if (same.Count == 0)
        {
            return;
        }

        if (!anyChanged)
        {
            issues.Add(Issue(string.Empty, "給的內容和專案現在的一樣，不需要修改。"));
            return;
        }

        issues.AddRange(same.Select(path => Issue(path, $"{path} 和專案現在的一樣，不要放進來。")));
    }

    private static bool HasAnyGiven(ProjectChangeProposalDraft draft) => draft.Name is not null || draft.Archived is not null || draft.Description.HasValue;

    private static string? CheckName(string? raw, List<FailureDetail> issues)
    {
        if (raw is null)
        {
            return null;
        }

        var name = raw.Trim();
        if (name.Length == 0 || name.Length > WorkRules.MaxProjectNameLength)
        {
            issues.Add(Issue("name", $"名稱要有 1 到 {WorkRules.MaxProjectNameLength} 個字（去掉前後空白後）。"));
            return null;
        }

        if (!PlanningText.HasUnsafeNameCharacters(name))
        {
            return name;
        }

        issues.Add(Issue("name", "名稱不能有換行、控制字元、看不見的字元或無效的字元。"));
        return null;
    }

    // 說明照 SetAsync：空白是清掉，其他照原樣（換行與縮排都保留）。
    private static (bool Valid, string? Text) CheckDescription(Optional<string?> given, ProjectSnapshot current, List<FailureDetail> issues)
    {
        if (!given.HasValue)
        {
            return (false, null);
        }

        if (current.Description is { Length: > AiRules.MaxEditableProjectDescriptionLength })
        {
            issues.Add(Issue("description", $"專案現在的說明超過 {AiRules.MaxEditableProjectDescriptionLength} 個字，助理讀不到全文，改了會蓋掉後面的內容；請使用者自己到資訊頁改。"));
            return (false, null);
        }

        var text = Normalize(given.Value);
        if (text is null)
        {
            return (true, null);
        }

        var problem = text.Length > ProjectDescriptionRules.MaxTextLength
            ? $"說明最多 {ProjectDescriptionRules.MaxTextLength} 個字，現在有 {text.Length} 個字。"
            : PlanningText.HasForbiddenCharacters(text, allowLineBreaks: true)
                ? "說明不能有控制字元或無效的字元；換行與 Tab 可以。"
                : SecretText.ContainsSecret(text)
                    ? "說明裡有私鑰或看得出格式的金鑰、權杖，不能存進專案說明；請拿掉。"
                    : null;
        if (problem is null)
        {
            return (true, text);
        }

        issues.Add(Issue("description", problem));
        return (false, null);
    }

    // 空白（含 null）就是沒有說明；其他原樣。
    private static string? Normalize(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private static FailureDetail Issue(string path, string message) => new(AiErrorCodes.FieldInvalid, path, message);
}
