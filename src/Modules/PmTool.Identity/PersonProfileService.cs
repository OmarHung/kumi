using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Identity.Persistence;

namespace PmTool.Identity;

/// <summary>人員的職稱與負責範圍。不是安全性的設定，所以不寫稽核紀錄，也不分擁有者與管理員。</summary>
internal sealed class PersonProfileService(IDbContextFactory<IdentityDbContext> contexts) : IPersonProfileService
{
    private const string JobTitlePath = "jobTitle";
    private const string ResponsibilitiesPath = "responsibilities";

    public async Task<Result<PersonSummary>> UpdateProfileAsync(
        Actor actor, Guid personId, PersonProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(profile);

        const string notAdmin = "只有組織的擁有者或管理員可以設定職稱與負責範圍。";
        if (!actor.IsOrganizationAdmin)
        {
            return new Failure(FailureKind.Forbidden, IdentityErrorCodes.Forbidden, notAdmin);
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // Actor 分不出擁有者與管理員，也不知道他現在還在不在、有沒有被停用，所以查資料庫（和停用、改角色同一套）。
        var actorIsAdmin = await (
            from p in db.People
            join a in db.Accounts on p.Id equals a.PersonId
            where p.Id == actor.PersonId && p.OrganizationId == actor.OrganizationId && a.DisabledAt == null
                && (p.Role == OrganizationRole.Owner || p.Role == OrganizationRole.Admin)
            select p.Id).AnyAsync(cancellationToken);
        if (!actorIsAdmin)
        {
            return new Failure(FailureKind.Forbidden, IdentityErrorCodes.Forbidden, notAdmin);
        }

        var checkedProfile = Check(profile);
        if (checkedProfile.Failure is { } invalid)
        {
            return invalid;
        }

        var (jobTitle, responsibilities) = checkedProfile.Value;
        var updated = await db.People
            .Where(p => p.Id == personId && p.OrganizationId == actor.OrganizationId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(p => p.JobTitle, jobTitle)
                    .SetProperty(p => p.Responsibilities, responsibilities),
                cancellationToken);
        if (updated == 0)
        {
            return new Failure(FailureKind.NotFound, IdentityErrorCodes.NotFound, "組織裡找不到這個人。");
        }

        return await PeopleDirectory
            .Summaries(db, db.People.Where(p => p.Id == personId && p.OrganizationId == actor.OrganizationId))
            .SingleAsync(cancellationToken);
    }

    private static Result<(string? JobTitle, string? Responsibilities)> Check(PersonProfile profile)
    {
        var jobTitle = Normalize(profile.JobTitle);
        var responsibilities = Normalize(profile.Responsibilities);
        FailureDetail?[] found =
        [
            CheckField(jobTitle, JobTitlePath, "職稱", IdentityRules.MaxJobTitleLength),
            CheckField(responsibilities, ResponsibilitiesPath, "負責範圍", IdentityRules.MaxResponsibilitiesLength),
        ];
        IReadOnlyList<FailureDetail> issues = [.. found.OfType<FailureDetail>()];
        return issues.Count == 0 ? (jobTitle, responsibilities) : PersonInput.Invalid(issues);
    }

    private static string? Normalize(string? text)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static FailureDetail? CheckField(string? text, string path, string label, int maxLength)
    {
        if (text is null)
        {
            return null;
        }

        if (text.Length > maxLength)
        {
            return new(IdentityErrorCodes.ProfileInvalid, path, $"{label}不能超過 {maxLength} 個字。");
        }

        // 一行字：連換行也擋（控制字元），看不見的字元會做出看起來一樣的內容。
        return InputText.HasForbiddenCharacters(text) || InputText.HasInvisibleCharacters(text)
            ? new(IdentityErrorCodes.ProfileInvalid, path, $"{label}不能有換行、控制字元或看不見的字元。")
            : null;
    }
}
