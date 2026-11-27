using PmTool.Ai.Items;
using PmTool.Work;

namespace PmTool.Ai.Proposals;

/// <summary>
/// 各種建議讀出來之後的一行說明（<see cref="ProposalStatusEntry.Summary"/>）：只放編號與數字、不放標題與名稱，
/// 那些是使用者寫的內容，不放進給模型的附註。
/// </summary>
internal static class ProposalSummaries
{
    public static ProposalStatusEntry Of(ItemProposal proposal)
    {
        var fields = Labels(proposal.Changes.Select(c => c.Field));
        var what = fields.Count > 0
            ? $"改 {proposal.ItemKey} 的 {string.Join("、", fields)}" + (proposal.Comment is null ? string.Empty : "，並加留言")
            : $"在 {proposal.ItemKey} 加留言";
        return Entry(proposal.Id.ToString(), ProposalKind.ItemChanges, State(proposal.Status), Partial(what, proposal.Error), proposal.CreatedAt, proposal.DecidedAt);
    }

    public static ProposalStatusEntry Of(ItemBatchProposal proposal)
    {
        var fields = Labels(proposal.Items.SelectMany(i => i.Changes.Select(c => c.Field)));
        var what = $"改 {proposal.Items.Count} 個項目：{KeyLists.Describe([.. proposal.Items.Select(i => i.ItemKey)])}"
            + (fields.Count > 0 ? $"，欄位 {string.Join("、", fields)}" : string.Empty);
        var failed = proposal.Items.Count(i => i.Error is not null);
        var text = failed > 0 ? $"{what}（有 {failed} 個項目沒完全套用）" : what;
        return Entry(proposal.Id.ToString(), ProposalKind.BulkItemChanges, State(proposal.Status), Partial(text, proposal.Error), proposal.CreatedAt, proposal.DecidedAt);
    }

    public static ProposalStatusEntry Of(NewItemProposal proposal)
    {
        var created = proposal.CreatedKeys.OfType<string>().ToList();
        var what = $"新增 {proposal.Items.Count} 個項目";
        if (created.Count > 0)
        {
            what += $"，已建立 {KeyLists.Describe(created)}";
            if (created.Count < proposal.Items.Count)
            {
                what += $"（只建立了 {created.Count} 個）";
            }
        }

        return Entry(
            proposal.Id.ToString(),
            ProposalKind.NewItems,
            proposal.Status switch
            {
                NewItemProposalStatus.Pending => ProposalState.Pending,
                NewItemProposalStatus.Applying => ProposalState.Applying,
                NewItemProposalStatus.Applied => ProposalState.Applied,
                _ => ProposalState.Dismissed,
            },
            what,
            proposal.CreatedAt,
            proposal.DecidedAt);
    }

    public static ProposalStatusEntry Of(ItemDeletionProposal proposal)
    {
        var what = proposal.Status == ItemProposalStatus.Applied
            ? $"刪除了 {proposal.DeletedKeys.Count} 個項目：{KeyLists.Describe(proposal.DeletedKeys)}"
            : $"刪除 {proposal.Items.Count} 個項目：{KeyLists.Describe([.. proposal.Items.Select(i => i.ItemKey)])}";
        return Entry(proposal.Id.ToString(), ProposalKind.ItemDeletions, State(proposal.Status), Partial(what, proposal.Error), proposal.CreatedAt, proposal.DecidedAt);
    }

    public static ProposalStatusEntry Of(MemberProposal proposal)
    {
        var added = proposal.Changes.Count(c => c.Role is not null && c.PreviousRole is null);
        var changed = proposal.Changes.Count(c => c.Role is not null && c.PreviousRole is not null);
        var removed = proposal.Changes.Count(c => c.Role is null);
        var parts = new List<string>();
        if (added > 0)
        {
            parts.Add($"把 {added} 個人加進專案");
        }

        if (changed > 0)
        {
            parts.Add($"改 {changed} 個人的角色");
        }

        if (removed > 0)
        {
            parts.Add($"把 {removed} 個人移出專案");
        }

        var state = proposal.Status switch
        {
            MemberProposalStatus.Pending => ProposalState.Pending,
            MemberProposalStatus.Applied => ProposalState.Applied,
            _ => ProposalState.Dismissed,
        };
        return Entry(proposal.Id.ToString(), ProposalKind.Members, state, Partial(string.Join("；", parts), proposal.Error), proposal.CreatedAt, proposal.DecidedAt);
    }

    public static ProposalStatusEntry Of(ProjectProposal proposal) => Entry(
        proposal.Id.ToString(),
        ProposalKind.Project,
        proposal.Status switch
        {
            ProjectProposalStatus.Pending => ProposalState.Pending,
            ProjectProposalStatus.Applied => ProposalState.Applied,
            _ => ProposalState.Dismissed,
        },
        proposal.CreatedProjectKey is { } created ? $"已建立專案 {created}" : $"新專案 {proposal.Key}",
        proposal.CreatedAt,
        proposal.DecidedAt);

    public static ProposalStatusEntry Of(ChangeSetSummary changeSet) => Entry(
        ProposalReference.ChangeSetText(changeSet.Number),
        ProposalKind.ChangeSet,
        changeSet.Status switch
        {
            ChangeSetStatus.Pending => ProposalState.Pending,
            ChangeSetStatus.Applied => ProposalState.Applied,
            ChangeSetStatus.Rejected => ProposalState.Rejected,
            _ => ProposalState.Dismissed,
        },
        changeSet.Status == ChangeSetStatus.Expired ? $"變更集 #{changeSet.Number}（基準版本已經不是目前的版本，過期了）" : $"變更集 #{changeSet.Number}",
        changeSet.ProposedAt,
        changeSet.DecidedAt);

    // 自訂欄位的 key 是藍圖裡管理員寫的內容，不放進給模型的附註；標準欄位的名稱是固定的。
    private static List<string> Labels(IEnumerable<string> fields) =>
        [.. fields.Select(f => f.StartsWith(ItemFieldNames.CustomPrefix, StringComparison.Ordinal) ? "自訂欄位" : f).Distinct(StringComparer.Ordinal)];

    private static ProposalState State(ItemProposalStatus status) => status switch
    {
        ItemProposalStatus.Pending => ProposalState.Pending,
        ItemProposalStatus.Applied => ProposalState.Applied,
        _ => ProposalState.Dismissed,
    };

    // 錯誤文字裡可能有名稱，不放進去；只告訴模型「有一部分沒成功」。
    private static string Partial(string what, string? error) => error is null ? what : $"{what}（有一部分沒有套用成功）";

    private static ProposalStatusEntry Entry(
        string reference, ProposalKind kind, ProposalState state, string summary, DateTimeOffset createdAt, DateTimeOffset? decidedAt) =>
        new(reference, kind, state, summary, createdAt, decidedAt);
}
