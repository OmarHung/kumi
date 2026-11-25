using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Identity;
using PmTool.Notifications.Persistence;

namespace PmTool.Notifications.Email;

/// <summary>要寄給一個人的一封信，以及寄出之後要標成處理完了的通知。</summary>
internal sealed record PreparedEmail(Guid RecipientId, OutgoingEmail Email, IReadOnlyList<Guid> NotificationIds);

internal sealed partial class EmailDispatcher
{
    /// <summary>
    /// 有還在等的項目通知、而且最舊的已經超過 <see cref="EmailRules.Delay"/> 的人，或有還在等的帳號通知的人（不等延遲，也不看讀了沒有）；
    /// 最舊的先寄，一輪最多 <see cref="EmailRules.MaxRecipientsPerRun"/> 個。
    /// </summary>
    private async Task<List<Guid>> DueRecipientsAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var cutoff = clock.GetUtcNow() - EmailRules.Delay;
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Notifications.AsNoTracking()
            .Where(n => n.OrganizationId == organizationId && n.EmailHandledAt == null
                && (n.Kind == NotificationKind.AccountSecurity || (n.ReadAt == null && n.CreatedAt <= cutoff)))
            .GroupBy(n => n.RecipientId)
            .Select(g => new { Recipient = g.Key, Oldest = g.Min(n => n.CreatedAt) })
            .OrderBy(x => x.Oldest)
            .Take(EmailRules.MaxRecipientsPerRun)
            .Select(x => x.Recipient)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 取這個人所有還在等的通知（不只超過延遲的那幾則，一起寄），決定寄不寄、寄什麼。
    /// 不必寄的（他關掉了、沒有 Email、帳號停用了、不在組織了、項目現在看不到、項目的通知已經讀了）當場標成處理完了；沒有信要寄是空的。
    /// 項目的通知併成一封；帳號通知另外一封，不和項目的通知放在一起（不連到項目、讀了也寄）。
    /// </summary>
    private async Task<IReadOnlyList<PreparedEmail>> PrepareAsync(
        EmailSettingsRow settings,
        Guid recipientId,
        (string Address, string? Name) sender,
        TimeZoneInfo zone,
        CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var pending = await db.Notifications.AsNoTracking()
            .Where(n => n.OrganizationId == settings.OrganizationId && n.RecipientId == recipientId && n.EmailHandledAt == null)
            .OrderBy(n => n.CreatedAt).ThenBy(n => n.Id)
            .ToListAsync(cancellationToken);
        if (pending.Count == 0)
        {
            return [];
        }

        var enabled = await db.EmailPreferences
            .Where(p => p.OrganizationId == settings.OrganizationId && p.PersonId == recipientId)
            .Select(p => (bool?)p.Enabled)
            .SingleOrDefaultAsync(cancellationToken) ?? true;
        Guid[] personIds = [recipientId, .. pending.Select(n => n.ActorPersonId).Distinct()];
        var names = await people.FindManyAsync(settings.OrganizationId, personIds, cancellationToken);
        var recipient = names.GetValueOrDefault(recipientId);
        if (!enabled || recipient is not { IsDisabled: false, Email: { } address } || string.IsNullOrWhiteSpace(address))
        {
            await MarkHandledAsync(db, [.. pending.Select(n => n.Id)], cancellationToken);
            return [];
        }

        if (!EmailSettingsValidator.IsDeliverable(address.Trim()))
        {
            // 地址寄信的函式庫解不開（例如帶逗號）：永遠寄不出去，不讓它卡住整個組織。
            LogInvalidAddress(logger, settings.OrganizationId, recipientId);
            await MarkHandledAsync(db, [.. pending.Select(n => n.Id)], cancellationToken);
            return [];
        }

        var actor = new Actor(settings.OrganizationId, recipientId, recipient.Role is OrganizationRole.Owner or OrganizationRole.Admin);
        var visible = await (await visibility.VisibleToAsync(db, actor, cancellationToken))
            .Where(n => n.EmailHandledAt == null && (n.ReadAt == null || n.Kind == NotificationKind.AccountSecurity))
            .OrderBy(n => n.CreatedAt).ThenBy(n => n.Id)
            .ToListAsync(cancellationToken);

        // 看不到的與剛剛才讀的：不寄，也不必再等。
        var visibleIds = visible.Select(n => n.Id).ToHashSet();
        await MarkHandledAsync(db, [.. pending.Select(n => n.Id).Where(id => !visibleIds.Contains(id))], cancellationToken);
        if (visible.Count == 0)
        {
            return [];
        }

        var context = new EmailComposer.Context(
            sender.Address,
            sender.Name,
            address.Trim(),
            settings.SiteUrl ?? string.Empty,
            zone,
            names.ToDictionary(pair => pair.Key, pair => pair.Value.DisplayName));
        return Compose(context, recipientId, visible);
    }

    private static List<PreparedEmail> Compose(EmailComposer.Context context, Guid recipientId, IReadOnlyList<NotificationRow> visible)
    {
        List<PreparedEmail> emails = [];
        var items = visible.Where(n => n.Kind != NotificationKind.AccountSecurity).ToList();
        if (items.Count > 0)
        {
            emails.Add(new PreparedEmail(recipientId, EmailComposer.Notification(context, items), [.. items.Select(n => n.Id)]));
        }

        var accounts = visible.Where(n => n.Kind == NotificationKind.AccountSecurity).ToList();
        if (accounts.Count > 0)
        {
            emails.Add(new PreparedEmail(recipientId, EmailComposer.AccountNotices(context, accounts), [.. accounts.Select(n => n.Id)]));
        }

        return emails;
    }

    private async Task MarkHandledAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await MarkHandledAsync(db, ids, cancellationToken);
    }

    private async Task MarkHandledAsync(NotificationsDbContext db, IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return;
        }

        var now = clock.GetUtcNow();
        Guid[] idArray = [.. ids];
        await db.Notifications
            .Where(n => idArray.Contains(n.Id) && n.EmailHandledAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(n => n.EmailHandledAt, now), cancellationToken);
    }
}
