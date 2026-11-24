using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Knowledge.Access;
using PmTool.Knowledge.Common;
using PmTool.Knowledge.Persistence;

namespace PmTool.Knowledge.Pages;

internal sealed partial class KnowledgePageService
{
    /// <summary>
    /// 送來的名單長度的上限（含重複）。去重之後才有人數的上限，但太長的名單不必讀完、查完，直接擋下；
    /// 留一點餘裕給「重複貼了幾次」的名單。
    /// </summary>
    private const int MaxRawRestrictionEntries = KnowledgeRules.MaxRestrictedPeople * 2;

    public async Task<Result<PageRestriction>> SetRestrictionAsync(
        Actor actor, string spaceKey, int pageNumber, IReadOnlyList<Guid>? people, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var opened = await OpenTreeWriteAsync(db, actor, spaceKey, pageNumber, cancellationToken);
        if (opened.Failure is { } failure)
        {
            return failure;
        }

        await using var transaction = opened.Value.Transaction;
        var target = opened.Value.Target;
        var wanted = await CheckRestrictionAsync(actor, people, cancellationToken);
        if (wanted.Failure is { } invalid)
        {
            return invalid;
        }

        var current = await BuildRestrictionAsync(db, target, cancellationToken);
        if (SameList(current.People, wanted.Value))
        {
            return current;
        }

        // 整份取代：先清掉舊的名單再寫新的，旗標跟著名單有沒有人。
        await db.PageRestrictions.Where(r => r.OrganizationId == actor.OrganizationId && r.PageId == target.Page.Id)
            .ExecuteDeleteAsync(cancellationToken);
        if (wanted.Value is { } listed)
        {
            db.PageRestrictions.AddRange(listed.Select((person, position) => new PageRestrictionRow
            {
                OrganizationId = actor.OrganizationId,
                PageId = target.Page.Id,
                PersonId = person,
                Position = position,
            }));
        }

        var isRestricted = wanted.Value is not null;
        await db.Pages.Where(p => p.OrganizationId == actor.OrganizationId && p.Id == target.Page.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.IsRestricted, isRestricted), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PageRestriction(wanted.Value, current.RestrictedAncestors);
    }

    /// <summary>
    /// 整理要設定的名單：空的是拿掉（null）；有名單時一定包含設定的人（沒列就加在最前面）、重複的去掉；
    /// 人數（含自動加的）不能超過上限，每個人都要在這個組織裡（停用的人也算）。
    /// </summary>
    private async Task<Result<IReadOnlyList<Guid>?>> CheckRestrictionAsync(
        Actor actor, IReadOnlyList<Guid>? people, CancellationToken cancellationToken)
    {
        if (people is null || people.Count == 0)
        {
            return (List<Guid>?)null;
        }

        if (people.Count > MaxRawRestrictionEntries)
        {
            return TooManyPeople();
        }

        // 用 HashSet 去重、保持第一次出現的順序；設定的人沒列就加在最前面。
        var seen = new HashSet<Guid>();
        var distinct = people.Where(seen.Add).ToList();
        if (seen.Add(actor.PersonId))
        {
            distinct.Insert(0, actor.PersonId);
        }

        if (distinct.Count > KnowledgeRules.MaxRestrictedPeople)
        {
            return TooManyPeople();
        }

        var known = await directory.FindManyAsync(actor.OrganizationId, [.. people], cancellationToken);
        var unknown = people.Select((person, index) => (person, index)).Where(e => !known.ContainsKey(e.person)).ToList();
        return unknown.Count > 0
            ? KnowledgeFailures.Invalid(
                [.. unknown.Select(e => new FailureDetail(KnowledgeErrorCodes.RestrictionInvalid, $"people[{e.index}]", "這個人不在組織裡。"))])
            : distinct;
    }

    private static Failure TooManyPeople() =>
        KnowledgeFailures.Invalid(
            KnowledgeErrorCodes.RestrictionInvalid, "people", $"一頁最多限制給 {KnowledgeRules.MaxRestrictedPeople} 個人看。");

    private static bool SameList(IReadOnlyList<Guid>? current, IReadOnlyList<Guid>? wanted) =>
        current is null || wanted is null ? current is null && wanted is null : current.SequenceEqual(wanted);
}
