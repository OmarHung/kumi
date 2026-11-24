namespace PmTool.Knowledge;

/// <summary>
/// 知識庫的空間（internal/HANDOFF-knowledge-base.md 第 3 節）。有兩種：
/// 組織空間（<see cref="Project"/> 是 null）組織的擁有者、管理員與一般成員都看得到、都能寫，訪客看不到；
/// 專案空間綁一個專案，看得到專案的人就看得到（含執行者與訪客），能不能寫依他在專案的角色。
/// </summary>
/// <param name="Key">代號（<see cref="KnowledgeRules.SpaceKeyPattern"/>），網址用它；建立後不能改。</param>
/// <param name="Description">簡介；沒寫是 null。</param>
/// <param name="ArchivedAt">封存的時刻；沒封存是 null。封存的空間只能看（綁的專案封存時也一樣，見 <see cref="SpacePermissions"/>）。</param>
public sealed record SpaceSummary(
    Guid Id,
    string Key,
    string Name,
    string? Description,
    SpaceProject? Project,
    DateTimeOffset CreatedAt,
    Guid CreatedBy,
    DateTimeOffset? ArchivedAt,
    SpacePermissions MyPermissions);

/// <summary>專案空間綁的專案（發生當下的代號與名稱）。</summary>
public sealed record SpaceProject(Guid Id, string Key, string Name, bool IsArchived);

/// <summary>
/// 呼叫者在這個空間能做的事（畫面用它決定開放哪些操作）。看得到空間才會拿到這份，所以沒有「能不能看」。
/// 空間或它的專案封存時 <see cref="CanEdit"/> 與 <see cref="CanComment"/> 一律是 false，<see cref="CanManage"/> 照舊（要能取消封存）。
/// </summary>
/// <param name="CanEdit">新增、修改、搬移、刪除與復原頁面、上傳附件、設定頁面的限制。組織空間：擁有者、管理員、一般成員；專案空間：組織管理員或角色有 item.create。</param>
/// <param name="CanComment">留言（第二段）。組織空間：同 CanEdit；專案空間：組織管理員或角色有 comment.create。</param>
/// <param name="CanManage">改空間的名稱與簡介、封存與取消封存。組織空間：組織管理員；專案空間：組織管理員或角色有 project.manage。</param>
public sealed record SpacePermissions(bool CanEdit, bool CanComment, bool CanManage);

/// <param name="Key">去掉頭尾空白、轉成大寫後要合 <see cref="KnowledgeRules.SpaceKeyPattern"/>。</param>
/// <param name="Name">去掉頭尾空白後 1 到 <see cref="KnowledgeRules.MaxSpaceNameLength"/> 個字。</param>
/// <param name="Description">null 或整段空白是沒有。</param>
/// <param name="ProjectKey">給了就是專案空間（綁這個專案）；null 是組織空間。</param>
public sealed record NewSpace(string Key, string Name, string? Description, string? ProjectKey);

/// <summary>整份取代空間的名稱與簡介；代號與綁的專案不能改。</summary>
public sealed record SpaceSettings(string Name, string? Description);
