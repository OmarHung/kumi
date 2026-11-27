using System.Globalization;
using PmTool.Ai.Assistant;

namespace PmTool.Ai.Proposals;

/// <summary>
/// 對話裡的一份建議：種類加編號（變更集是專案裡的號碼，其他是 GUID）。給模型與 withdraw_proposal 的字串寫法是
/// 建議的編號（GUID），變更集是 <c>changeset:12</c>。
/// </summary>
internal sealed record ProposalReference(ProposalKind Kind, Guid? Id, long? Number)
{
    private const string ChangeSetPrefix = "changeset:";

    public string Text => Kind == ProposalKind.ChangeSet ? ChangeSetText(Number!.Value) : Id!.Value.ToString();

    public static string ChangeSetText(long number) => ChangeSetPrefix + number.ToString(CultureInfo.InvariantCulture);

    /// <summary>模型給的字串；寫法不對是 null。種類要對照這則對話的工具段落才知道，這裡只管看得懂。</summary>
    public static (Guid? Id, long? Number)? Parse(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.StartsWith(ChangeSetPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return long.TryParse(trimmed[ChangeSetPrefix.Length..].Trim().TrimStart('#'), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0
                ? (null, number)
                : null;
        }

        return Guid.TryParse(trimmed, out var id) ? (id, null) : null;
    }

    /// <summary>一則助理訊息的工具段落裡記著的建議，依段落的順序。</summary>
    public static IEnumerable<ProposalReference> From(AssistantToolPart tool)
    {
        if (tool.ChangeSetNumber is { } number)
        {
            yield return new ProposalReference(ProposalKind.ChangeSet, null, number);
        }

        foreach (var (kind, id) in IdsOf(tool))
        {
            yield return new ProposalReference(kind, id, null);
        }
    }

    private static IEnumerable<(ProposalKind Kind, Guid Id)> IdsOf(AssistantToolPart tool)
    {
        if (tool.ItemProposalId is { } item)
        {
            yield return (ProposalKind.ItemChanges, item);
        }

        if (tool.ItemBatchProposalId is { } batch)
        {
            yield return (ProposalKind.BulkItemChanges, batch);
        }

        if (tool.NewItemProposalId is { } newItems)
        {
            yield return (ProposalKind.NewItems, newItems);
        }

        if (tool.ItemDeletionProposalId is { } deletion)
        {
            yield return (ProposalKind.ItemDeletions, deletion);
        }

        if (tool.MemberProposalId is { } members)
        {
            yield return (ProposalKind.Members, members);
        }

        if (tool.ProjectProposalId is { } project)
        {
            yield return (ProposalKind.Project, project);
        }
    }
}
