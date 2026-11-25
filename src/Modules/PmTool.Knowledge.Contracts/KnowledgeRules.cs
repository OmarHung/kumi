namespace PmTool.Knowledge;

public static class KnowledgeRules
{
    /// <summary>空間代號：和專案代號同一個格式（大寫英文字母開頭，只能有大寫字母與數字，2 到 10 個字元），出現在網址裡（/knowledge/HR/12）。</summary>
    public const string SpaceKeyPattern = "^[A-Z][A-Z0-9]{1,9}$";

    public const int MaxSpaceNameLength = 100;

    /// <summary>空間的簡介：空間清單上的一兩行字，不是文件。</summary>
    public const int MaxSpaceDescriptionLength = 500;

    /// <summary>一個組織最多幾個空間（含封存的）。</summary>
    public const int MaxSpacesPerOrganization = 500;

    /// <summary>
    /// 一個空間最多幾頁（不含垃圾桶裡的）。整棵樹一次讀出來給側欄用，太多就要改成一層一層讀；到那時再改。
    /// </summary>
    public const int MaxPagesPerSpace = 5_000;

    /// <summary>樹最多幾層（最上層算第 1 層）。限制住，算繼承的限制時往上找的次數有上限。</summary>
    public const int MaxDepth = 10;

    public const int MaxPageTitleLength = 200;

    /// <summary>頁面內容（Markdown）的上限。比項目的說明（5 萬字）長：文件比說明長，但太長的編輯器會變慢。</summary>
    public const int MaxPageBodyLength = 100_000;

    /// <summary>一頁最多限制給幾個人看。</summary>
    public const int MaxRestrictedPeople = 100;

    /// <summary>和項目的附件同一個上限（Cloud Run 的請求上限是 32 MiB）。</summary>
    public const long MaxAttachmentBytes = 25L * 1024 * 1024;

    public const int MaxAttachmentsPerPage = 200;

    /// <summary>一頁最多留幾個版本；超過時刪掉最舊的。</summary>
    public const int MaxVersionsPerPage = 1_000;

    /// <summary>
    /// 同一個人在這段時間內連續存檔，併成同一個版本（更新那一版的內容與時刻），不每存一次就多一版：
    /// 寫文件的人會一直按儲存，版本清單要看得出「誰在什麼時候改了一輪」。
    /// </summary>
    public static readonly TimeSpan VersionMergeWindow = TimeSpan.FromMinutes(15);

    /// <summary>一個空間的垃圾桶最多幾批（一次刪除算一批）。滿了就不能再刪，要先永久刪除。</summary>
    public const int MaxTrashBatchesPerSpace = 1_000;

    /// <summary>
    /// 一個空間的垃圾桶最多幾頁。一批最多可以有一整棵樹，只限批數擋不住一批塞滿幾千頁。
    /// </summary>
    public const int MaxTrashPagesPerSpace = 20_000;

    public const int DefaultTrashPageSize = 50;

    public const int MaxTrashPageSize = 100;

    /// <summary>清掉到期的批時，一次最多清幾批。</summary>
    public const int MaxBatchesPerPurge = 200;

    /// <summary>清掉到期的批時，一次最多清幾頁：累計超過就停，但至少清一批（一批不能拆）。</summary>
    public const int MaxPagesPerPurge = 2_000;

    /// <summary>刪除超過這麼久的批才會被「清掉到期的」永久刪除。</summary>
    public static readonly TimeSpan TrashPurgeAge = TimeSpan.FromDays(30);
}
