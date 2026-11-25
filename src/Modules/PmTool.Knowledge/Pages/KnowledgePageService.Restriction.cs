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
        if (await HasHiddenPagesBelowAsync(db, actor, target, cancellationToken))
        {
            return HiddenDescendantsConflict("改限制");
        }

        var wanted = await CheckRestrictionAsync(actor, people, cancellationToken);
        if (wanted.Failure is { } invalid)
        {
            return invalid;
        }

        var current = await BuildRestrictionAsync(db, actor, target, cancellationToken);
        if (SameList(current.People, wanted.Value))
        {
            return current;
        }

        // 放寬要設定的人或管理者：在鎖裡、「底下有看不到的頁面」之後檢查，檢查與寫入之間沒有空窗。
        if (IsRelaxing(current.People, wanted.Value) && !CanRelax(actor, target.Space, target.Page.RestrictedBy))
        {
            return KnowledgeFailures.RestrictionOwnerRequired();
        }

        // 沒有限制 → 有限制：執行的人是擁有者；拿掉：清掉；其餘（只刪人、換順序、管理者加人）不轉移：
        // 乙把甲設的名單縮小之後若變成擁有者，就能再把別人加回去，等於繞過甲。
        var owner = wanted.Value is null ? null : current.People is null ? actor.PersonId : target.Page.RestrictedBy;

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
            .ExecuteUpdateAsync(
                set => set.SetProperty(p => p.IsRestricted, isRestricted).SetProperty(p => p.RestrictedBy, owner),
                cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ComposeRestriction(actor, target.Space, wanted.Value, owner, current.RestrictedAncestors);
    }

    /// <summary>原本有名單，新的是沒有、或多了原本沒有的人，就是放寬。原本沒有名單時任何新名單都是收緊。</summary>
    private static bool IsRelaxing(IReadOnlyList<Guid>? current, IReadOnlyList<Guid>? wanted) =>
        current is not null && (wanted is null || wanted.Any(person => !current.Contains(person)));

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
