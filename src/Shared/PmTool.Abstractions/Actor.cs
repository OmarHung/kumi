namespace PmTool.Abstractions;

/// <summary>
/// 誰在做這件事。API 從登入的工作階段組出來交給模組，模組用它做權限檢查；
/// 模組自己不碰 HTTP，之後 CLI、MCP 走的也是同一套檢查。
/// </summary>
/// <param name="IsOrganizationAdmin">組織的擁有者或管理員。</param>
public sealed record Actor(Guid OrganizationId, Guid PersonId, bool IsOrganizationAdmin);
