using PmTool.Abstractions;

namespace PmTool.Ai;

/// <summary>
/// AI 助理跨專案查項目（find_items）：範圍是發問的人看得到的所有沒封存的專案，每個專案照他的權限（<c>ItemScope</c>：看得到全部的看全部、
/// 只看自己的只看自己負責或建立的）。內建工具與 MCP 的同名工具都用這一份，規則只有一份。規格見 <c>internal/HANDOFF-ai-find-items.md</c>。
/// </summary>
/// <remarks>
/// 給了 <paramref name="conversationId"/>（助理的對話、或本機與主機代理的工作所屬的對話）時，結果裡有「不是他負責也不是他建立」的項目的專案
/// 會記進這則對話讀過的專案（<c>ai.conversation_reads</c>）：之後他在那個專案只剩看自己的、或看不到那個專案時，這則對話照「讀過全部項目」的規則刪掉，
/// 舊對話裡才不會留著他已經看不到的內容。一般的 MCP 用戶端（OAuth）沒有對話，不記。
/// </remarks>
public interface IAssistantItemFinder
{
    /// <summary>
    /// 查詢條件不合格（日期格式、上限、控制字元、人員編號不在這個組織）回 <see cref="AiErrorCodes.FieldInvalid"/>，細項的路徑是參數名稱。
    /// 給了專案代號但看不到：原樣回傳 <c>project.not_found</c>。
    /// </summary>
    Task<Result<ItemFindResult>> FindAsync(Actor actor, Guid? conversationId, ItemFindQuery query, CancellationToken cancellationToken);
}

public enum ItemFindAssignee
{
    /// <summary>不限負責人。</summary>
    Anyone,

    /// <summary>發問的人自己。</summary>
    Me,

    /// <summary>沒有負責人的。</summary>
    Nobody,

    /// <summary><see cref="ItemFindQuery.AssigneeId"/> 那個人。</summary>
    Person,
}

/// <param name="Limit">1 到 <see cref="AiRules.MaxFindItemsLimit"/>。</param>
public sealed record ItemFindQuery(int Limit)
{
    public ItemFindAssignee Assignee { get; init; } = ItemFindAssignee.Anyone;

    /// <summary><see cref="Assignee"/> 是 <see cref="ItemFindAssignee.Person"/> 時必填：組織裡的人員編號。</summary>
    public Guid? AssigneeId { get; init; }

    /// <summary>建立的人；null 是不限。</summary>
    public Guid? CreatedBy { get; init; }

    /// <summary>只查這個專案；null 是跨所有看得到的專案。</summary>
    public string? ProjectKey { get; init; }

    /// <summary>狀態類別（<c>backlog</c>、<c>not_started</c>、<c>in_progress</c>、<c>done</c>、<c>canceled</c>）；空的是還沒結束的三類。</summary>
    public IReadOnlyList<string> StateCategories { get; init; } = [];

    public DateOnly? DueFrom { get; init; }

    public DateOnly? DueTo { get; init; }

    /// <summary>到期日在今天（組織時區）以前、還沒結束的；和 <see cref="DueTo"/> 取較早的，和 done／canceled 不能一起用。</summary>
    public bool Overdue { get; init; }

    /// <summary>標題或編號包含這段文字（和 search_items 一樣）；null 是不限。最長 <see cref="AiRules.MaxFindItemsTextLength"/>，擋控制字元。</summary>
    public string? Text { get; init; }
}

/// <param name="HasMore">還有符合的沒列出來（多查一筆判斷）。</param>
public sealed record ItemFindResult(IReadOnlyList<FoundItem> Items, bool HasMore);

/// <param name="Assignee">負責人；沒有是 null。</param>
public sealed record FoundItem(
    string Key,
    string ProjectKey,
    string ProjectName,
    string Title,
    string Type,
    string State,
    string StateCategory,
    DateOnly? DueDate,
    string Priority,
    FoundPerson? Assignee,
    FoundPerson CreatedBy);

public sealed record FoundPerson(Guid Id, string Name);
