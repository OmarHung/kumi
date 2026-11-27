using PmTool.Knowledge;

namespace PmTool.Ai.Actions.Knowledge;

internal sealed partial class KnowledgePlanner
{
    private static string DescribeSpace(string name, string? description, string? projectKey)
    {
        var parts = new List<string> { name };
        if (!string.IsNullOrWhiteSpace(description))
        {
            parts.Add(description);
        }

        parts.Add(projectKey is null ? "組織空間" : $"綁專案 {projectKey}");
        return string.Join(" · ", parts);
    }

    private async Task<PlannedChange?> CreateSpaceAsync(KnowledgeChange change, int index, CancellationToken cancellationToken)
    {
        var key = change.Key!;
        if (change.ProjectKey is null && !actor.IsOrganizationAdmin)
        {
            Forbid(index, "", "只有組織管理員能建立組織空間。你可以請組織管理員來做；綁專案的空間要有管理那個專案的權限。");
            return null;
        }

        var before = Issues.Count;
        if (_newSpaces.ContainsKey(key) || (await lookup.AllSpacesAsync(cancellationToken)).Any(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase)))
        {
            Issue(index, "key", $"空間代號「{key}」已經有人用了（封存的也算），換一個。");
        }

        if (change.ProjectKey is { } projectKey
            && (await lookup.AllSpacesAsync(cancellationToken)).Any(s => string.Equals(s.Project?.Key, projectKey, StringComparison.OrdinalIgnoreCase)))
        {
            Issue(index, "projectKey", $"專案 {projectKey} 已經有空間了，一個專案最多一個空間。");
        }

        if (Issues.Count > before)
        {
            return null;
        }

        var projectUpper = change.ProjectKey?.ToUpperInvariant();
        _newSpaces[key] = new SpaceTarget(key, change.Name!, null);
        var resolved = change with { ProjectKey = projectUpper };
        var step = new ActionStep("新增空間", $"{key}（{change.Name}）", null, DescribeSpace(change.Name!, change.Description, projectUpper), null, ActionTextFormat.Text);
        return new PlannedChange(resolved, step);
    }

    private async Task<PlannedChange?> UpdateSpaceAsync(KnowledgeChange change, int index, CancellationToken cancellationToken)
    {
        if (await SpaceOfAsync(change, index, cancellationToken) is not { } target || !CanManageSpace(target, index, "改"))
        {
            return null;
        }

        if (target.Existing is not { } space)
        {
            Issue(index, "spaceKey", $"空間「{target.Key}」是這份建議才新增的，不用再改設定，直接在新增時給名稱與簡介。");
            return null;
        }

        // 簡介沒給就維持原樣：知識庫的更新是整份取代，沒給卻清掉會是模型沒想到的結果。
        var description = change.Description ?? space.Description;
        if (change.Name == space.Name && description == space.Description)
        {
            Issue(index, "", "名稱與簡介都和現在一樣，沒有要改的。");
            return null;
        }

        var resolved = change with { SpaceKey = space.Key, Description = description };
        var step = new ActionStep(
            "改空間設定", space.Key, DescribeSpace(space.Name, space.Description, space.Project?.Key), DescribeSpace(change.Name!, description, space.Project?.Key), null, ActionTextFormat.Text);
        return new PlannedChange(resolved, step);
    }

    private async Task<PlannedChange?> ArchiveSpaceAsync(KnowledgeChange change, int index, CancellationToken cancellationToken)
    {
        var archive = change.Action == KnowledgeActions.ArchiveSpace;
        if (await SpaceOfAsync(change, index, cancellationToken) is not { } target || !CanManageSpace(target, index, archive ? "封存" : "取消封存"))
        {
            return null;
        }

        if (target.Existing is not { } space)
        {
            Issue(index, "spaceKey", $"空間「{target.Key}」是這份建議才新增的，不用{(archive ? "封存" : "取消封存")}。");
            return null;
        }

        if ((space.ArchivedAt is not null) == archive)
        {
            Issue(index, "spaceKey", $"空間「{space.Key}」已經是{(archive ? "封存" : "沒封存")}的了。");
            return null;
        }

        var resolved = change with { SpaceKey = space.Key };
        var step = archive
            ? new ActionStep("封存空間", $"{space.Key}（{space.Name}）", "使用中", "封存（唯讀）", "裡面的頁面照舊看得到，可以再取消封存", ActionTextFormat.Text)
            : new ActionStep("取消封存空間", $"{space.Key}（{space.Name}）", "封存（唯讀）", "使用中", null, ActionTextFormat.Text);
        return new PlannedChange(resolved, step);
    }

    private async Task<PlannedChange?> SetRestrictionAsync(KnowledgeChange change, int index, CancellationToken cancellationToken)
    {
        if (await SpaceOfAsync(change, index, cancellationToken) is not { } space || !CanWritePages(space, index)
            || await PageOfAsync(space, change, index, cancellationToken) is not { } page)
        {
            return null;
        }

        var people = change.People ?? [];
        if (people.Count > KnowledgeRules.MaxRestrictedPeople)
        {
            Issue(index, "people", $"一頁最多限制給 {KnowledgeRules.MaxRestrictedPeople} 個人看。");
            return null;
        }

        var current = await lookup.RestrictionAsync(space.Key, page.Number, cancellationToken);
        var currentPeople = current?.People ?? [];
        var known = await lookup.PeopleAsync(PeopleToLookUp(people, current), cancellationToken);
        if (!CheckRestriction(index, space.Key, page, people, current, known))
        {
            return null;
        }

        var resolved = change with { SpaceKey = space.Key, People = people };
        var note = people.Count > 0 && !people.Contains(actor.PersonId) ? "套用的人會自動加進名單" : null;
        var step = new ActionStep(
            "改頁面限制", TargetOf(space.Key, page.Title), DescribeRestriction(currentPeople, known), DescribeRestriction(people, known), note, ActionTextFormat.Text);
        return new PlannedChange(resolved, step);
    }

    private static List<Guid> PeopleToLookUp(IReadOnlyList<Guid> people, PageRestriction? current)
    {
        var wanted = new List<Guid>([.. people, .. current?.People ?? []]);
        if (current?.RestrictedBy is { } restrictedBy)
        {
            wanted.Add(restrictedBy);
        }

        return wanted;
    }

    /// <summary>名單裡的人、底下看不到的頁面、和現在一樣、放寬的權限；有問題就記下並回 false。</summary>
    private bool CheckRestriction(
        int index,
        string spaceKey,
        PageDetail page,
        IReadOnlyList<Guid> people,
        PageRestriction? current,
        IReadOnlyDictionary<Guid, PmTool.Identity.PersonSummary> known)
    {
        var before = Issues.Count;
        for (var i = 0; i < people.Count; i++)
        {
            if (!known.ContainsKey(people[i]))
            {
                Issue(index, $"people[{i}]", "找不到這個人（要是組織裡的人員編號，用 list_people 找）。");
            }
        }

        if (Issues.Count > before)
        {
            return false;
        }

        if (!page.MyPermissions.CanDelete)
        {
            Issue(index, "number", $"頁面 {spaceKey}／{page.Number} 底下有你看不到的頁面，改限制會改變那些頁面誰看得到，只有組織管理員能改。");
            return false;
        }

        var currentPeople = current?.People ?? [];
        if (people.Count == currentPeople.Count && people.All(currentPeople.Contains))
        {
            Issue(index, "people", "限制的名單和現在一樣，沒有要改的。");
            return false;
        }

        return CanRelaxIfNeeded(index, people, current, known);
    }

    private bool CanRelaxIfNeeded(
        int index, IReadOnlyList<Guid> people, PageRestriction? current, IReadOnlyDictionary<Guid, PmTool.Identity.PersonSummary> known)
    {
        var currentPeople = current?.People ?? [];
        var isRelaxing = people.Count == 0 || people.Any(id => !currentPeople.Contains(id));
        if (current is not { People.Count: > 0, CanRelax: false } || !isRelaxing)
        {
            return true;
        }

        var owner = current.RestrictedBy is { } id && known.TryGetValue(id, out var person) ? $"「{person.DisplayName}」" : "設定這個限制的人";
        Forbid(index, "people", $"放寬限制（拿掉限制或加人）只有{owner}與空間管理者能做，你只能把人從名單拿掉（收緊）。");
        return false;
    }

    private static string DescribeRestriction(IReadOnlyList<Guid> people, IReadOnlyDictionary<Guid, PmTool.Identity.PersonSummary> known) =>
        people.Count == 0
            ? "沒有自己的限制（照上層的規則）"
            : "只有這些人看得到：" + string.Join("、", people.Select(id => known.TryGetValue(id, out var person) ? person.DisplayName : "（已不在組織）"));
}
