using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.Settings;

/// <summary>
/// 連線屬於誰：組織（<see cref="PersonId"/> 是 null）或某個人。組織的服務與個人的服務共用同一套連線的程式，
/// 靠這個決定查哪一組、鎖哪一把、上限多少、要不要處理「預設」。
/// </summary>
internal sealed record ConnectionOwner(Guid OrganizationId, Guid? PersonId)
{
    public bool IsPersonal => PersonId is not null;

    public int MaxConnections => IsPersonal ? AiRules.MaxPersonalConnections : AiRules.MaxConnectionsPerOrganization;

    public string Subject => IsPersonal ? "一個人" : "一個組織";

    public static ConnectionOwner Organization(Guid organizationId) => new(organizationId, null);

    public static ConnectionOwner Personal(Actor actor) => new(actor.OrganizationId, actor.PersonId);

    /// <summary>
    /// 寫入排隊的鎖。組織的和「設定」（開關與預設）同一把：兩個管理員同時改，預設不會指到剛被刪掉的模型。
    /// 個人的只鎖這個人：預設的規則不適用，不必和組織的、別人的排隊。
    /// </summary>
    public Task LockAsync(AiDbContext db, CancellationToken cancellationToken)
    {
        var key = PersonId is { } person ? $"ai_personal:{OrganizationId}:{person}" : $"ai_settings:{OrganizationId}";
        return db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }
}

internal static class ConnectionOwnerQueries
{
    /// <summary>只留這個擁有者的連線，也一定用組織過濾。別人的、組織的（或個人的）都看不到。</summary>
    public static IQueryable<AiConnectionRow> OwnedBy(this IQueryable<AiConnectionRow> connections, ConnectionOwner owner) =>
        owner.PersonId is { } person
            ? connections.Where(c => c.OrganizationId == owner.OrganizationId && c.OwnerPersonId == person)
            : connections.Where(c => c.OrganizationId == owner.OrganizationId && c.OwnerPersonId == null);
}

internal static class AiPersonalPolicy
{
    /// <summary>組織現在允許成員接自己的 AI 帳號嗎（<see cref="AiSettings.AllowPersonalConnections"/>）。</summary>
    public static async Task<bool> IsAllowedAsync(
        IDbContextFactory<AiDbContext> contexts, Guid organizationId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Settings.AsNoTracking()
            .AnyAsync(s => s.OrganizationId == organizationId && s.AllowPersonalConnections, cancellationToken);
    }
}
