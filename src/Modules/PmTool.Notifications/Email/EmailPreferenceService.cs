using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Notifications.Persistence;

namespace PmTool.Notifications.Email;

/// <summary>每個人自己要不要收 Email 通知。沒有存過就是要收。</summary>
internal sealed class EmailPreferenceService(IDbContextFactory<NotificationsDbContext> contexts, TimeProvider clock) : IEmailPreferenceService
{
    public async Task<EmailPreference> GetAsync(Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var enabled = await db.EmailPreferences
            .Where(p => p.OrganizationId == actor.OrganizationId && p.PersonId == actor.PersonId)
            .Select(p => (bool?)p.Enabled)
            .SingleOrDefaultAsync(cancellationToken);
        return new EmailPreference(enabled ?? true, await IsOrganizationEnabledAsync(db, actor.OrganizationId, cancellationToken));
    }

    public async Task<EmailPreference> SetAsync(Actor actor, bool enabled, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // 一句 upsert：同一個人在兩個分頁同時按，也不會撞到唯一鍵。
        var now = clock.GetUtcNow();
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO notifications.email_preferences (organization_id, person_id, enabled, updated_at)
            VALUES ({actor.OrganizationId}, {actor.PersonId}, {enabled}, {now})
            ON CONFLICT (organization_id, person_id) DO UPDATE SET enabled = EXCLUDED.enabled, updated_at = EXCLUDED.updated_at
            """,
            cancellationToken);
        return new EmailPreference(enabled, await IsOrganizationEnabledAsync(db, actor.OrganizationId, cancellationToken));
    }

    private static Task<bool> IsOrganizationEnabledAsync(NotificationsDbContext db, Guid organizationId, CancellationToken cancellationToken) =>
        db.EmailSettings.AnyAsync(s => s.OrganizationId == organizationId && s.Enabled, cancellationToken);
}
