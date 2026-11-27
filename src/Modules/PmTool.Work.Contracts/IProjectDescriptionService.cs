using PmTool.Abstractions;

namespace PmTool.Work;

/// <summary>專案說明與說明格式（internal/HANDOFF-project-info.md）。</summary>
public interface IProjectDescriptionService
{
    /// <summary>
    /// 讀專案說明。看得到專案的人都能讀，包含只看自己項目的角色（執行者要知道部署到哪裡）。
    /// 看不到的專案和不存在的一樣回 NotFound。沒寫過回 <see cref="ProjectDescription.Empty"/>。
    /// </summary>
    Task<Result<ProjectDescription>> GetAsync(Actor actor, string projectKey, CancellationToken cancellationToken);

    /// <summary>
    /// 改專案說明。需要 project.manage 或組織管理員；封存的專案也可以改（網站上線之後還在跑，主機照樣會換）。
    /// text 是 null 或整段空白就是清掉（存成 null）。其他的照原樣存，換行與縮排都保留，不去掉頭尾空白。
    /// 和現在的內容一樣時什麼都不做，更新的時刻與人不變。
    /// 權限先於內容的檢查。內容的問題（路徑都是 text）：
    /// 超過 <see cref="ProjectDescriptionRules.MaxTextLength"/> 字是 <see cref="WorkErrorCodes.ProjectDescriptionTooLong"/>；
    /// 有 NUL、換行與 Tab 以外的控制字元是 <see cref="WorkErrorCodes.ProjectDescriptionInvalid"/>；
    /// 有私鑰是 <see cref="WorkErrorCodes.ProjectDescriptionSecret"/>。
    /// </summary>
    Task<Result<ProjectDescription>> SetAsync(Actor actor, string projectKey, string? text, CancellationToken cancellationToken);

    /// <summary>
    /// 同 <see cref="SetAsync"/>，但只在現在的 <see cref="ProjectDescription.UpdatedAt"/> 等於 <paramref name="expectedUpdatedAt"/> 時才寫
    /// （null 表示「還沒寫過」）；比對與寫入在同一個交易裡，不會被同時的寫入蓋掉。
    /// 不同就回 <see cref="WorkErrorCodes.ProjectDescriptionConflict"/>（Conflict），什麼都不改；權限與內容的檢查照 <see cref="SetAsync"/>，先於這個比對。
    /// 預設實作丟 <see cref="NotSupportedException"/>，讓各處的假物件不必跟著改。
    /// </summary>
    Task<Result<ProjectDescription>> SetIfUnchangedAsync(
        Actor actor, string projectKey, string? text, DateTimeOffset? expectedUpdatedAt, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    /// <summary>
    /// 說明格式：組織自己的在前（預設的那一份最前面，其餘依名稱），內建的在後。組織的成員都能讀。
    /// 剛好一份 <see cref="DescriptionTemplate.IsDefault"/>：組織指定的那一份，沒有指定就是內建的「網站案子」。
    /// </summary>
    Task<IReadOnlyList<DescriptionTemplate>> ListTemplatesAsync(Actor actor, CancellationToken cancellationToken);

    /// <summary>
    /// 新增組織的說明格式，回傳新的那一份（<see cref="DescriptionTemplate.Id"/> 是編號的字串）。只有組織管理員可以做（其他人 <see cref="WorkErrorCodes.Forbidden"/>）。
    /// 權限先於內容的檢查。問題：名稱空白 <see cref="WorkErrorCodes.DescriptionTemplateNameRequired"/>、太長 <see cref="WorkErrorCodes.DescriptionTemplateNameTooLong"/>
    /// （路徑 name；名稱有換行、控制字元或看不見的格式字元也算 name_required）；內容空白 <see cref="WorkErrorCodes.DescriptionTemplateBodyRequired"/>，
    /// 內容的長度、控制字元、私鑰用專案說明的三個代碼（路徑 body）。這三種驗證問題一次回齊。
    /// 和組織裡另一份或內建的格式同名（不分大小寫）是 Conflict <see cref="WorkErrorCodes.DescriptionTemplateNameTaken"/>；
    /// 已經有 <see cref="ProjectDescriptionRules.MaxTemplatesPerOrganization"/> 份是 Conflict <see cref="WorkErrorCodes.DescriptionTemplateLimitReached"/>。
    /// 同一個組織同時新增、修改、刪除時排隊做，上限與「最多一份預設」不會被同時的寫入繞過。
    /// </summary>
    Task<Result<DescriptionTemplate>> CreateTemplateAsync(Actor actor, DescriptionTemplateInput input, CancellationToken cancellationToken);

    /// <summary>
    /// 整份取代組織的一份說明格式，規則同新增（同名的檢查不含它自己）。不是組織管理員是 Forbidden；
    /// 不存在、在別的組織、或是內建的（內建的不能改），一律 <see cref="WorkErrorCodes.DescriptionTemplateNotFound"/>。
    /// 已經寫好的專案說明不跟著改。
    /// </summary>
    Task<Result<DescriptionTemplate>> UpdateTemplateAsync(
        Actor actor, Guid templateId, DescriptionTemplateInput input, CancellationToken cancellationToken);

    /// <summary>
    /// 刪除組織的一份說明格式，回傳刪掉前的內容。不是組織管理員是 Forbidden；找不到是 NotFound。
    /// 刪掉的是預設時，組織回到沒有自己的預設（內建的成為預設）。已經寫好的專案說明不受影響。
    /// </summary>
    Task<Result<DescriptionTemplate>> DeleteTemplateAsync(Actor actor, Guid templateId, CancellationToken cancellationToken);

    /// <summary>
    /// 在呼叫者看得到的專案裡（和 <see cref="IProjectService.ListAsync"/> 同一個範圍，含封存的），找代號、名稱或專案說明包含這段文字的
    /// （不分大小寫；%、_、\ 這些字照字面比對，不是萬用字元）。用途：輸入主機名稱、網址，查出哪些專案在那裡。
    /// 排序：代號完全符合、代號或名稱以它開頭、代號或名稱包含、只有說明包含；同一層裡沒封存的在前，再依代號。
    /// 放在這個服務：說明的資料表與摘錄的規則在這裡。
    /// <paramref name="text"/> 去掉頭尾空白後要有 1 到 200 個字、不能有控制字元；<paramref name="limit"/> 是 1 到 20；
    /// 不合格是 <see cref="WorkErrorCodes.QueryInvalid"/>，路徑是 q 或 limit（和項目的搜尋一樣）。
    /// </summary>
    Task<Result<IReadOnlyList<ProjectSearchHit>>> SearchProjectsAsync(
        Actor actor, string text, int limit, CancellationToken cancellationToken);
}
