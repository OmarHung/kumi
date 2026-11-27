using PmTool.Work;

namespace PmTool.Ai.Items;

/// <summary>
/// 檢查修改建議時需要先去工作引擎查的東西（上層、週期、里程碑、要回覆的留言）。檢查本身是純計算，
/// 查的部分都在 <see cref="ItemChangeFactsLoader"/>，而且只查這份建議真的用到的。
/// </summary>
internal sealed record ItemChangeFacts
{
    public static readonly ItemChangeFacts None = new();

    /// <summary>要掛上去的上層（<c>parentKey</c> 給了編號時）；找不到、看不到、不在這個專案是 null。</summary>
    public WorkItem? NewParent { get; init; }

    /// <summary>新上層與它往上的每一層的編號（讀得到的部分）；用來擋「掛到自己或自己的下層底下」。</summary>
    public IReadOnlySet<string> NewParentChain { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>項目現在的上層（改類型時，上層的類型要允許新類型）；沒有或讀不到是 null。</summary>
    public WorkItem? CurrentParent { get; init; }

    /// <summary>項目沒刪除的下層（改類型時，每個下層都要允許掛在新類型底下）。</summary>
    public IReadOnlyList<WorkItem> Children { get; init; } = [];

    /// <summary>這個專案的週期 id；null 是讀不到（沒有 item.read）。沒用到時是空的。</summary>
    public IReadOnlySet<Guid>? Cycles { get; init; } = new HashSet<Guid>();

    /// <summary>這個專案的里程碑 id；null 是讀不到。沒用到時是空的。</summary>
    public IReadOnlySet<Guid>? Milestones { get; init; } = new HashSet<Guid>();

    /// <summary>要回覆的留言（建立建議時的作者與摘錄）；沒給 replyTo 或有問題時是 null。</summary>
    public CommentReplyTarget? Reply { get; init; }

    /// <summary>要回覆的留言有問題時的說明（找不到、已刪除）。</summary>
    public string? ReplyProblem { get; init; }
}
