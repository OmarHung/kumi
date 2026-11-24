namespace PmTool.Cli.Import.Monday;

// 匯入的對應（internal/HANDOFF-monday-import.md 第 5 節）：plan 產生 YAML 的草稿，使用者改好，run 照它匯入。
// YAML 的鍵是 camelCase；MondayMappingFile.Load 讀成這些 record（含簡寫：群組寫 skip、欄位直接寫欄位的 key）。
// 對應檔裡有真實的群組名稱與 Email，只放在使用者自己的電腦，不進版控。

/// <param name="Source">匯入來源的名字，記在每一筆活動上（時間軸寫「從 monday 匯入」）。</param>
/// <param name="Dump">fetch 的輸出資料夾；相對路徑以對應檔所在的資料夾為準。</param>
/// <param name="People">monday 的人（編號）→ 這裡的人的 Email；null 是對不上（作者改用 <see cref="FallbackPerson"/>）。</param>
/// <param name="FallbackPerson">對不上的作者、建立的人、上傳的人改用這個人（Email），內容前面註明原作者；null 時對不上就停下來。</param>
/// <param name="IncludeArchived">monday 上封存的項目也匯入；預設不匯。刪除的（deleted）一律不匯。</param>
internal sealed record MondayMapping
{
    public string Source { get; init; } = "monday";

    public required string Dump { get; init; }

    public IReadOnlyDictionary<string, string?> People { get; init; } = new Dictionary<string, string?>();

    public string? FallbackPerson { get; init; }

    public AssigneeRule Assignee { get; init; } = new();

    /// <summary>留過言的人自動關注那個項目（對得上的人）。</summary>
    public bool FollowCommenters { get; init; } = true;

    public bool IncludeArchived { get; init; }

    public IReadOnlyDictionary<string, BoardMapping> Boards { get; init; } = new Dictionary<string, BoardMapping>();
}

/// <summary>
/// 負責人怎麼選：負責人欄裡對得上、而且是專案成員的人，去掉 <see cref="Exclude"/>（例如 PM；去掉之後沒人時還是用他），
/// 取在這個項目留言最多的人，一樣多時取欄位裡排前面的。欄位裡的其他人變成關注的人。
/// </summary>
internal sealed record AssigneeRule
{
    public IReadOnlyList<string> Exclude { get; init; } = [];
}

/// <param name="StatusColumn">狀態的欄（status）。</param>
/// <param name="OwnerColumn">負責人的欄（people）。</param>
/// <param name="DueColumn">到期日的欄（date）。</param>
/// <param name="TimelineColumn">期間的欄（timeline）：from 是開始日；沒有 <paramref name="DueColumn"/> 的值時 to 是到期日。</param>
/// <param name="PriorityColumn">優先度的欄（status）。</param>
/// <param name="Priorities">優先度的標籤 → none、low、medium、high、urgent；對不上的當成 none 並提醒。</param>
/// <param name="States">狀態的標籤 → 狀態的 key（整個看板的預設，群組可以另外給）；空的標籤用 ""。</param>
/// <param name="Fields">monday 的欄（編號）→ 自訂欄位。</param>
/// <param name="LinkField">每個項目寫上 monday 原連結的欄位（url 欄位的 key）；null 是不寫。</param>
/// <param name="Stale">很久沒動、還沒結束的項目改用的狀態；null 是不處理（項目照狀態對應匯入）。</param>
/// <param name="Groups">群組（編號）→ 怎麼匯；沒列的群組不匯（和 skip 一樣，但會提醒）。</param>
internal sealed record BoardMapping
{
    public string? StatusColumn { get; init; }

    public string? OwnerColumn { get; init; }

    public string? DueColumn { get; init; }

    public string? TimelineColumn { get; init; }

    public string? PriorityColumn { get; init; }

    public IReadOnlyDictionary<string, string> Priorities { get; init; } = new Dictionary<string, string>();

    public IReadOnlyDictionary<string, string> States { get; init; } = new Dictionary<string, string>();

    public IReadOnlyDictionary<string, FieldMapping> Fields { get; init; } = new Dictionary<string, FieldMapping>();

    public string? LinkField { get; init; }

    public SubitemMapping? Subitems { get; init; }

    public StaleRule? Stale { get; init; }

    /// <summary>依標題的關鍵字補類型與欄位的值（照順序，第一條符合的生效）；見 <see cref="TitleRule"/>。</summary>
    public IReadOnlyList<TitleRule> TitleRules { get; init; } = [];

    public IReadOnlyDictionary<string, GroupMapping> Groups { get; init; } = new Dictionary<string, GroupMapping>();
}

/// <summary>
/// 依標題的關鍵字決定類型、補欄位的值：monday 上維運的項目沒有「類型」，很多也沒有填客戶，但標題常常寫得出來
/// （「Example 憑證更新」「Sample 會員名單下載功能失效」）。
/// <list type="bullet">
/// <item>標題（不分大小寫）包含 <see cref="Match"/> 的任一個字就算符合；<see cref="Project"/> 有給時只對匯進那個專案的項目。</item>
/// <item>類型：照順序第一條符合、而且有 <see cref="Type"/> 的規則決定；都不符合時用群組的類型。只對項目，不對子項目。</item>
/// <item>欄位：每個欄位各自照順序找第一條符合、而且給了那個欄位的規則；只在那個欄位還沒有值時補（monday 的欄、群組的 <see cref="GroupMapping.Fields"/> 優先）。</item>
/// </list>
/// </summary>
internal sealed record TitleRule
{
    public string? Project { get; init; }

    public required IReadOnlyList<string> Match { get; init; }

    public string? Type { get; init; }

    /// <summary>欄位的 key → 值（寫法同 <see cref="GroupMapping.Fields"/>）。</summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// 一個 monday 欄 → 一個自訂欄位。值怎麼轉看兩邊的種類（第 6.3 節）。<see cref="Values"/> 給單選、多選用：
/// monday 的標籤或關聯項目的名稱 → 選項的 key；對不上的略過並提醒。YAML 可以簡寫成「欄的編號: 欄位的 key」。
/// </summary>
internal sealed record FieldMapping
{
    public required string Field { get; init; }

    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();
}

/// <summary>子項目變成上層項目底下的項目。欄的編號是子項目看板的；狀態沒給時用看板的 <see cref="BoardMapping.States"/>。</summary>
internal sealed record SubitemMapping
{
    public required string Type { get; init; }

    public string? StatusColumn { get; init; }

    public string? OwnerColumn { get; init; }

    public string? DueColumn { get; init; }

    public string? TimelineColumn { get; init; }

    public IReadOnlyDictionary<string, string>? States { get; init; }
}

/// <summary>一個群組怎麼匯。YAML 可以簡寫成「群組的編號: skip」。</summary>
/// <param name="Project">匯進哪個專案（代號）；專案要先在網頁上建好。</param>
/// <param name="Type">項目的類型（key）。</param>
/// <param name="Cycle">放進哪個週期；專案裡沒有這個名字的週期時 run 會建立。</param>
/// <param name="States">這個群組自己的狀態對應（蓋過看板的）。</param>
internal sealed record GroupMapping
{
    public bool Skip { get; init; }

    public string? Project { get; init; }

    public string? Type { get; init; }

    public CycleMapping? Cycle { get; init; }

    public IReadOnlyDictionary<string, string>? States { get; init; }

    /// <summary>
    /// 這個群組的項目的欄位預設值：欄位的 key → 值，monday 的欄沒有值時才用。單選是選項的 key，多選用逗號隔開幾個選項的 key，
    /// 勾選是 true／false，數字是數字，其他照字串。例：長期服務的群組（某個客戶的維護）搬進維運專案，「客戶」整組都是同一個。
    /// </summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// 週期。YAML 寫 <c>cycle: monthly</c> 時 <see cref="Monthly"/> 是 true、其他是空的：每個項目依它在 monday 建立的月份（組織的時區）
/// 放進「2026 年 11 月」那樣的週期（和每月的維運群組同一套名字，沒有就建立）；子項目跟著上層。
/// 長期服務的群組（一個客戶好幾年的維護）搬進維運專案時用，每個月的工時才算得到那個月。
/// </summary>
internal sealed record CycleMapping(string Name, DateOnly Start, DateOnly End)
{
    public static CycleMapping MonthlyByCreation { get; } = new(string.Empty, default, default) { Monthly = true };

    public bool Monthly { get; init; }
}

/// <summary>
/// 很久沒動的項目：最後一次動（建立、留言、回覆裡最晚的）早於 <paramref name="Before"/>（組織時區的那一天 00:00，當天不算），
/// 而且照狀態對應算出來還沒結束（類別是 backlog、not_started、in_progress）時，狀態改用 <paramref name="State"/>。
/// 不然舊的待辦會全部擠進每個人的「我的工作」的「已過期」。
/// </summary>
internal sealed record StaleRule(DateOnly Before, string State);
