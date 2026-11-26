using PmTool.Abstractions;

namespace PmTool.Ai;

/// <summary>
/// 派工時給模型看的資料：一個專案的摘要（get_project）與組織裡的人（list_people）。內建助理的工具與 MCP 的同名工具都用這一份，
/// 規則（看得到什麼、名單怎麼排）只有一份；兩邊只負責把它排成 JSON。規格見 <c>internal/HANDOFF-ai-assign.md</c>。
/// </summary>
public interface IStaffingBriefService
{
    /// <summary>
    /// 專案的摘要。看不到的專案原樣回傳 <c>project.not_found</c>；看得到就能讀（含只看自己項目的人：那時 <see cref="ProjectBriefMember.OpenItems"/> 只算得到自己的）。
    /// 讀不到週期、里程碑（沒有 item.read 的人）時那兩份是空的，不算失敗。
    /// </summary>
    Task<Result<ProjectBrief>> GetProjectAsync(Actor actor, string projectKey, CancellationToken cancellationToken);

    /// <summary>
    /// 組織裡的人（帳號停用的不列），依顯示名稱排序，最多 <see cref="PeopleQuery.Limit"/> 個。發問的人是訪客時回 <see cref="AiErrorCodes.Forbidden"/>
    /// （訊息「訪客看不到組織的人員名單。」，和 <c>GET /v1/people</c> 擋訪客同一個理由）。
    /// 不回 Email：模型用人員編號就夠了，Email 不必送給 AI 供應商。
    /// </summary>
    Task<Result<IReadOnlyList<PersonBrief>>> ListPeopleAsync(Actor actor, PeopleQuery query, CancellationToken cancellationToken);
}

/// <param name="ProjectKey">給了的話，每個人帶他在這個專案的角色（<see cref="PersonBrief.ProjectRole"/>）；專案要看得到，不然原樣回傳 <c>project.not_found</c>。</param>
/// <param name="Text">給了的話只列顯示名稱、職稱或負責範圍包含這段文字的（不分大小寫）。</param>
/// <param name="Limit">1 到 <see cref="AiRules.MaxPeopleListed"/>。</param>
public sealed record PeopleQuery(string? ProjectKey, string? Text, int Limit);

/// <param name="OrganizationRole">組織裡的角色：<c>owner</c>、<c>admin</c>、<c>member</c>、<c>guest</c>（小寫，和 API 一樣）。</param>
/// <param name="ProjectRole">在 <see cref="PeopleQuery.ProjectKey"/> 那個專案的角色 key；不是成員或沒給專案是 null。</param>
public sealed record PersonBrief(
    Guid PersonId, string DisplayName, string OrganizationRole, string? JobTitle, string? Responsibilities, string? ProjectRole);

/// <param name="MyRole">發問的人在這個專案的角色；組織管理員不是成員時是 null。</param>
/// <param name="MyPermissions">發問的人在這個專案能做的事（和 <c>ProjectDetail.MyPermissions</c> 一樣）。</param>
/// <param name="ItemTypes">能建立的項目類型（藍圖的順序）。</param>
/// <param name="Members">專案成員，帳號停用的不列，依顯示名稱排序。</param>
/// <param name="Roles">專案能用的角色（<c>IProjectStaffingQueries.ListRolesAsync</c>）。</param>
/// <param name="MemberManagers">能管理成員的成員的名稱（角色有 project.manage、帳號沒停用），最多 10 個；組織管理員另外也能，不列在這裡。</param>
/// <param name="Cycles">結束日不早於今天（組織時區）的週期，依開始日排序，最多 10 個。</param>
/// <param name="Milestones">到期日不早於今天（組織時區）的里程碑，依到期日排序，最多 10 個。</param>
public sealed record ProjectBrief(
    string Key,
    string Name,
    bool IsArchived,
    string? MyRole,
    IReadOnlyList<string> MyPermissions,
    IReadOnlyList<ProjectBriefItemType> ItemTypes,
    IReadOnlyList<ProjectBriefMember> Members,
    IReadOnlyList<ProjectBriefRole> Roles,
    IReadOnlyList<string> MemberManagers,
    IReadOnlyList<ProjectBriefCycle> Cycles,
    IReadOnlyList<ProjectBriefMilestone> Milestones);

/// <param name="Parents">這個類型能掛在哪些類型底下（藍圖的 <c>ItemType.Parents</c>）；空的是不能有上層。</param>
/// <param name="FirstState">新建的項目一開始的狀態（流程的第一個狀態）的名稱。</param>
/// <param name="Fields">這個類型的自訂欄位。</param>
public sealed record ProjectBriefItemType(
    string Key, string Name, IReadOnlyList<string> Parents, string FirstState, IReadOnlyList<ProjectBriefField> Fields);

/// <param name="Kind">藍圖的欄位型別（text、number、select…，照藍圖的寫法）。</param>
/// <param name="Options">選項類欄位的選項 key；其他是空的。</param>
public sealed record ProjectBriefField(string Key, string Name, string Kind, bool Required, IReadOnlyList<string> Options);

/// <param name="OpenItems">
/// 他在這個專案手上還沒結束的項目數（<c>IProjectStaffingQueries.CountOpenByAssigneeAsync</c>）。發問的人只看得到自己的項目（沒有 item.read）時，
/// 別人的是 null、只有自己的有數字：看不到不等於 0，給 0 模型會以為那個人很閒而偏向派給他。
/// </param>
public sealed record ProjectBriefMember(
    Guid PersonId, string DisplayName, string Role, string? JobTitle, string? Responsibilities, int? OpenItems);

/// <summary>專案能用的角色（<c>IProjectStaffingQueries.ListRolesAsync</c> 的一筆）。</summary>
/// <param name="Permissions">這個角色的權限（item.read、item.read_own、project.manage…），模型用它判斷「執行者只看得到自己的項目」這類差別。</param>
public sealed record ProjectBriefRole(string Key, string Name, bool IsBuiltIn, IReadOnlyList<string> Permissions);

public sealed record ProjectBriefCycle(Guid Id, string Name, DateOnly StartDate, DateOnly EndDate);

public sealed record ProjectBriefMilestone(Guid Id, string Name, DateOnly DueDate);
