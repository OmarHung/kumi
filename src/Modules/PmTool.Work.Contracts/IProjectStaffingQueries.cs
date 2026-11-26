using PmTool.Abstractions;

namespace PmTool.Work;

/// <summary>
/// 派工用的查詢：專案有哪些角色（加成員時選角色）、每個人手上還有多少沒結束的項目（選負責人）。AI 助理的 get_project 與網頁都可以用。
/// 和 <see cref="IProjectService"/>、<see cref="IWorkItemService"/> 分開放，是為了不讓各模組測試裡的假服務都得跟著多實作。
/// 規格見 <c>internal/HANDOFF-ai-assign.md</c>。
/// </summary>
public interface IProjectStaffingQueries
{
    /// <summary>
    /// 專案能用的角色：先是內建的五個（admin、editor、contributor、commenter、viewer，照這個順序），再是藍圖的自訂角色（照藍圖的順序）。
    /// 看得到專案就能讀（含只看自己項目的人）；看不到或不存在回 <see cref="WorkErrorCodes.ProjectNotFound"/>。
    /// </summary>
    Task<Result<IReadOnlyList<ProjectRoleInfo>>> ListRolesAsync(Actor actor, string projectKey, CancellationToken cancellationToken);

    /// <summary>
    /// 這個專案裡每個負責人手上還沒結束的項目數（狀態類別不是 done、canceled；已刪除的不算），一次查詢算完。
    /// 只算呼叫的人看得到的項目（<c>ItemScope</c>）：只看自己項目的人只會拿到自己的數字。沒有負責人的項目不算；數字是 0 的人不在結果裡。
    /// 看不到或不存在的專案回 <see cref="WorkErrorCodes.ProjectNotFound"/>。
    /// </summary>
    Task<Result<IReadOnlyDictionary<Guid, int>>> CountOpenByAssigneeAsync(Actor actor, string projectKey, CancellationToken cancellationToken);
}

/// <param name="Key">角色的 key（成員資料的 <see cref="ProjectMember.Role"/>）。</param>
/// <param name="Name">顯示的名稱（內建的是「管理員」「編輯者」「執行者」「留言者」「檢視者」，自訂的是藍圖裡寫的）。</param>
/// <param name="Permissions">這個角色的權限（<see cref="WorkPermissions"/> 的字串），順序和 <see cref="ProjectDetail.MyPermissions"/> 一樣。</param>
public sealed record ProjectRoleInfo(string Key, string Name, bool IsBuiltIn, IReadOnlyList<string> Permissions);
