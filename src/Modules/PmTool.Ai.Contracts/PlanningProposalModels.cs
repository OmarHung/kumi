using PmTool.Abstractions;

namespace PmTool.Ai;

/// <summary>
/// AI 助理提出的「週期與里程碑」建議（內建的 propose_planning_changes，或本機、主機代理經 MCP 的同名工具）：新增、修改、刪除，
/// 一份最多 <see cref="AiRules.MaxPlanningChangesPerProposal"/> 筆。使用者在網頁按「套用」才以自己的身分照工作引擎的規則寫入
/// （<c>IPlanningService</c>），按「不要」就作廢。只有建立它的人看得到、能套用；別人的、不存在的、專案已經看不到的一律
/// <see cref="AiErrorCodes.PlanningProposalNotFound"/>。規格見 <c>internal/HANDOFF-ai-coverage.md</c> 5.3。
/// </summary>
public interface IPlanningProposalService
{
    Task<Result<PlanningProposal>> GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);

    /// <summary>
    /// 新增週期、里程碑不是冪等的（重做會多一個），所以照新增項目建議的做法：先在建議的 advisory lock 裡標成
    /// <see cref="NewItemProposalStatus.Applying"/> 並提交，再照順序一筆一筆寫，每寫好一筆立刻記下結果（新增的記 id）並刷新
    /// <see cref="PlanningProposal.DecidedAt"/>（心跳）。只有第一筆被工作引擎明確拒絕（還沒寫入）才回到待決定、回傳它的失敗；
    /// 任何例外（含逾時）都維持建立中；之後的失敗不復原已經寫的、繼續下一筆，每一筆的結果在 <see cref="PlanningChange.Error"/>。
    /// 停在建立中超過 <see cref="AiRules.NewItemProposalApplyStale"/> 讀出來是已套用，<see cref="PlanningProposal.Error"/> 說明過程中斷了。
    /// 已經套用、作廢或建立中的：<see cref="AiErrorCodes.PlanningProposalClosed"/>。
    /// </summary>
    Task<Result<PlanningProposal>> ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);

    /// <summary>作廢。已經套用、作廢或建立中的：<see cref="AiErrorCodes.PlanningProposalClosed"/>。</summary>
    Task<Result<PlanningProposal>> DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);
}

/// <summary>建立週期與里程碑建議。內建助理與 MCP 的 propose_planning_changes 都呼叫它，檢查的規則只有一份。</summary>
public interface IPlanningProposalCreator
{
    /// <summary>
    /// 專案要看得到（不然原樣回傳 <c>project.not_found</c>）；使用者沒有 <c>item.update</c> 或看不到專案的全部項目時回
    /// <see cref="AiErrorCodes.Forbidden"/>，訊息照成員建議的句型寫出能做的人（專案的管理者與組織管理員）。
    /// 每一筆的問題回 <see cref="AiErrorCodes.FieldInvalid"/>，路徑 <c>changes[1].endDate</c>，一次回全部，什麼都不存：
    /// <list type="bullet">
    /// <item>1 到 <see cref="AiRules.MaxPlanningChangesPerProposal"/> 筆；同一個週期或里程碑只能出現一次（新增的不算）；</item>
    /// <item>新增：週期要 name、startDate、endDate，里程碑要 name、dueDate（description 可以不給），不能給 id；</item>
    /// <item>修改、刪除：id 要是這個專案的週期或里程碑；修改至少有一個欄位真的有變，沒給的照現在的值補齊；</item>
    /// <item>週期不能給 dueDate、description，里程碑不能給 startDate、endDate；</item>
    /// <item>名稱去掉前後空白後 1 到 <c>PlanningRules.MaxCycleNameLength</c>／<c>MaxMilestoneNameLength</c> 字、不能有控制字元；
    ///   日期是 yyyy-MM-dd，週期的開始不能晚於結束；說明照里程碑的規則。</item>
    /// </list>
    /// 存起來的每一筆帶「改之前」（修改、刪除）與「改之後」（新增、修改）的完整內容，以及刪除時裡面有幾個項目（給卡片顯示）。
    /// </summary>
    Task<Result<PlanningProposal>> CreateAsync(Actor actor, PlanningProposalDraft draft, CancellationToken cancellationToken);
}

public sealed record PlanningProposalDraft(string ProjectKey, IReadOnlyList<PlanningChangeDraft> Changes)
{
    /// <summary>內建助理建立的才有；MCP 是 null。</summary>
    public Guid? ConversationId { get; init; }

    /// <summary>為什麼這樣排（一行，最長 200 字），給卡片顯示；可以不給。擋控制字元。</summary>
    public string? Reason { get; init; }
}

public enum PlanningTarget
{
    Cycle,
    Milestone,
}

public enum PlanningAction
{
    Create,
    Update,
    Delete,
}

/// <summary>模型給的一筆。沒給的欄位是 null；修改時 null 表示「不改」（里程碑的說明要清掉用空字串）。</summary>
public sealed record PlanningChangeDraft(PlanningTarget Target, PlanningAction Action, Guid? Id)
{
    public string? Name { get; init; }

    public DateOnly? StartDate { get; init; }

    public DateOnly? EndDate { get; init; }

    public DateOnly? DueDate { get; init; }

    public string? Description { get; init; }
}

/// <summary>週期或里程碑的完整內容（建立建議時的）。週期的 <see cref="DueDate"/>、<see cref="Description"/> 是 null，里程碑的開始、結束是 null。</summary>
public sealed record PlanningValues(string Name, DateOnly? StartDate, DateOnly? EndDate, DateOnly? DueDate, string? Description);

/// <param name="Id">修改、刪除的對象；新增的在套用之前是 null，套用成功後是新的 id。</param>
/// <param name="Before">修改、刪除才有：建立建議時的內容。</param>
/// <param name="After">新增、修改才有：套用後的內容。</param>
/// <param name="ItemCount">刪除才有：建立建議時裡面的項目數（刪掉之後這些項目變成沒有週期或里程碑）。</param>
/// <param name="Error">套用後這一筆沒成功的原因；還沒套用或成功是 null。</param>
public sealed record PlanningChange(
    PlanningTarget Target,
    PlanningAction Action,
    Guid? Id,
    PlanningValues? Before,
    PlanningValues? After,
    int? ItemCount,
    string? Error);

public sealed record PlanningProposal(
    Guid Id, string ProjectKey, IReadOnlyList<PlanningChange> Changes, NewItemProposalStatus Status, DateTimeOffset CreatedAt)
{
    public string? Reason { get; init; }

    public DateTimeOffset? DecidedAt { get; init; }

    /// <summary>有幾筆沒成功，或過程中斷了：摘要，繁體中文。</summary>
    public string? Error { get; init; }
}
