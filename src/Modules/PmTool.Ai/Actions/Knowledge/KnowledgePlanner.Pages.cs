using PmTool.Knowledge;

namespace PmTool.Ai.Actions.Knowledge;

internal sealed partial class KnowledgePlanner
{
    /// <summary>頁面要放的位置：上層（編號、這份建議裡的 ref，或最上層）與它的深度、給人看的說法。</summary>
    private sealed record Location(int? ParentNumber, string? ParentRef, string Text, int ParentDepth, PageNode? ParentNode, TreeIndex? Tree);

    private async Task<PlannedChange?> CreatePageAsync(KnowledgeChange change, int index, CancellationToken cancellationToken)
    {
        if (await SpaceOfAsync(change, index, cancellationToken) is not { } space || !CanWritePages(space, index))
        {
            return null;
        }

        CheckBody(change.Body, index);
        var reference = change.Ref;
        if (reference is not null && _refs.ContainsKey(reference))
        {
            Issue(index, "ref", $"ref「{reference}」已經用過了。");
        }

        var before = Issues.Count;
        var location = await LocateAsync(change, index, space, cancellationToken);
        if (location is null || Issues.Count > before)
        {
            return null;
        }

        if (location.ParentDepth + 1 > KnowledgeRules.MaxDepth)
        {
            Issue(index, "parentNumber", $"放在那裡會超過 {KnowledgeRules.MaxDepth} 層，換一個比較上面的位置。");
            return null;
        }

        var title = change.Title!;
        if (reference is not null)
        {
            _refs[reference] = new CreatedPageRef(space.Key, title, location.ParentDepth + 1);
        }

        var place = location.Text + (change.BeforeNumber is { } next ? $"，排在 {next} 號頁面前面" : "，排在最後面");
        var resolved = change with { SpaceKey = space.Key, ParentNumber = location.ParentNumber };
        var step = new ActionStep("新增頁面", TargetOf(space.Key, title), null, change.Body is { Length: > 0 } ? change.Body : null, $"放在{place}", ActionTextFormat.Markdown);
        return new PlannedChange(resolved, step);
    }

    private async Task<PlannedChange?> UpdatePageAsync(KnowledgeChange change, int index, CancellationToken cancellationToken)
    {
        if (await SpaceOfAsync(change, index, cancellationToken) is not { } space || !CanWritePages(space, index))
        {
            return null;
        }

        CheckBody(change.Body, index);
        if (await PageOfAsync(space, change, index, cancellationToken) is not { } page)
        {
            return null;
        }

        if (page.Revision != change.ExpectedRevision)
        {
            Issue(index, "expectedRevision", $"頁面現在是第 {page.Revision} 版，你給的是第 {change.ExpectedRevision} 版：頁面在你讀過之後被改過了，請重新用 get_page 讀再提。");
            return null;
        }

        if (change.Body is not null && page.Body.Length > AiRules.MaxEditableProjectDescriptionLength)
        {
            Issue(index, "body", $"這一頁的內文有 {page.Body.Length} 個字，超過 {AiRules.MaxEditableProjectDescriptionLength} 字，讀不到全文，不能提改內文（只能改標題）。");
            return null;
        }

        var titleChanged = change.Title is not null && change.Title != page.Title;
        var bodyChanged = change.Body is not null && change.Body != page.Body;
        if (!titleChanged && !bodyChanged)
        {
            Issue(index, "", "標題與內文都和現在一樣，沒有要改的。");
            return null;
        }

        var resolved = change with
        {
            SpaceKey = space.Key,
            Title = titleChanged ? change.Title : null,
            Body = bodyChanged ? change.Body : null,
        };
        return new PlannedChange(resolved, UpdateStep(space.Key, page, resolved, titleChanged, bodyChanged));
    }

    private static ActionStep UpdateStep(string spaceKey, PageDetail page, KnowledgeChange change, bool titleChanged, bool bodyChanged)
    {
        var target = TargetOf(spaceKey, page.Title);
        if (!bodyChanged)
        {
            return new ActionStep("改頁面標題", target, page.Title, change.Title, null, ActionTextFormat.Text);
        }

        var note = titleChanged ? $"標題改成「{change.Title}」" : null;
        return new ActionStep("改頁面內容", target, page.Body, change.Body, note, ActionTextFormat.Markdown);
    }

    private async Task<PlannedChange?> MovePageAsync(KnowledgeChange change, int index, CancellationToken cancellationToken)
    {
        if (await SpaceOfAsync(change, index, cancellationToken) is not { } space || !CanWritePages(space, index)
            || await PageOfAsync(space, change, index, cancellationToken) is not { } page)
        {
            return null;
        }

        if (!page.MyPermissions.CanDelete)
        {
            Issue(index, "number", $"頁面 {space.Key}／{page.Number} 底下有你看不到的頁面，搬動會改變那些頁面誰看得到，只有組織管理員能搬。");
            return null;
        }

        if (await lookup.TreeAsync(space.Key, cancellationToken) is not { } tree || tree.Find(page.Number) is not { } node)
        {
            Issue(index, "number", $"頁面 {space.Key}／{page.Number} 不在頁面樹裡（可能在垃圾桶）。");
            return null;
        }

        var before = Issues.Count;
        var location = await LocateAsync(change, index, space, cancellationToken);
        if (location is null || Issues.Count > before)
        {
            return null;
        }

        if (!CheckMoveTarget(change, index, space.Key, tree, node, location))
        {
            return null;
        }

        var current = tree.ParentOf(node);
        var from = current is null ? "最上層" : $"「{current.Title}」底下";
        var place = location.Text + (change.BeforeNumber is { } next ? $"，排在 {next} 號頁面前面" : "，排在最後面");
        var resolved = change with { SpaceKey = space.Key, ParentNumber = location.ParentNumber };
        return new PlannedChange(resolved, new ActionStep("搬移頁面", TargetOf(space.Key, page.Title), from, place, "底下的頁面會跟著搬", ActionTextFormat.Text));
    }

    /// <summary>不能搬進自己底下、搬完不能太深、不能已經在那個位置；有問題就記下並回 false。</summary>
    private bool CheckMoveTarget(KnowledgeChange change, int index, string spaceKey, TreeIndex tree, PageNode node, Location location)
    {
        if (location.ParentNode is { } parent && tree.IsSameOrDescendant(parent, node))
        {
            Issue(index, "parentNumber", "不能搬到自己或自己底下的頁面底下。");
            return false;
        }

        var (_, height) = tree.SubtreeOf(node);
        if (location.ParentDepth + 1 + height > KnowledgeRules.MaxDepth)
        {
            Issue(index, "parentNumber", $"搬到那裡之後最深的子頁面會超過 {KnowledgeRules.MaxDepth} 層，換一個比較上面的位置。");
            return false;
        }

        if (IsAlreadyThere(tree.ChildrenOf(location.ParentNode), node, tree.ParentOf(node), location, change.BeforeNumber))
        {
            Issue(index, "", $"頁面 {spaceKey}／{node.Number} 已經在那個位置了。");
            return false;
        }

        return true;
    }

    private static bool IsAlreadyThere(IReadOnlyList<PageNode> siblings, PageNode node, PageNode? current, Location location, int? beforeNumber)
    {
        if (location.ParentRef is not null || current?.Id != location.ParentNode?.Id)
        {
            return false;
        }

        var position = siblings.ToList().FindIndex(sibling => sibling.Id == node.Id);
        return beforeNumber is null
            ? position == siblings.Count - 1
            : position + 1 < siblings.Count && siblings[position + 1].Number == beforeNumber;
    }

    private async Task<PlannedChange?> DeletePageAsync(KnowledgeChange change, int index, CancellationToken cancellationToken)
    {
        if (await SpaceOfAsync(change, index, cancellationToken) is not { } space || !CanWritePages(space, index)
            || await PageOfAsync(space, change, index, cancellationToken) is not { } page)
        {
            return null;
        }

        if (!page.MyPermissions.CanDelete)
        {
            Issue(index, "number", $"頁面 {space.Key}／{page.Number} 底下有你看不到的頁面，不能刪除（刪了那些頁面也會一起進垃圾桶）；請組織管理員處理。");
            return null;
        }

        var descendants = await lookup.TreeAsync(space.Key, cancellationToken) is { } tree && tree.Find(page.Number) is { } node
            ? tree.SubtreeOf(node).Count
            : 0;
        var note = descendants > 0 ? $"底下的 {descendants} 頁會一起進垃圾桶，可以復原" : "會進垃圾桶，可以復原";
        var resolved = change with { SpaceKey = space.Key };
        return new PlannedChange(resolved, new ActionStep("刪除頁面", TargetOf(space.Key, page.Title), null, null, note, ActionTextFormat.Text));
    }

    private async Task<PlannedChange?> RestorePageAsync(KnowledgeChange change, int index, CancellationToken cancellationToken)
    {
        if (await SpaceOfAsync(change, index, cancellationToken) is not { } space || !CanWritePages(space, index))
        {
            return null;
        }

        var entry = space.Existing is null ? null : await lookup.TrashEntryAsync(space.Key, change.Number!.Value, cancellationToken);
        if (entry is null)
        {
            Issue(index, "number", $"垃圾桶裡找不到頁面 {space.Key}／{change.Number}。用 list_page_trash 確認（只有每批被刪的那一頁能復原，底下的頁面會跟著回來）。");
            return null;
        }

        var from = entry.FormerParent is { } parent ? $"垃圾桶（刪除時在「{parent.Title}」底下）" : "垃圾桶（刪除時在最上層）";
        var note = entry.PageCount > 1 ? $"底下的 {entry.PageCount - 1} 頁會一起回來" : null;
        var resolved = change with { SpaceKey = space.Key };
        return new PlannedChange(resolved, new ActionStep("復原頁面", TargetOf(space.Key, entry.Page.Title), from, "放回原本的位置", note, ActionTextFormat.Text));
    }

    private async Task<Location?> LocateAsync(KnowledgeChange change, int index, SpaceTarget space, CancellationToken cancellationToken)
    {
        if (change.ParentRef is not null)
        {
            return LocateByRef(change, index, space);
        }

        if (space.Existing is null)
        {
            if (change.ParentNumber is not null || change.BeforeNumber is not null)
            {
                Issue(index, "parentNumber", $"空間「{space.Key}」是這份建議才新增的，裡面還沒有頁面，只能放最上層（或掛在前面新增的頁面底下，用 parentRef）。");
                return null;
            }

            return new Location(null, null, "最上層", 0, null, null);
        }

        if (await lookup.TreeAsync(space.Key, cancellationToken) is not { } tree)
        {
            Issue(index, "spaceKey", $"讀不到空間「{space.Key}」的頁面。");
            return null;
        }

        PageNode? parent = null;
        if (change.ParentNumber is { } parentNumber)
        {
            parent = tree.Find(parentNumber);
            if (parent is null)
            {
                Issue(index, "parentNumber", $"找不到上層頁面 {space.Key}／{parentNumber}：它不存在、在垃圾桶裡，或你看不到。");
                return null;
            }
        }

        if (change.BeforeNumber is { } beforeNumber && tree.ChildrenOf(parent).All(sibling => sibling.Number != beforeNumber || sibling.Number == change.Number))
        {
            Issue(index, "beforeNumber", $"beforeNumber 要是那個上層底下（同一層）的另一個頁面，{beforeNumber} 不是。");
            return null;
        }

        var text = parent is null ? "最上層" : $"「{parent.Title}」底下";
        return new Location(parent?.Number, null, text, parent is null ? 0 : tree.DepthOf(parent), parent, tree);
    }

    private Location? LocateByRef(KnowledgeChange change, int index, SpaceTarget space)
    {
        if (change.ParentNumber is not null)
        {
            Issue(index, "parentRef", "parentNumber 與 parentRef 只能給一個。");
            return null;
        }

        if (!_refs.TryGetValue(change.ParentRef!, out var made) || !string.Equals(made.SpaceKey, space.Key, StringComparison.OrdinalIgnoreCase))
        {
            Issue(index, "parentRef", $"parentRef「{change.ParentRef}」要是前面某個 create_page 的 ref，而且在同一個空間。");
            return null;
        }

        if (change.BeforeNumber is not null)
        {
            Issue(index, "beforeNumber", "掛在這份建議新增的頁面底下時，不能指定 beforeNumber（它底下還沒有別的頁面）。");
            return null;
        }

        return new Location(null, change.ParentRef, $"「{made.Title}」底下（這份建議新增的頁面）", made.Depth, null, null);
    }
}
