namespace PmTool.Work;

/// <summary>
/// 專案說明：專案的網址、部署環境、主機這類資料，一份 Markdown（internal/HANDOFF-project-info.md）。
/// 存在自己的資料表，不放在專案那一列：每個請求都會讀專案，不該每次都把幾十 KB 的說明一起讀出來。
/// </summary>
/// <param name="Text">Markdown；沒寫過或清掉了是 null。</param>
/// <param name="UpdatedAt">最後一次改的時刻；沒寫過是 null。清掉也算改過，照樣記下時刻與人。</param>
/// <param name="UpdatedBy">最後一次改的人；沒寫過是 null。</param>
public sealed record ProjectDescription(string? Text, DateTimeOffset? UpdatedAt, Guid? UpdatedBy)
{
    public static readonly ProjectDescription Empty = new(null, null, null);
}

/// <summary>說明格式：開始寫專案說明時填進去的 Markdown 架構。套用只是起點，之後改格式不會回頭改已經寫好的說明。</summary>
/// <param name="Id">內建的是固定的 key（例如 web_project）；組織自己建的是編號（第二段才有）。</param>
/// <param name="IsBuiltIn">內建的不能改、不能刪。</param>
/// <param name="IsDefault">開始寫說明、開新專案時預先選好的那一份；清單裡剛好一份是 true。</param>
public sealed record DescriptionTemplate(string Id, string Name, string Body, bool IsBuiltIn, bool IsDefault);

/// <summary>組織管理員新增或修改說明格式時送的內容；修改是整份取代。</summary>
/// <param name="Name">去掉頭尾空白後 1 到 <see cref="ProjectDescriptionRules.MaxTemplateNameLength"/> 個字。</param>
/// <param name="Body">和專案說明同一套檢查（長度、控制字元、私鑰），而且不能是空白：空的格式等於「空白」那個選項。</param>
/// <param name="IsDefault">設成組織的預設；同時把原本的預設取消。修改時從 true 改成 false，組織就沒有自己的預設，回到內建的那一份。</param>
public sealed record DescriptionTemplateInput(string Name, string Body, bool IsDefault);

/// <summary>專案搜尋的一筆：用代號、名稱或專案說明找到的專案。</summary>
/// <param name="Snippet">
/// 說明裡第一個比對到的那一行，給人看出「為什麼找到它」：去掉行首的 Markdown 符號（#、-、*、&gt;、「1.」這類）與頭尾空白，
/// 超過 <see cref="ProjectDescriptionRules.MaxSnippetLength"/> 個字時以比對到的字為中心截斷，被截掉的那一邊加「…」。
/// 代號或名稱就比對到的是 null（不必再看說明）。
/// </param>
public sealed record ProjectSearchHit(ProjectSummary Project, string? Snippet);

public static class ProjectDescriptionRules
{
    /// <summary>搜尋結果摘錄的長度上限：快速跳轉與清單一行放得下。</summary>
    public const int MaxSnippetLength = 80;

    /// <summary>和項目的說明同一個上限。</summary>
    public const int MaxTextLength = 50_000;

    public const int MaxTemplateNameLength = 100;

    /// <summary>一個組織最多幾份自己的說明格式（內建的不算）。選單裡太多就沒辦法挑了。</summary>
    public const int MaxTemplatesPerOrganization = 50;
}
