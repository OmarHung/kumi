namespace PmTool.Ai;

/// <summary>
/// AI 模組的定期清理（API 的 <c>AiMaintenanceWorker</c> 每小時叫一次）：超過 <see cref="AiRules.ConversationRetention"/> 沒更新的對話、
/// 本機代理與主機代理已經結束或放棄、卻還留著整段對話的工作內容。好幾個程序同時跑也安全（刪除是冪等的）。
/// </summary>
public interface IAiMaintenance
{
    /// <summary>跑一輪，回傳刪掉或清掉的筆數（給記錄與測試看）。</summary>
    Task<AiMaintenanceResult> SweepAsync(CancellationToken cancellationToken);
}

/// <param name="Conversations">刪掉的對話數（含它們的訊息）。</param>
/// <param name="JobPayloads">清掉內容的本機代理工作數。</param>
public sealed record AiMaintenanceResult(int Conversations, int JobPayloads);
